using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using WFAM.App.ViewModels;

namespace WFAM.App.Helpers;

/// <summary>
/// 附加属性：在元素上启用文件夹拖放，自动转发到 <see cref="FoldersViewModel"/>。
/// 这样保持 View 内零业务逻辑。
/// </summary>
public static class DragDropHelper
{
    public static readonly DependencyProperty DropFoldersProperty =
        DependencyProperty.RegisterAttached(
            "DropFolders",
            typeof(bool),
            typeof(DragDropHelper),
            new PropertyMetadata(false, OnDropFoldersChanged));

    public static void SetDropFolders(DependencyObject element, bool value) =>
        element.SetValue(DropFoldersProperty, value);

    public static bool GetDropFolders(DependencyObject element) =>
        (bool)element.GetValue(DropFoldersProperty);

    private static void OnDropFoldersChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement el) return;
        if ((bool)e.NewValue)
        {
            el.AllowDrop = true;
            el.PreviewDragOver += OnPreviewDragOver;
            el.Drop += OnDrop;
        }
        else
        {
            el.PreviewDragOver -= OnPreviewDragOver;
            el.Drop -= OnDrop;
        }
    }

    private static void OnPreviewDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private static async void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;

        var folders = paths.Where(Directory.Exists).ToList();
        if (folders.Count == 0) return;

        var vm = App.Services.GetRequiredService<FoldersViewModel>();
        await vm.AddFoldersAsync(folders);
    }
}
