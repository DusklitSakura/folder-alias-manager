namespace WFAM.App.Models;

/// <summary>用户对某个文件夹执行的一次操作记录。</summary>
public sealed class HistoryEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime Timestamp { get; set; } = DateTime.Now;

    /// <summary>Modify / Restore</summary>
    public HistoryAction Action { get; set; }

    public string FolderPath { get; set; } = string.Empty;
    public string FolderName { get; set; } = string.Empty;

    // 操作前
    public string BeforeAlias { get; set; } = string.Empty;
    public string BeforeIconPath { get; set; } = string.Empty;
    public int BeforeIconIndex { get; set; }

    // 操作后
    public string AfterAlias { get; set; } = string.Empty;
    public string AfterIconPath { get; set; } = string.Empty;
    public int AfterIconIndex { get; set; }
}

public enum HistoryAction
{
    Modify = 0,
    Restore = 1,
}
