using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace WFAM.Helper;

/// <summary>
/// 对 Helper 输入做强校验，防止以管理员身份被任意调用方滥用。
/// 设计目标：
///   - 只接受由同目录下的 WFAM.exe 进程作为父进程触发；
///   - 输入/输出文件必须位于用户 %TEMP% 下且文件名遵循固定格式；
///   - 拒绝系统受保护目录（Windows、Program Files、根盘等）；
///   - 拒绝符号链接 / 路径遍历。
/// </summary>
internal static class SecurityGuard
{
    /// <summary>合法的输入文件名：wfam_in_&lt;pid&gt;_&lt;32hex&gt;.json</summary>
    private static readonly Regex InputNamePattern =
        new(@"^wfam_in_\d+_[0-9a-fA-F]{32}\.json$", RegexOptions.Compiled);

    /// <summary>合法的输出文件名：wfam_out_&lt;pid&gt;_&lt;32hex&gt;.json</summary>
    private static readonly Regex OutputNamePattern =
        new(@"^wfam_out_\d+_[0-9a-fA-F]{32}\.json$", RegexOptions.Compiled);

    /// <summary>合法的暂存图标文件名：wfam_ico_&lt;pid&gt;_&lt;32hex&gt;.ico</summary>
    private static readonly Regex StagedIconNamePattern =
        new(@"^wfam_ico_\d+_[0-9a-fA-F]{32}\.ico$", RegexOptions.Compiled);

    /// <summary>autorun.inf 引用的盘内图标文件名只允许简单形态。</summary>
    private static readonly Regex DriveIconTargetNamePattern =
        new(@"^[A-Za-z0-9_\-]{1,32}\.ico$", RegexOptions.Compiled);

    /// <summary>desktop.ini CLSID 必须形如 {GUID}。</summary>
    private static readonly Regex ClsidPattern =
        new(@"^\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}$",
            RegexOptions.Compiled);

    /// <summary>
    /// 校验调用方可信。
    /// UAC 提权后真正的父进程是 svchost.exe (AppInfo)，因此不能直接看父 PID；
    /// 改为要求当前 Windows 会话中存在同目录下、与本 Helper 同一 install 路径的
    /// 非提权 WFAM.exe 进程，作为「主程序在前台」的弱证据。
    /// </summary>
    public static void EnsureTrustedCaller()
    {
        var helperDir = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd('\\');
        int mySession = Process.GetCurrentProcess().SessionId;

        foreach (var p in Process.GetProcessesByName("WFAM"))
        {
            try
            {
                if (p.SessionId != mySession) continue;
                var img = p.MainModule?.FileName;
                if (string.IsNullOrEmpty(img)) continue;
                var dir = Path.GetDirectoryName(Path.GetFullPath(img))?.TrimEnd('\\') ?? string.Empty;
                if (string.Equals(dir, helperDir, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            catch { /* 不同权限下访问失败属于正常，继续遍历 */ }
            finally { p.Dispose(); }
        }

        throw new SecurityException(
            "非法调用：当前会话未发现同目录下的 WFAM.exe 主程序进程。");
    }

    /// <summary>校验输入文件路径合法。</summary>
    public static string ValidateInputFile(string path)
    {
        var full = Path.GetFullPath(path);
        var name = Path.GetFileName(full);
        if (!InputNamePattern.IsMatch(name))
            throw new SecurityException($"输入文件名不合法：{name}");

        EnsureUnderUserTemp(full);
        EnsureRegularFile(full);
        return full;
    }

    /// <summary>校验输出文件路径合法（必须与输入文件位于同一目录）。</summary>
    public static string ValidateOutputFile(string outputPath, string validatedInputPath)
    {
        if (string.IsNullOrEmpty(outputPath))
            return string.Empty;

        var full = Path.GetFullPath(outputPath);
        var name = Path.GetFileName(full);
        if (!OutputNamePattern.IsMatch(name))
            throw new SecurityException($"输出文件名不合法：{name}");

        var inputDir = Path.GetDirectoryName(validatedInputPath) ?? string.Empty;
        var outputDir = Path.GetDirectoryName(full) ?? string.Empty;
        if (!string.Equals(inputDir, outputDir, StringComparison.OrdinalIgnoreCase))
            throw new SecurityException("输出文件必须与输入文件位于同一目录。");

        // 输出文件若已存在必须是普通文件；防止符号链接到敏感路径
        if (File.Exists(full)) EnsureRegularFile(full);
        return full;
    }

    /// <summary>
    /// 校验目标文件夹安全：必须存在的目录、非系统受保护根目录、非 reparse point。
    /// </summary>
    public static bool IsFolderAllowed(string folderPath, out string reason)
    {
        reason = string.Empty;
        if (string.IsNullOrWhiteSpace(folderPath))
        { reason = "空路径"; return false; }

        string full;
        try { full = Path.GetFullPath(folderPath); }
        catch { reason = "路径不合法"; return false; }

        if (!Directory.Exists(full))
        { reason = "目录不存在"; return false; }

        // 拒绝驱动器根（如 C:\）
        var root = Path.GetPathRoot(full);
        if (!string.IsNullOrEmpty(root) &&
            string.Equals(full.TrimEnd('\\'), root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
        { reason = "驱动器根目录"; return false; }

        // 拒绝系统受保护目录及其子目录
        foreach (var protectedDir in GetProtectedDirectories())
        {
            if (IsSameOrSubDirectory(full, protectedDir))
            { reason = $"系统受保护目录（{protectedDir}）"; return false; }
        }

        // 拒绝 reparse point（避免符号链接绕过检查）
        try
        {
            var attrs = File.GetAttributes(full);
            if ((attrs & FileAttributes.ReparsePoint) != 0)
            { reason = "符号链接 / Reparse Point"; return false; }
        }
        catch { reason = "无法读取目录属性"; return false; }

        return true;
    }

    /// <summary>
    /// 校验目标驱动器根目录可用：必须是驱动器根（如 G:\）、可移动盘或本地盘、且非系统盘。
    /// 该方法用于 autorun.inf 写入路径，需要写到盘符根。
    /// </summary>
    public static bool IsDriveRootAllowed(string drivePath, out string reason)
    {
        reason = string.Empty;
        if (string.IsNullOrWhiteSpace(drivePath))
        { reason = "空路径"; return false; }

        string full;
        try { full = Path.GetFullPath(drivePath); }
        catch { reason = "路径不合法"; return false; }

        if (!Directory.Exists(full))
        { reason = "驱动器未就绪"; return false; }

        var root = Path.GetPathRoot(full) ?? string.Empty;
        if (string.IsNullOrEmpty(root)
            || !string.Equals(full.TrimEnd('\\'), root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
        { reason = "必须是驱动器根目录"; return false; }

        // 拒绝系统盘
        var sysDrive = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.System)) ?? string.Empty;
        if (string.Equals(root.TrimEnd('\\'), sysDrive.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
        { reason = "拒绝系统驱动器"; return false; }

        try
        {
            var di = new DriveInfo(root);
            if (!di.IsReady) { reason = "驱动器未就绪"; return false; }
            if (di.DriveType is not (DriveType.Removable or DriveType.Fixed))
            { reason = $"不支持的驱动器类型：{di.DriveType}"; return false; }
        }
        catch (Exception ex) { reason = ex.Message; return false; }

        return true;
    }

    /// <summary>校验 autorun.inf 引用的图标文件名（限定后缀 .ico、无路径分隔符）。</summary>
    public static bool IsValidIconTargetName(string name, out string reason)
    {
        reason = string.Empty;
        if (string.IsNullOrEmpty(name)) { reason = "空名称"; return false; }
        if (!DriveIconTargetNamePattern.IsMatch(name))
        { reason = $"不允许的图标文件名：{name}"; return false; }
        return true;
    }

    /// <summary>校验 desktop.ini 的 CLSID 字符串格式（必须是带大括号的标准 GUID）。</summary>
    public static bool IsValidClsid(string? value, out string reason)
    {
        reason = string.Empty;
        if (string.IsNullOrEmpty(value)) { reason = "空 CLSID"; return false; }
        if (!ClsidPattern.IsMatch(value))
        { reason = $"CLSID 格式不合法：{value}"; return false; }
        return true;
    }

    /// <summary>校验暂存的 .ico 文件路径必须位于 %TEMP%、文件名符合固定格式、为普通文件。</summary>
    public static string ValidateStagedIconFile(string path)
    {
        var full = Path.GetFullPath(path);
        var name = Path.GetFileName(full);
        if (!StagedIconNamePattern.IsMatch(name))
            throw new SecurityException($"暂存图标文件名不合法：{name}");
        EnsureUnderUserTemp(full);
        EnsureRegularFile(full);
        return full;
    }

    /// <summary>
    /// 校验自定义背景图片字面量。
    /// helper 不会打开图片，只把它作为字符串写进 desktop.ini，因此安全要求很轻：
    /// 仅过滤换行/INI 控制字符，限制长度，并禁止隐含控制字符；空字符串视为「清除背景」。
    /// </summary>
    public static bool IsValidBackgroundImagePath(string? value, out string reason)
    {
        reason = string.Empty;
        if (string.IsNullOrEmpty(value)) return true; // 空 → 清除背景
        if (value.Length > 512) { reason = "路径过长"; return false; }
        foreach (var ch in value)
        {
            if (ch is '\r' or '\n' or '\0' or '[' or ']')
            { reason = $"非法字符: {ch}"; return false; }
            if (char.IsControl(ch))
            { reason = "包含控制字符"; return false; }
        }
        return true;
    }

    // ---- helpers ----

    private static void EnsureUnderUserTemp(string fullPath)
    {
        var temp = Path.GetFullPath(Path.GetTempPath());
        if (!IsSameOrSubDirectory(fullPath, temp))
            throw new SecurityException($"路径必须位于 %TEMP% 下：{fullPath}");
    }

    private static void EnsureRegularFile(string fullPath)
    {
        if (!File.Exists(fullPath)) return;
        var attrs = File.GetAttributes(fullPath);
        if ((attrs & FileAttributes.ReparsePoint) != 0)
            throw new SecurityException($"文件不能是符号链接：{fullPath}");
        if ((attrs & FileAttributes.Directory) != 0)
            throw new SecurityException($"路径必须是文件：{fullPath}");
    }

    private static bool IsSameOrSubDirectory(string path, string parent)
    {
        var p = Path.GetFullPath(path).TrimEnd('\\') + "\\";
        var par = Path.GetFullPath(parent).TrimEnd('\\') + "\\";
        return p.StartsWith(par, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> GetProtectedDirectories()
    {
        // SafeExpand：当环境变量缺失时返回空字符串
        string E(string name) => Environment.ExpandEnvironmentVariables("%" + name + "%") is { } v
            && !v.Equals("%" + name + "%", StringComparison.Ordinal) ? v : string.Empty;

        var list = new[]
        {
            E("SystemRoot"),
            E("WINDIR"),
            E("ProgramFiles"),
            E("ProgramFiles(x86)"),
            E("ProgramW6432"),
            E("ProgramData"),
            E("CommonProgramFiles"),
            E("CommonProgramFiles(x86)"),
        };
        foreach (var p in list)
            if (!string.IsNullOrEmpty(p)) yield return p;

        // 显式追加常见 Recovery / Boot 目录（一般无对应环境变量）
        var sys = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var sysDrive = Path.GetPathRoot(sys);
        if (!string.IsNullOrEmpty(sysDrive))
        {
            yield return Path.Combine(sysDrive, "Recovery");
            yield return Path.Combine(sysDrive, "Boot");
            yield return Path.Combine(sysDrive, "$Recycle.Bin");
            yield return Path.Combine(sysDrive, "System Volume Information");
        }
    }
}

internal sealed class SecurityException : Exception
{
    public SecurityException(string message) : base(message) { }
}
