using Avalonia.Controls;
using Avalonia.Platform.Storage;
using FindMyFavouriteMusic.GUI.ViewModels;

namespace FindMyFavouriteMusic.GUI.Views;

/// <summary>
/// 预测视图，处理文件选择对话框
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
    /// 打开文件选择对话框，筛选音频格式
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
}
