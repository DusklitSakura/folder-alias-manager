using WFAM.App.Models;

namespace WFAM.App.Services;

/// <summary>
/// 应用设置加载/保存。基于 JSON 文件，安全地处理首次运行与读取异常。
/// </summary>
public interface ISettingsService
{
    AppSettings Current { get; }

    /// <summary>从磁盘加载（失败则返回默认值并保留默认设置在内存中）。</summary>
    void Load();

    /// <summary>把 <see cref="Current"/> 写回磁盘。</summary>
    void Save();
}
