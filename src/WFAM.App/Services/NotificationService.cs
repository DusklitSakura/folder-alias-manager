using Wpf.Ui;
using Wpf.Ui.Controls;

namespace WFAM.App.Services;

/// <summary>
/// 抽象出 UI 通知，便于 ViewModel 单元测试。
/// </summary>
public interface INotificationService
{
    void Info(string title, string message);
    void Success(string title, string message);
    void Warning(string title, string message);
    void Error(string title, string message);
}

public sealed class NotificationService : INotificationService
{
    private readonly ISnackbarService _snackbar;

    public NotificationService(ISnackbarService snackbar)
    {
        _snackbar = snackbar;
    }

    public void Info(string title, string message) =>
        Show(title, message, ControlAppearance.Info, SymbolRegular.Info24);

    public void Success(string title, string message) =>
        Show(title, message, ControlAppearance.Success, SymbolRegular.Checkmark24);

    public void Warning(string title, string message) =>
        Show(title, message, ControlAppearance.Caution, SymbolRegular.Warning24);

    public void Error(string title, string message) =>
        Show(title, message, ControlAppearance.Danger, SymbolRegular.ErrorCircle24);

    private void Show(string title, string message, ControlAppearance appearance, SymbolRegular icon)
    {
        _snackbar.Show(title, message, appearance, new SymbolIcon(icon), TimeSpan.FromSeconds(4));
    }
}
