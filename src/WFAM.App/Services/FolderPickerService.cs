using Microsoft.Win32;

namespace WFAM.App.Services;

public interface IFolderPickerService
{
    /// <summary>选择多个文件夹。返回空集合表示用户取消。</summary>
    IReadOnlyList<string> PickFolders();

    /// <summary>选择单个 .exe / .dll / .ico 文件用于自定义图标。</summary>
    string? PickIconFile();

    /// <summary>选择单张图片（jpg/jpeg/png/bmp/gif）作为文件夹/驱动器自定义背景。</summary>
    string? PickImageFile();
}

public sealed class FolderPickerService : IFolderPickerService
{
    public IReadOnlyList<string> PickFolders()
    {
        var dlg = new OpenFolderDialog
        {
            Title = "选择文件夹（可多选）",
            Multiselect = true,
        };
        return dlg.ShowDialog() == true ? dlg.FolderNames : Array.Empty<string>();
    }

    public string? PickIconFile()
    {
        var dlg = new OpenFileDialog
        {
            Title = "选择图标来源文件",
            Filter = "图标来源 (*.exe;*.dll;*.ico)|*.exe;*.dll;*.ico|所有文件 (*.*)|*.*",
            CheckFileExists = true,
        };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    public string? PickImageFile()
    {
        var dlg = new OpenFileDialog
        {
            Title = "选择背景图片",
            Filter = "图片 (*.jpg;*.jpeg;*.png;*.bmp;*.gif)|*.jpg;*.jpeg;*.png;*.bmp;*.gif|所有文件 (*.*)|*.*",
            CheckFileExists = true,
        };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }
}
