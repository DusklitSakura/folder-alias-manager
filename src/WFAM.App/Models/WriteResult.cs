namespace WFAM.App.Models;

/// <summary>
/// 写入操作的结果分类。
/// </summary>
public enum WriteOutcome
{
    Success,
    Failed,
    AccessDenied,
}

public sealed record WriteResult(string FolderPath, string Name, WriteOutcome Outcome, string? Message = null);
