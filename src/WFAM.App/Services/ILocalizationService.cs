using System.ComponentModel;

namespace WFAM.App.Services;

/// <summary>
/// 运行时本地化服务。通过单例 + 索引器 + INotifyPropertyChanged 实现绑定式动态切换。
/// 在 XAML 中通过 {loc:Tr Key} 标记扩展使用。
/// </summary>
public interface ILocalizationService : INotifyPropertyChanged
{
    /// <summary>当前语言代码（zh-CN / en-US）。</summary>
    string CurrentLanguage { get; }

    /// <summary>支持的语言列表。</summary>
    IReadOnlyList<LanguageOption> AvailableLanguages { get; }

    /// <summary>切换当前语言（同时通知 UI 重新解析所有绑定）。</summary>
    void SetLanguage(string code);

    /// <summary>翻译指定 Key；找不到时回退到 zh-CN，再回退为 Key 本身。</summary>
    string this[string key] { get; }
}

public sealed record LanguageOption(string Code, string DisplayName);
