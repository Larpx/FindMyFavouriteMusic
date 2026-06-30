using Avalonia.Controls;
using Avalonia.Platform.Storage;
using FindMyFavouriteMusic.GUI.ViewModels;

namespace FindMyFavouriteMusic.GUI.Views;

/// <summary>
/// 音乐库视图，处理文件对话框交互
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
        }
    }

    /// <summary>
    /// 打开文件夹选择对话框
    /// </summary>
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
}
