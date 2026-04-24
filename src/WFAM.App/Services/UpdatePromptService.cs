using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.Logging;
using Wpf.Ui;
using Wpf.Ui.Controls;
using WFAM.App.Models;

// 同时引用 Wpf.Ui.Controls 与 System.Windows.Controls 时，TextBlock / ProgressBar / ScrollViewer
// 等控件在两个命名空间下都存在；显式使用 System.Windows.Controls 版本以避免歧义。
using TextBlock = System.Windows.Controls.TextBlock;
using ProgressBar = System.Windows.Controls.ProgressBar;
using ScrollViewer = System.Windows.Controls.ScrollViewer;

namespace WFAM.App.Services;

/// <summary>
/// 弹出"发现更新"对话框（含 release notes + 立即更新 / 稍后 / 忽略 三个按钮），
/// 并在用户选择"立即更新"时下载并触发自重启更新流程。
/// </summary>
public interface IUpdatePromptService
{
    Task PromptAsync(UpdateInfo info);
}

public sealed class UpdatePromptService : IUpdatePromptService
{
    private readonly IContentDialogService _dialogs;
    private readonly IUpdateService _updates;
    private readonly ISettingsService _settings;
    private readonly INotificationService _notify;
    private readonly ILocalizationService _loc;
    private readonly ILogger<UpdatePromptService> _logger;

    private bool _busy;

    public UpdatePromptService(
        IContentDialogService dialogs,
        IUpdateService updates,
        ISettingsService settings,
        INotificationService notify,
        ILocalizationService loc,
        ILogger<UpdatePromptService> logger)
    {
        _dialogs = dialogs;
        _updates = updates;
        _settings = settings;
        _notify = notify;
        _loc = loc;
        _logger = logger;
    }

    public async Task PromptAsync(UpdateInfo info)
    {
        if (_busy) return; // 防止重复弹
        _busy = true;
        try
        {
            var host = _dialogs.GetDialogHostEx();
            if (host is null)
            {
                _logger.LogWarning("ContentDialogHost 尚未注册，跳过更新提示。");
                return;
            }

            var dlg = new ContentDialog
            {
                Title = string.Format(_loc["Update.Dialog.Title"], info.Version.ToString(3)),
                Content = BuildContent(info),
                PrimaryButtonText = _loc["Update.Dialog.UpdateNow"],
                SecondaryButtonText = _loc["Update.Dialog.Later"],
                CloseButtonText = _loc["Update.Dialog.Skip"],
                DefaultButton = ContentDialogButton.Primary,
                DialogHostEx = host,
            };

            var result = await dlg.ShowAsync(CancellationToken.None);
            switch (result)
            {
                case ContentDialogResult.Primary:
                    await DownloadAndApplyAsync(info);
                    break;
                case ContentDialogResult.None:
                    // CloseButton -> 忽略此版本
                    _settings.Current.LastSkippedUpdateVersion = info.Version.ToString();
                    _settings.Save();
                    break;
                case ContentDialogResult.Secondary:
                default:
                    // 稍后：什么都不做，下次启动还会提示
                    break;
            }
        }
        finally
        {
            _busy = false;
        }
    }

    private object BuildContent(UpdateInfo info)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };

        panel.Children.Add(new TextBlock
        {
            Text = string.Format(_loc["Update.Dialog.Subtitle"], info.Version.ToString(3), _updates.CurrentVersion.ToString(3)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
            Opacity = 0.85,
        });

        panel.Children.Add(new TextBlock
        {
            Text = _loc["Update.Dialog.ReleaseNotes"],
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4),
        });

        var notes = string.IsNullOrWhiteSpace(info.Body) ? _loc["Update.Dialog.NoNotes"] : info.Body!.Trim();
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            MaxHeight = 280,
            MinWidth = 460,
        };
        scroll.Content = new TextBlock
        {
            Text = notes,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new System.Windows.Media.FontFamily("Consolas, Cascadia Mono, Segoe UI"),
            FontSize = 12.5,
        };
        panel.Children.Add(scroll);

        return panel;
    }

    private async Task DownloadAndApplyAsync(UpdateInfo info)
    {
        if (string.IsNullOrEmpty(info.PrimaryAssetUrl))
        {
            _notify.Warning(_loc["Update.Failed"], _loc["Update.NoAsset"]);
            return;
        }

        // 进度条对话框
        var progressBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 1,
            Value = 0,
            Height = 6,
            MinWidth = 460,
            Margin = new Thickness(0, 8, 0, 4),
        };
        var status = new TextBlock
        {
            Text = string.Format(_loc["Update.Downloading"], 0),
            Margin = new Thickness(0, 0, 0, 4),
        };
        var panel = new StackPanel();
        panel.Children.Add(status);
        panel.Children.Add(progressBar);

        var dlg = new ContentDialog
        {
            Title = _loc["Update.Dialog.Downloading.Title"],
            Content = panel,
            CloseButtonText = _loc["Common.Cancel"],
            DialogHostEx = _dialogs.GetDialogHostEx(),
        };

        var cts = new CancellationTokenSource();
        dlg.ButtonClicked += (_, e) =>
        {
            if (e.Button == ContentDialogButton.Close) cts.Cancel();
        };

        var progress = new Progress<double>(p =>
        {
            progressBar.Value = Math.Clamp(p, 0, 1);
            status.Text = string.Format(_loc["Update.Downloading"], (int)(p * 100));
        });

        var showTask = dlg.ShowAsync(cts.Token);

        try
        {
            var staging = await _updates.DownloadAsync(info, progress, cts.Token);
            dlg.Hide();

            // 启动外部脚本并退出当前进程；由脚本完成覆盖+重启
            _updates.ApplyAndRestart(staging);

            await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                Application.Current.Shutdown();
            });
        }
        catch (OperationCanceledException)
        {
            try { dlg.Hide(); } catch { }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "下载或应用更新失败");
            try { dlg.Hide(); } catch { }
            _notify.Error(_loc["Update.Failed"], ex.Message);
        }
    }
}
