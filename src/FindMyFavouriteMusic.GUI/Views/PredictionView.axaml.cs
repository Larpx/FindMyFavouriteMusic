using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Audio;
using Larpx.PersonalTools.FindMyFavouriteMusic.GUI.ViewModels;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.GUI.Views;

/// <summary>
/// 预测视图，处理文件选择对话框与拖拽上传交互。
/// <para>支持单文件拖拽（自动填充路径）和多文件拖拽（触发批量预测）。</para>
/// </summary>
public partial class PredictionView : UserControl
{
    /// <summary>支持的音频文件扩展名集合</summary>
    private static readonly HashSet<string> SupportedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".mp3", ".wav", ".flac", ".ogg", ".oga", ".m4a" };

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
            vm.MultiFilePicker = OpenMultiFileDialogAsync;
            vm.FolderPicker = OpenFolderDialogAsync;
            vm.SaveFilePicker = SaveFileDialogAsync;
        }
    }

    /// <summary>打开单文件选择对话框</summary>
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
                    Patterns = ["*.mp3", "*.wav", "*.flac", "*.ogg", "*.oga", "*.m4a"]
                },
                new FilePickerFileType("所有文件")
                {
                    Patterns = ["*"]
                }
            ]
        });

        return files.FirstOrDefault()?.TryGetLocalPath();
    }

    /// <summary>打开多文件选择对话框，用于批量预测</summary>
    private async Task<IReadOnlyList<string>?> OpenMultiFileDialogAsync()
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return null;

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择音乐文件（批量预测）",
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType("音频文件")
                {
                    Patterns = ["*.mp3", "*.wav", "*.flac", "*.ogg", "*.oga", "*.m4a"]
                },
                new FilePickerFileType("所有文件")
                {
                    Patterns = ["*"]
                }
            ]
        });

        var paths = files
            .Select(f => f.TryGetLocalPath())
            .Where(p => p is not null && SupportedExtensions.Contains(Path.GetExtension(p)))
            .Select(p => p!)
            .ToList();

        return paths.Count > 0 ? paths : null;
    }

    /// <summary>打开文件夹选择对话框，用于批量预测</summary>
    private async Task<string?> OpenFolderDialogAsync()
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return null;

        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择音乐文件夹（批量预测）",
            AllowMultiple = false
        });

        return folders.FirstOrDefault()?.TryGetLocalPath();
    }

    /// <summary>打开文件保存对话框，用于导出历史记录到 CSV 文件</summary>
    private async Task<string?> SaveFileDialogAsync()
    {
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return null;

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "导出历史记录",
            DefaultExtension = ".csv",
            FileTypeChoices =
            [
                new FilePickerFileType("CSV 文件")
                {
                    Patterns = ["*.csv"]
                }
            ]
        });

        return file?.TryGetLocalPath();
    }

    /// <summary>拖拽进入：高亮显示放置区</summary>
    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (!e.DataTransfer.Contains(DataFormat.File)) return;
        e.DragEffects = DragDropEffects.Copy;
        if (DropZone is Border border)
        {
            border.BorderBrush = Avalonia.Media.Brushes.Cyan;
            border.BorderThickness = new Avalonia.Thickness(2);
        }
    }

    /// <summary>拖拽离开：恢复默认外观</summary>
    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        ResetDropZoneAppearance();
    }

    /// <summary>
    /// 放下文件：单文件填充路径，多文件触发批量预测。
    /// </summary>
    private void OnDrop(object? sender, DragEventArgs e)
    {
        ResetDropZoneAppearance();

        if (DataContext is not PredictionViewModel vm) return;
        if (!e.DataTransfer.Contains(DataFormat.File)) return;

        var files = e.DataTransfer.TryGetFiles();
        if (files is null) return;

        // 过滤出支持的音频文件
        var audioPaths = files
            .Select(f => f.TryGetLocalPath())
            .Where(p => p is not null && SupportedExtensions.Contains(Path.GetExtension(p)))
            .Select(p => p!)
            .ToList();

        if (audioPaths.Count == 0)
        {
            vm.StatusMessage = "未找到支持的音频文件";
            return;
        }

        if (audioPaths.Count == 1)
        {
            // 单文件：填充路径，用户点击预测按钮手动触发
            vm.SelectedFilePath = audioPaths[0];
        }
        else
        {
            // 多文件：直接填充第一个路径，并自动触发批量预测
            vm.SelectedFilePath = audioPaths[0];
            // 使用 Dispatcher 延迟触发，确保 UI 先更新路径
            Avalonia.Threading.Dispatcher.UIThread.Post(async () =>
            {
                await vm.BatchPredictWithFilesCommand.ExecuteAsync(audioPaths);
            });
        }

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
