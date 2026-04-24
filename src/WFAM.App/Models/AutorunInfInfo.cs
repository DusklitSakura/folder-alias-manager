namespace WFAM.App.Models;

/// <summary>
/// 解析自 &lt;drive&gt;\autorun.inf 的 [autorun] 段。
/// IconPath 保留原始字面量（可能是相对盘符根的相对路径，如 "autorun.ico"）。
/// </summary>
public sealed record AutorunInfInfo(string? Label, string? IconPath, int IconIndex);
