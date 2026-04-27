using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using WFAM.App.Models;

namespace WFAM.App.Services;

/// <inheritdoc />
public sealed class FolderDisguiseService : IFolderDisguiseService
{
    // 大括号包裹的标准 GUID
    private static readonly Regex ClsidRegex = new(
        @"^\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}$",
        RegexOptions.Compiled);

    public IReadOnlyList<DisguisePreset> Presets { get; } = new[]
    {
        new DisguisePreset("Disguise.Preset.RecycleBin",   "{645FF040-5081-101B-9F08-00AA002F954E}", "Delete24"),
        new DisguisePreset("Disguise.Preset.MyComputer",   "{20D04FE0-3AEA-1069-A2D8-08002B30309D}", "Desktop24"),
        new DisguisePreset("Disguise.Preset.ControlPanel", "{26EE0668-A00A-44D7-9371-BEB064C98683}", "Settings24"),
        new DisguisePreset("Disguise.Preset.Network",      "{208D2C60-3AEA-1069-A2D7-08002B30309D}", "GlobeShield24"),
        new DisguisePreset("Disguise.Preset.Printers",     "{2227A280-3AEA-1069-A2DE-08002B30309D}", "Print24"),
        new DisguisePreset("Disguise.Preset.Tasks",        "{D6277990-4C6A-11CF-8D87-00AA0060F5BF}", "ClipboardClock24"),
        new DisguisePreset("Disguise.Preset.Fonts",        "{BD84B380-8CA2-1069-AB1D-08002B30309D}", "TextFont24"),
        new DisguisePreset("Disguise.Preset.AllTasks",     "{ED7BA470-8E54-465E-825C-99712043E01C}", "Wrench24"),
        new DisguisePreset("Disguise.Preset.Connections",  "{7007ACC7-3202-11D1-AAD2-00805FC1270E}", "PlugConnected24"),
    };

    public DisguiseState Detect(string folderPath)
    {
        try
        {
            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
                return new DisguiseState(false, null, null);

            var iniPath = Path.Combine(folderPath, "desktop.ini");
            if (!File.Exists(iniPath))
                return new DisguiseState(false, null, null);

            string[]? lines = null;
            foreach (var enc in CandidateEncodings())
            {
                try { lines = File.ReadAllLines(iniPath, enc); break; }
                catch (DecoderFallbackException) { /* try next */ }
                catch (IOException) { return new DisguiseState(false, null, null); }
            }
            if (lines is null) return new DisguiseState(false, null, null);

            var section = string.Empty;
            foreach (var raw in lines)
            {
                var l = raw.Trim();
                if (l.StartsWith('[') && l.EndsWith(']')) { section = l; continue; }
                if (!section.Equals("[.ShellClassInfo]", StringComparison.OrdinalIgnoreCase)) continue;
                if (!l.StartsWith("CLSID=", StringComparison.OrdinalIgnoreCase)) continue;

                var clsid = l["CLSID=".Length..].Trim();
                var preset = Presets.FirstOrDefault(p =>
                    string.Equals(p.Clsid, clsid, StringComparison.OrdinalIgnoreCase));
                return new DisguiseState(true, clsid, preset?.NameKey);
            }
            return new DisguiseState(false, null, null);
        }
        catch
        {
            return new DisguiseState(false, null, null);
        }
    }

    public Task<WriteResult> DisguiseAsync(string folderPath, string clsid, CancellationToken ct = default)
        => Task.Run(() => DisguiseCore(folderPath, clsid), ct);

    public Task<WriteResult> RestoreAsync(string folderPath, CancellationToken ct = default)
        => Task.Run(() => RestoreCore(folderPath), ct);

    private static WriteResult DisguiseCore(string folderPath, string clsid)
    {
        var name = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (!Directory.Exists(folderPath))
            return new WriteResult(folderPath, name, WriteOutcome.Failed, "目录不存在");
        if (!ClsidRegex.IsMatch(clsid))
            return new WriteResult(folderPath, name, WriteOutcome.Failed, "CLSID 格式无效");

        var iniPath = Path.Combine(folderPath, "desktop.ini");

        // 解除原属性以便覆盖写入
        RunAttrib($"-r -s \"{folderPath}\"");
        if (File.Exists(iniPath)) RunAttrib($"-r -h -s \"{iniPath}\"");

        // 直接覆盖：伪装本身要求 desktop.ini 仅描述命名空间对象。
        var sb = new StringBuilder();
        sb.AppendLine("[.ShellClassInfo]");
        sb.AppendLine("CLSID=" + clsid);

        try
        {
            File.WriteAllText(iniPath, sb.ToString(), GetSystemEncoding());
        }
        catch (UnauthorizedAccessException ex)
        {
            return new WriteResult(folderPath, name, WriteOutcome.AccessDenied, ex.Message);
        }
        catch (IOException ex)
        {
            return new WriteResult(folderPath, name, WriteOutcome.Failed, ex.Message);
        }

        // desktop.ini 必须为 +h +s 才会被 Explorer 解释；
        // 文件夹必须 +s（系统）+r（只读）才会让 Explorer 把目录视为命名空间对象。
        RunAttrib($"+h +s \"{iniPath}\"");
        RunAttrib($"+s +r \"{folderPath}\"");
        return new WriteResult(folderPath, name, WriteOutcome.Success);
    }

    private static WriteResult RestoreCore(string folderPath)
    {
        var name = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (!Directory.Exists(folderPath))
            return new WriteResult(folderPath, name, WriteOutcome.Failed, "目录不存在");

        var iniPath = Path.Combine(folderPath, "desktop.ini");
        RunAttrib($"-r -s \"{folderPath}\"");
        if (File.Exists(iniPath))
        {
            RunAttrib($"-r -h -s \"{iniPath}\"");
            try { File.Delete(iniPath); }
            catch (UnauthorizedAccessException ex) { return new WriteResult(folderPath, name, WriteOutcome.AccessDenied, ex.Message); }
            catch (IOException ex) { return new WriteResult(folderPath, name, WriteOutcome.Failed, ex.Message); }
        }
        return new WriteResult(folderPath, name, WriteOutcome.Success);
    }

    private static IEnumerable<Encoding> CandidateEncodings()
    {
        yield return GetSystemEncoding();
        yield return Encoding.UTF8;
        yield return Encoding.Unicode;
    }

    private static Encoding GetSystemEncoding()
    {
        try { return Encoding.GetEncoding(0); }
        catch { return Encoding.UTF8; }
    }

    private static void RunAttrib(string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "attrib",
                Arguments = args,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WindowStyle = ProcessWindowStyle.Hidden,
            };
            using var p = Process.Start(psi);
            p?.WaitForExit(5000);
        }
        catch { /* best effort */ }
    }
}
