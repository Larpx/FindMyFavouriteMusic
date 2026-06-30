using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Larpx.PersonalTools.FindMyFavouriteMusic.GUI.ViewModels;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.GUI.Views;

/// <summary>
/// 预测视图，处理文件选择对话框与拖拽上传交互。
/// <para>Avalonia 12 拖拽 API：使用 <see cref="DragEventArgs.DataTransfer"/> 获取数据，</para>
/// <para>通过 <see cref="DataTransferExtensions.TryGetFiles"/> 解析文件。</para>
/// </summary>
public partial class PredictionView : UserControl
{
    public PredictionView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is PredictionViewModel vm)
        {
            vm.FilePicker = OpenFileDialogAsync;
        }
    }

    /// <summary>
    /// 打开文件选择对话框，筛选音频格式。
    /// </summary>
    private async Task<string?> OpenFileDialogAsync()
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return null;

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择音乐文件",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("音频文件")
                {
                    Patterns = ["*.mp3", "*.wav", "*.flac", "*.m4a", "*.ogg", "*.wma"]
                },
                new FilePickerFileType("所有文件")
                {
                    Patterns = ["*"]
                }
            ]
        });

        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    /// <summary>拖拽进入：高亮显示放置区</summary>
    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        // 仅当拖拽数据包含文件时才显示复制效果与高亮
        if (!e.DataTransfer.Contains(DataFormat.File)) return;
        e.DragEffects = DragDropEffects.Copy;
        // 视觉高亮：边框变粗
        if (DropZone is Border border)
        {
            border.BorderBrush = Avalonia.Media.Brushes.Indigo;
            border.BorderThickness = new Avalonia.Thickness(2);
        }
    }

    /// <summary>拖拽离开：恢复默认外观</summary>
    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        ResetDropZoneAppearance();
    }

    /// <summary>放下文件：解析路径并填充到 ViewModel</summary>
    private void OnDrop(object? sender, DragEventArgs e)
    {
        ResetDropZoneAppearance();

        if (DataContext is not PredictionViewModel vm) return;
        if (!e.DataTransfer.Contains(DataFormat.File)) return;

        // 使用 Avalonia 12 扩展方法获取文件列表
        var files = e.DataTransfer.TryGetFiles();
        var file = files?.FirstOrDefault();
        if (file is null) return;

        var path = file.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path)) return;

        vm.SelectedFilePath = path;
        e.DragEffects = DragDropEffects.Copy;
    }

    /// <summary>恢复放置区默认外观</summary>
    private void ResetDropZoneAppearance()
    {
        if (DropZone is Border border)
        {
            border.BorderBrush = null;
            border.BorderThickness = new Avalonia.Thickness(0);
        }
    }
}
