using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WFAM.App.Models;

namespace WFAM.App.Services;

public sealed class UpdateService : IUpdateService, IDisposable
{
    // ⚠️ 仓库坐标。后续若易主请同步修改 RepositoryUrl 与 ApiLatestUrl。
    private const string Owner = "DusklitSakura";
    private const string Repo = "folder-alias-manager";

    private static readonly Uri ApiLatestUrl =
        new($"https://api.github.com/repos/{Owner}/{Repo}/releases/latest");

    private readonly ILogger<UpdateService> _logger;
    private readonly HttpClient _http;

    public UpdateService(ILogger<UpdateService> logger)
    {
        _logger = logger;
        _http = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All
        })
        {
            Timeout = TimeSpan.FromSeconds(15),
        };
        // GitHub API 强制要求 UA。
        _http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("WFAM", CurrentVersion.ToString(3)));
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    public Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

    public string RepositoryUrl => $"https://github.com/{Owner}/{Repo}";

    public async Task<UpdateInfo?> CheckAsync(bool onlyIfNewer, CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.GetAsync(ApiLatestUrl, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogInformation("检查更新返回 HTTP {code}", (int)resp.StatusCode);
                return null;
            }

            await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);
            var root = doc.RootElement;

            string tag = root.TryGetProperty("tag_name", out var t) ? (t.GetString() ?? "") : "";
            string html = root.TryGetProperty("html_url", out var h) ? (h.GetString() ?? RepositoryUrl) : RepositoryUrl;
            string? body = root.TryGetProperty("body", out var b) ? b.GetString() : null;
            DateTimeOffset? pub = root.TryGetProperty("published_at", out var p) && p.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(p.GetString(), out var dt) ? dt : null;
            bool isPrerelease = root.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True;
            bool isDraft = root.TryGetProperty("draft", out var dr) && dr.ValueKind == JsonValueKind.True;

            if (isDraft)
                return null; // 草稿版本不应被普通用户看到

            if (!TryParseTag(tag, out var ver))
            {
                _logger.LogInformation("无法解析 tag_name 为版本号：{tag}", tag);
                return null;
            }

            string? asset = null;
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in assets.EnumerateArray())
                {
                    var name = a.TryGetProperty("name", out var n) ? n.GetString() : null;
                    var url = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(url)) continue;
                    if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                        || name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase)
                        || name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    {
                        asset = url;
                        break;
                    }
                }
            }

            if (onlyIfNewer && ver <= CurrentVersion)
                return null;

            // 标记 pre-release 时仅在 onlyIfNewer=false 时返回（手动检查时让用户看到）
            if (isPrerelease && onlyIfNewer)
                return null;

            return new UpdateInfo(ver, tag, html, body, pub, asset);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "检查更新失败");
            return null;
        }
    }

    /// <summary>把 "v1.2.3" / "1.2" / "1.2.3.4" 解析为 <see cref="System.Version"/>。</summary>
    private static bool TryParseTag(string? tag, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(tag)) return false;
        var s = tag.Trim();
        if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase) ||
            s.StartsWith("V", StringComparison.OrdinalIgnoreCase))
            s = s[1..];
        // 截断 pre-release 后缀: 1.2.3-beta -> 1.2.3
        var dash = s.IndexOf('-');
        if (dash > 0) s = s[..dash];
        var plus = s.IndexOf('+');
        if (plus > 0) s = s[..plus];
        return Version.TryParse(s, out version!);
    }

    public async Task<UpdateStaging> DownloadAsync(UpdateInfo info, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(info.PrimaryAssetUrl))
            throw new InvalidOperationException("Release 中未找到可下载的 .zip / .exe / .msi 资产。");

        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WFAM", "update", info.Version.ToString());

        // 清理旧暂存
        if (Directory.Exists(dir))
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
        Directory.CreateDirectory(dir);

        var fileName = SanitizeAssetFileName(info.PrimaryAssetUrl);
        var downloadPath = Path.Combine(dir, fileName);

        // 下载
        using (var resp = await _http.GetAsync(info.PrimaryAssetUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
        {
            resp.EnsureSuccessStatusCode();
            var total = resp.Content.Headers.ContentLength ?? -1L;
            await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var dst = File.Create(downloadPath);
            var buffer = new byte[81920];
            long read = 0;
            int n;
            while ((n = await src.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
                read += n;
                if (total > 0 && progress is not null)
                    progress.Report((double)read / total);
            }
        }

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (ext == ".zip")
        {
            // 解压到 dir/extracted
            var extractDir = Path.Combine(dir, "extracted");
            Directory.CreateDirectory(extractDir);
            ZipFile.ExtractToDirectory(downloadPath, extractDir, overwriteFiles: true);

            // 若 zip 顶层只有一个文件夹（例如 WFAM-1.2.3/），把它当作根目录
            var top = Directory.GetFileSystemEntries(extractDir);
            if (top.Length == 1 && Directory.Exists(top[0]))
                extractDir = top[0];

            return new UpdateStaging(extractDir, UpdateStagingKind.ZipExtracted, extractDir);
        }

        // 直接是 .exe / .msi —— 当作独立安装包对待
        return new UpdateStaging(dir, UpdateStagingKind.Installer, downloadPath);
    }

    public void ApplyAndRestart(UpdateStaging staging)
    {
        if (staging.Kind == UpdateStagingKind.Installer)
        {
            // 直接启动安装包，由它处理替换/重启；本进程退出。
            var ext = Path.GetExtension(staging.PrimaryFilePath).ToLowerInvariant();
            ProcessStartInfo psi = ext switch
            {
                ".msi" => new ProcessStartInfo("msiexec.exe", $"/i \"{staging.PrimaryFilePath}\"") { UseShellExecute = true },
                _ => new ProcessStartInfo(staging.PrimaryFilePath) { UseShellExecute = true },
            };
            Process.Start(psi);
            return;
        }

        // ZipExtracted 模式：写一个 PowerShell 脚本 → 等待本进程退出 → 复制覆盖 → 重启
        var pid = Environment.ProcessId;
        var installDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var sourceDir = staging.PrimaryFilePath.TrimEnd(Path.DirectorySeparatorChar);
        var exeName = Path.GetFileName(Process.GetCurrentProcess().MainModule?.FileName ?? "WFAM.exe");
        var scriptPath = Path.Combine(Path.GetTempPath(), $"wfam_update_{pid}_{Guid.NewGuid():N}.ps1");
        var logPath = Path.Combine(Path.GetTempPath(), $"wfam_update_{pid}.log");

        // 自我清理：脚本完成后删除自身
        var script = new StringBuilder();
        script.AppendLine("$ErrorActionPreference = 'SilentlyContinue'");
        script.AppendLine($"Start-Transcript -Path '{logPath}' -Force | Out-Null");
        script.AppendLine($"$pidToWait = {pid}");
        script.AppendLine("for ($i=0; $i -lt 60; $i++) {");
        script.AppendLine("  $p = Get-Process -Id $pidToWait -ErrorAction SilentlyContinue");
        script.AppendLine("  if (-not $p) { break }");
        script.AppendLine("  Start-Sleep -Milliseconds 500");
        script.AppendLine("}");
        // 再保险一下
        script.AppendLine("Start-Sleep -Milliseconds 800");
        script.AppendLine($"$src = '{sourceDir.Replace("'", "''")}'");
        script.AppendLine($"$dst = '{installDir.Replace("'", "''")}'");
        // 用 robocopy 镜像/合并，/XO 不覆盖更新的文件、/R:2 重试 2 次。MIR 会删除目标多余文件，保守起见用普通模式。
        script.AppendLine("robocopy $src $dst /E /R:2 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null");
        script.AppendLine($"$exe = Join-Path $dst '{exeName.Replace("'", "''")}'");
        script.AppendLine("if (Test-Path $exe) { Start-Process -FilePath $exe }");
        script.AppendLine("Stop-Transcript | Out-Null");
        // 自删
        script.AppendLine("Remove-Item -LiteralPath $MyInvocation.MyCommand.Path -Force -ErrorAction SilentlyContinue");

        File.WriteAllText(scriptPath, script.ToString(), new UTF8Encoding(false));

        var psi2 = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        Process.Start(psi2);
    }

    private static string SanitizeAssetFileName(string url)
    {
        try
        {
            var uri = new Uri(url);
            var name = Path.GetFileName(uri.LocalPath);
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            if (string.IsNullOrEmpty(name)) name = "asset.bin";
            return name;
        }
        catch
        {
            return "asset.bin";
        }
    }

    public void Dispose() => _http.Dispose();
}
