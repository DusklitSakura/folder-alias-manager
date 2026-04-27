using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WFAM.App.Helpers;
using WFAM.App.Models;

namespace WFAM.App.Services;

/// <inheritdoc />
public sealed class ElevationService : IElevationService
{
    private static readonly Lazy<string?> HelperPath = new(LocateHelper);

    public bool IsHelperAvailable => HelperPath.Value is not null;

    public async Task<IReadOnlyList<WriteResult>> ElevatedBatchWriteAsync(
        IReadOnlyList<ElevatedWriteRequest> items,
        CancellationToken ct = default)
    {
        var helper = HelperPath.Value
            ?? throw new InvalidOperationException("DesktopIniHelper.exe 未找到。");

        var pid = Environment.ProcessId;
        var inputFile = Path.Combine(Path.GetTempPath(), $"wfam_in_{pid}_{Guid.NewGuid():N}.json");
        var outputFile = Path.Combine(Path.GetTempPath(), $"wfam_out_{pid}_{Guid.NewGuid():N}.json");

        var payload = new BatchInput
        {
            OutputFile = outputFile,
            Items = items.Select(i => new BatchItem
            {
                FolderPath = i.FolderPath,
                Name = i.Name,
                Alias = i.Alias,
                IconPath = i.IconPath ?? string.Empty,
                IconIndex = i.IconIndex,
                BackgroundImage = i.BackgroundImage ?? string.Empty,
                Restore = i.Restore,
            }).ToList(),
        };

        try
        {
            await File.WriteAllTextAsync(inputFile,
                JsonSerializer.Serialize(payload, JsonOpts), Encoding.UTF8, ct).ConfigureAwait(false);

            var status = await Task.Run(() => RunElevated(helper, $"--batch \"{inputFile}\"", 120_000), ct)
                                   .ConfigureAwait(false);

            if (status == ElevationStatus.Cancelled)
                return items.Select(i => new WriteResult(i.FolderPath, i.Name, WriteOutcome.AccessDenied, "用户取消了 UAC")).ToList();
            if (status != ElevationStatus.Ok)
                return items.Select(i => new WriteResult(i.FolderPath, i.Name, WriteOutcome.Failed, status.ToString())).ToList();

            if (!File.Exists(outputFile))
                return items.Select(i => new WriteResult(i.FolderPath, i.Name, WriteOutcome.Failed, "Helper 未输出结果文件")).ToList();

            var text = await File.ReadAllTextAsync(outputFile, Encoding.UTF8, ct).ConfigureAwait(false);
            var output = JsonSerializer.Deserialize<BatchOutput>(text, JsonOpts) ?? new BatchOutput();

            return output.Results.Select(r => new WriteResult(
                r.FolderPath,
                r.Name,
                r.Success ? WriteOutcome.Success : WriteOutcome.Failed,
                string.IsNullOrEmpty(r.Message) ? null : r.Message)).ToList();
        }
        finally
        {
            TryDelete(inputFile);
            TryDelete(outputFile);
        }
    }

    // -- Helper 启动（ShellExecute + runas，触发 UAC） --

    private enum ElevationStatus { Ok, Cancelled, Timeout, LaunchFailed, WaitError }

    private static ElevationStatus RunElevated(string exe, string args, int timeoutMs)
    {
        var sei = new NativeMethods.SHELLEXECUTEINFO
        {
            cbSize = Marshal.SizeOf<NativeMethods.SHELLEXECUTEINFO>(),
            fMask = NativeMethods.SEE_MASK_NOCLOSEPROCESS,
            hwnd = IntPtr.Zero,
            lpVerb = "runas",
            lpFile = exe,
            lpParameters = args,
            lpDirectory = null,
            nShow = NativeMethods.SW_HIDE,
        };

        if (!NativeMethods.ShellExecuteEx(ref sei))
        {
            var err = Marshal.GetLastWin32Error();
            return err == NativeMethods.ERROR_CANCELLED ? ElevationStatus.Cancelled : ElevationStatus.LaunchFailed;
        }

        try
        {
            var wait = NativeMethods.WaitForSingleObject(sei.hProcess, (uint)timeoutMs);
            return wait switch
            {
                NativeMethods.WAIT_OBJECT_0 => ElevationStatus.Ok,
                NativeMethods.WAIT_TIMEOUT => ElevationStatus.Timeout,
                _ => ElevationStatus.WaitError,
            };
        }
        finally
        {
            if (sei.hProcess != IntPtr.Zero) NativeMethods.CloseHandle(sei.hProcess);
        }
    }

    private static string? LocateHelper()
    {
        var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        if (string.IsNullOrEmpty(dir)) return null;
        var path = Path.Combine(dir, "DesktopIniHelper.exe");
        return File.Exists(path) ? path : null;
    }

    public async Task<IReadOnlyList<WriteResult>> ElevatedBatchAutorunAsync(
        IReadOnlyList<ElevatedAutorunRequest> items,
        CancellationToken ct = default)
    {
        var helper = HelperPath.Value
            ?? throw new InvalidOperationException("DesktopIniHelper.exe 未找到。");

        var pid = Environment.ProcessId;
        var inputFile = Path.Combine(Path.GetTempPath(), $"wfam_in_{pid}_{Guid.NewGuid():N}.json");
        var outputFile = Path.Combine(Path.GetTempPath(), $"wfam_out_{pid}_{Guid.NewGuid():N}.json");

        var payload = new AutorunBatchInput
        {
            OutputFile = outputFile,
            Items = items.Select(i => new AutorunBatchItem
            {
                DrivePath = i.DrivePath,
                Name = i.Name,
                Label = i.Label,
                StagedIconPath = i.StagedIconPath ?? string.Empty,
                IconTargetName = i.IconTargetName,
                BackgroundImage = i.BackgroundImage ?? string.Empty,
                Restore = i.Restore,
            }).ToList(),
        };

        try
        {
            await File.WriteAllTextAsync(inputFile,
                JsonSerializer.Serialize(payload, JsonOpts), Encoding.UTF8, ct).ConfigureAwait(false);

            var status = await Task.Run(() => RunElevated(helper, $"--batch-autorun \"{inputFile}\"", 120_000), ct)
                                   .ConfigureAwait(false);

            if (status == ElevationStatus.Cancelled)
                return items.Select(i => new WriteResult(i.DrivePath, i.Name, WriteOutcome.AccessDenied, "用户取消了 UAC")).ToList();
            if (status != ElevationStatus.Ok)
                return items.Select(i => new WriteResult(i.DrivePath, i.Name, WriteOutcome.Failed, status.ToString())).ToList();

            if (!File.Exists(outputFile))
                return items.Select(i => new WriteResult(i.DrivePath, i.Name, WriteOutcome.Failed, "Helper 未输出结果文件")).ToList();

            var text = await File.ReadAllTextAsync(outputFile, Encoding.UTF8, ct).ConfigureAwait(false);
            var output = JsonSerializer.Deserialize<BatchOutput>(text, JsonOpts) ?? new BatchOutput();

            return output.Results.Select(r => new WriteResult(
                r.FolderPath,
                r.Name,
                r.Success ? WriteOutcome.Success : WriteOutcome.Failed,
                string.IsNullOrEmpty(r.Message) ? null : r.Message)).ToList();
        }
        finally
        {
            TryDelete(inputFile);
            TryDelete(outputFile);
            // 清理 staged 图标
            foreach (var i in items)
            {
                if (!string.IsNullOrEmpty(i.StagedIconPath)) TryDelete(i.StagedIconPath);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* ignore */ }
    }

    public async Task<IReadOnlyList<WriteResult>> ElevatedBatchDisguiseAsync(
        IReadOnlyList<ElevatedDisguiseRequest> items,
        CancellationToken ct = default)
    {
        var helper = HelperPath.Value
            ?? throw new InvalidOperationException("DesktopIniHelper.exe 未找到。");

        var pid = Environment.ProcessId;
        var inputFile = Path.Combine(Path.GetTempPath(), $"wfam_in_{pid}_{Guid.NewGuid():N}.json");
        var outputFile = Path.Combine(Path.GetTempPath(), $"wfam_out_{pid}_{Guid.NewGuid():N}.json");

        var payload = new DisguiseBatchInput
        {
            OutputFile = outputFile,
            Items = items.Select(i => new DisguiseBatchItem
            {
                FolderPath = i.FolderPath,
                Name = i.Name,
                Clsid = i.Clsid ?? string.Empty,
                Restore = i.Restore,
            }).ToList(),
        };

        try
        {
            await File.WriteAllTextAsync(inputFile,
                JsonSerializer.Serialize(payload, JsonOpts), Encoding.UTF8, ct).ConfigureAwait(false);

            var status = await Task.Run(() => RunElevated(helper, $"--batch-disguise \"{inputFile}\"", 120_000), ct)
                                   .ConfigureAwait(false);

            if (status == ElevationStatus.Cancelled)
                return items.Select(i => new WriteResult(i.FolderPath, i.Name, WriteOutcome.AccessDenied, "用户取消了 UAC")).ToList();
            if (status != ElevationStatus.Ok)
                return items.Select(i => new WriteResult(i.FolderPath, i.Name, WriteOutcome.Failed, status.ToString())).ToList();

            if (!File.Exists(outputFile))
                return items.Select(i => new WriteResult(i.FolderPath, i.Name, WriteOutcome.Failed, "Helper 未输出结果文件")).ToList();

            var text = await File.ReadAllTextAsync(outputFile, Encoding.UTF8, ct).ConfigureAwait(false);
            var output = JsonSerializer.Deserialize<BatchOutput>(text, JsonOpts) ?? new BatchOutput();

            return output.Results.Select(r => new WriteResult(
                r.FolderPath,
                r.Name,
                r.Success ? WriteOutcome.Success : WriteOutcome.Failed,
                string.IsNullOrEmpty(r.Message) ? null : r.Message)).ToList();
        }
        finally
        {
            TryDelete(inputFile);
            TryDelete(outputFile);
        }
    }

    private sealed class DisguiseBatchInput
    {
        public List<DisguiseBatchItem> Items { get; set; } = new();
        public string OutputFile { get; set; } = string.Empty;
    }

    private sealed class DisguiseBatchItem
    {
        public string FolderPath { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Clsid { get; set; } = string.Empty;
        public bool Restore { get; set; }
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private sealed class BatchInput
    {
        [JsonPropertyName("items")] public List<BatchItem> Items { get; set; } = new();
        [JsonPropertyName("output_file")] public string OutputFile { get; set; } = string.Empty;
    }
    private sealed class BatchItem
    {
        [JsonPropertyName("folder_path")] public string FolderPath { get; set; } = string.Empty;
        [JsonPropertyName("alias")] public string Alias { get; set; } = string.Empty;
        [JsonPropertyName("icon_path")] public string IconPath { get; set; } = string.Empty;
        [JsonPropertyName("icon_index")] public int IconIndex { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("background_image")] public string BackgroundImage { get; set; } = string.Empty;
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

    // ---- autorun.inf 提权批处理 payload ----

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
        [JsonPropertyName("background_image")] public string BackgroundImage { get; set; } = string.Empty;
        [JsonPropertyName("restore")] public bool Restore { get; set; }
    }
}
