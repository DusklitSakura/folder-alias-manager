using WFAM.App.Models;

namespace WFAM.App.Services;

public interface IHistoryService
{
    /// <summary>当前所有历史记录（按时间倒序）。</summary>
    IReadOnlyList<HistoryEntry> Entries { get; }

    /// <summary>新增一条记录并持久化。仅在 before/after 不同时写入。</summary>
    void Add(HistoryEntry entry);

    /// <summary>按 Id 删除一条。</summary>
    void Remove(string id);

    /// <summary>清空所有历史。</summary>
    void Clear();

    /// <summary>条目变化通知（增、删、清空）。</summary>
    event EventHandler? Changed;
}
