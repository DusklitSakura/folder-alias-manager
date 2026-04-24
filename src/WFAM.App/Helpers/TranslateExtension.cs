using System.Windows.Data;
using System.Windows.Markup;
using WFAM.App.Services;

namespace WFAM.App.Helpers;

/// <summary>
/// XAML 标记扩展：{loc:Tr Key=Settings.Title}。
/// 实际返回一个绑定到 LocalizationService.Instance["Key"] 的 Binding，
/// 因此切换语言时所有 UI 会自动刷新。
/// </summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TrExtension : MarkupExtension
{
    public TrExtension() { }
    public TrExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]")
        {
            Source = LocalizationService.Instance,
            Mode = BindingMode.OneWay,
            FallbackValue = Key,
        };
        return binding.ProvideValue(serviceProvider);
    }
}
