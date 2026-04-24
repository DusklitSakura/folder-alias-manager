using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WFAM.Helper;

/// <summary>
/// DesktopIniHelper —— 提权辅助进程。
/// 由主程序通过 ShellExecuteEx + "runas" 调用，
/// 从 JSON 输入文件批量写入 desktop.ini，并将结果写回 JSON。
/// </summary>
internal static class Program
{
    private const uint SHCNE_ASSOCCHANGED = 0x08000000;
    private const uint SHCNF_IDLIST = 0x0000;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    public static int Main(string[] args)
    {
        // .NET (Core) 默认不包含 GB2312/GBK 等传统代码页，需要手动注册
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        try
        {
            // 拒绝非 WFAM.exe 调用。Helper 以管理员权限运行，
            // 必须确保只有同目录下的主程序能驱动它，避免被任意进程当作提权跳板。
            SecurityGuard.EnsureTrustedCaller();

            if (args.Length >= 2 && args[0] == "--batch")
            {
                return RunBatch(args[1]);
            }

            if (args.Length >= 2 && args[0] == "--batch-autorun")
            {
                return RunAutorunBatch(args[1]);
            }

            Console.Error.WriteLine("Usage: DesktopIniHelper.exe --batch <input-json>");
            Console.Error.WriteLine("       DesktopIniHelper.exe --batch-autorun <input-json>");
            return 2;
        }
        catch (SecurityException ex)
        {
            Console.Error.WriteLine("[security] " + ex.Message);
            return 87; // ERROR_INVALID_PARAMETER
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static int RunBatch(string rawInputFile)
    {
        // 1) 校验输入文件路径（必须位于 %TEMP%、文件名符合 wfam_in_<pid>_<guid>.json）
        var inputFile = SecurityGuard.ValidateInputFile(rawInputFile);

        var json = File.ReadAllText(inputFile, Encoding.UTF8);
        var input = JsonSerializer.Deserialize<BatchInput>(json, JsonOpts) ?? new BatchInput();

        // 2) 校验输出文件：必须与输入同目录，文件名符合 wfam_out_<pid>_<guid>.json
        var outputFile = SecurityGuard.ValidateOutputFile(input.OutputFile, inputFile);

        var results = new List<BatchResultItem>();
        foreach (var item in input.Items)
        {
            bool ok = false;
            string? message = null;
            try
            {
                // 3) 校验目标文件夹：拒绝系统受保护目录、根盘、reparse point
                if (!SecurityGuard.IsFolderAllowed(item.FolderPath, out var reason))
                {
                    message = $"拒绝目录：{reason}";
                    Console.Error.WriteLine($"[{item.Name}] {message}（{item.FolderPath}）");
                }
                else if (item.Restore)
                {
                    ok = DesktopIniWriter.Restore(item.FolderPath);
                    if (!ok) message = "恢复默认失败";
                }
                else
                {
                    ok = DesktopIniWriter.Write(
                        item.FolderPath,
                        item.Alias,
                        string.IsNullOrEmpty(item.IconPath) ? null : item.IconPath,
                        item.IconIndex);
                    if (!ok) message = "写入 desktop.ini 失败";
                }
            }
            catch (Exception ex)
            {
                ok = false;
                message = ex.Message;
                Console.Error.WriteLine($"[{item.Name}] {ex.Message}");
            }
            results.Add(new BatchResultItem { FolderPath = item.FolderPath, Name = item.Name, Success = ok, Message = message ?? string.Empty });
        }

        if (!string.IsNullOrEmpty(outputFile))
        {
            File.WriteAllText(
                outputFile,
                JsonSerializer.Serialize(new BatchOutput { Results = results }, JsonOpts),
                Encoding.UTF8);
        }

        SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
        return 0;
    }

    private static int RunAutorunBatch(string rawInputFile)
    {
        var inputFile = SecurityGuard.ValidateInputFile(rawInputFile);
        var json = File.ReadAllText(inputFile, Encoding.UTF8);
        var input = JsonSerializer.Deserialize<AutorunBatchInput>(json, JsonOpts) ?? new AutorunBatchInput();
        var outputFile = SecurityGuard.ValidateOutputFile(input.OutputFile, inputFile);

        var results = new List<BatchResultItem>();
        foreach (var item in input.Items)
        {
            bool ok = false;
            string? message = null;
            try
            {
                if (!SecurityGuard.IsDriveRootAllowed(item.DrivePath, out var reason))
                {
                    message = $"拒绝目录：{reason}";
                    Console.Error.WriteLine($"[{item.Name}] {message}（{item.DrivePath}）");
                }
                else if (!SecurityGuard.IsValidIconTargetName(item.IconTargetName, out var nameReason))
                {
                    message = $"非法图标文件名：{nameReason}";
                    Console.Error.WriteLine($"[{item.Name}] {message}");
                }
                else if (item.Restore)
                {
                    ok = AutorunInfWriter.Restore(item.DrivePath, item.IconTargetName);
                    if (!ok) message = "恢复默认失败";
                }
                else
                {
                    string? staged = string.IsNullOrEmpty(item.StagedIconPath) ? null : item.StagedIconPath;
                    if (!string.IsNullOrEmpty(staged))
                    {
                        // 校验 staged icon 必须在 %TEMP%（防止任意路径复制到驱动器根）
                        try { SecurityGuard.ValidateStagedIconFile(staged); }
                        catch (SecurityException sex) { message = sex.Message; staged = null; }
                    }

                    if (message is null)
                    {
                        ok = AutorunInfWriter.Write(
                            item.DrivePath,
                            item.Label ?? string.Empty,
                            staged,
                            item.IconTargetName);
                        if (!ok) message = "写入 autorun.inf 失败";
                    }
                }
            }
            catch (Exception ex)
            {
                ok = false;
                message = ex.Message;
                Console.Error.WriteLine($"[{item.Name}] {ex.Message}");
            }
            results.Add(new BatchResultItem
            {
                FolderPath = item.DrivePath,
                Name = item.Name,
                Success = ok,
                Message = message ?? string.Empty,
            });
        }

        if (!string.IsNullOrEmpty(outputFile))
        {
            File.WriteAllText(
                outputFile,
                JsonSerializer.Serialize(new BatchOutput { Results = results }, JsonOpts),
                Encoding.UTF8);
        }

        SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
        return 0;
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private sealed class BatchInput
    {
        [JsonPropertyName("items")] public List<BatchInputItem> Items { get; set; } = new();
        [JsonPropertyName("output_file")] public string OutputFile { get; set; } = string.Empty;
    }

    private sealed class BatchInputItem
    {
        [JsonPropertyName("folder_path")] public string FolderPath { get; set; } = string.Empty;
        [JsonPropertyName("alias")] public string Alias { get; set; } = string.Empty;
        [JsonPropertyName("icon_path")] public string IconPath { get; set; } = string.Empty;
        [JsonPropertyName("icon_index")] public int IconIndex { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("restore")] public bool Restore { get; set; }
    }

    private sealed class BatchOutput
    {
        [JsonPropertyName("results")] public List<BatchResultItem> Results { get; set; } = new();
    }

    private sealed class BatchResultItem
    {
        [JsonPropertyName("folder_path")] public string FolderPath { get; set; } = string.Empty;
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("success")] public bool Success { get; set; }
        [JsonPropertyName("message")] public string Message { get; set; } = string.Empty;
    }

    // ---- autorun.inf 批处理 payload ----

    private sealed class AutorunBatchInput
    {
        [JsonPropertyName("items")] public List<AutorunBatchItem> Items { get; set; } = new();
        [JsonPropertyName("output_file")] public string OutputFile { get; set; } = string.Empty;
    }

    private sealed class AutorunBatchItem
    {
        [JsonPropertyName("drive_path")] public string DrivePath { get; set; } = string.Empty;
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("label")] public string Label { get; set; } = string.Empty;
        [JsonPropertyName("staged_icon_path")] public string StagedIconPath { get; set; } = string.Empty;
        [JsonPropertyName("icon_target_name")] public string IconTargetName { get; set; } = string.Empty;
        [JsonPropertyName("restore")] public bool Restore { get; set; }
    }
}
