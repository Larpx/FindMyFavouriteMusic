using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Larpx.PersonalTools.FindMyFavouriteMusic.GUI.ViewModels;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.GUI.Views;

/// <summary>
/// 音乐库视图，处理文件对话框与文件夹拖拽交互。
/// <para>支持将文件夹拖拽到列表区域，自动触发目录扫描。</para>
/// </summary>
public partial class MusicLibraryView : UserControl
{
    public MusicLibraryView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is MusicLibraryViewModel vm)
        {
            vm.FolderPicker = OpenFolderDialogAsync;
            vm.ImagePicker = OpenImageDialogAsync;
        }
    }

    /// <summary>打开文件夹选择对话框</summary>
    private async Task<string?> OpenFolderDialogAsync()
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return null;

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择音乐文件夹",
            AllowMultiple = false
        });

        return folders.FirstOrDefault()?.TryGetLocalPath();
    }

    /// <summary>选择封面图片</summary>
    private async Task<(byte[]? Data, string? Mime)?> OpenImageDialogAsync()
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return null;

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择封面图片",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("图片")
                {
                    Patterns = ["*.jpg", "*.jpeg", "*.png"]
                }
            ]
        });

        var file = files.FirstOrDefault();
        if (file is null) return null;

        var path = file.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

        var bytes = await File.ReadAllBytesAsync(path);
        var mime = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            _ => "image/jpeg"
        };
        return (bytes, mime);
    }

    /// <summary>文件夹拖拽进入：高亮提示</summary>
    private void OnFolderDragEnter(object? sender, DragEventArgs e)
    {
        if (!e.DataTransfer.Contains(DataFormat.File)) return;
        e.DragEffects = DragDropEffects.Copy;
        SetDropHighlight(FolderDropZone, true);
    }

    /// <summary>文件夹拖拽离开：恢复外观</summary>
    private void OnFolderDragLeave(object? sender, DragEventArgs e)
    {
        SetDropHighlight(FolderDropZone, false);
    }

    /// <summary>文件夹放下：校验是否为目录后触发扫描</summary>
    private void OnFolderDrop(object? sender, DragEventArgs e)
    {
        SetDropHighlight(FolderDropZone, false);

        if (DataContext is not MusicLibraryViewModel vm) return;
        if (!e.DataTransfer.Contains(DataFormat.File)) return;

        var items = e.DataTransfer.TryGetFiles();
        var item = items?.FirstOrDefault();
        if (item is null) return;

        var path = item.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path)) return;

        // 仅接受文件夹
        if (Directory.Exists(path))
        {
            e.DragEffects = DragDropEffects.Copy;
            _ = vm.ScanDirectoryAsync(path);
        }
        else
        {
            vm.StatusMessage = "请拖入文件夹而非文件";
        }
    }

    /// <summary>设置拖拽高亮/恢复外观</summary>
    private static void SetDropHighlight(Border? control, bool highlight)
    {
        if (control is null) return;

        if (highlight)
        {
            control.BorderBrush = Brushes.Cyan;
            control.BorderThickness = new Thickness(2);
        }
        else
        {
            control.ClearValue(Border.BorderBrushProperty);
            control.ClearValue(Border.BorderThicknessProperty);
        }
    }
}
