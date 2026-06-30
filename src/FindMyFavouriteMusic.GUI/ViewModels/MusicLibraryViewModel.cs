using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FindMyFavouriteMusic.Models.Dtos;
using FindMyFavouriteMusic.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace FindMyFavouriteMusic.GUI.ViewModels;

/// <summary>
/// 音乐库 ViewModel
/// </summary>
public partial class MusicLibraryViewModel : ViewModelBase
{
    private readonly IMusicLibraryService _libraryService;
    private readonly ILogger<MusicLibraryViewModel> _logger;

    /// <summary>
    /// 文件夹选择交互回调，由 View 层设置
    /// </summary>
    public Func<Task<string?>>? FolderPicker { get; set; }

    public MusicLibraryViewModel(
        IMusicLibraryService libraryService,
        ILogger<MusicLibraryViewModel> logger)
    {
        _libraryService = libraryService;
        _logger = logger;
    }

    [ObservableProperty]
    private ObservableCollection<SongDto> _songs = [];

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private int _scanProgress;

    [ObservableProperty]
    private string _statusMessage = "就绪";

    [RelayCommand]
    private async Task ScanDirectoryAsync()
    {
        if (IsScanning) return;

        if (FolderPicker is not null)
        {
            var path = await FolderPicker();
            if (string.IsNullOrWhiteSpace(path))
            {
                StatusMessage = "已取消选择";
                return;
            }

            await ScanDirectoryAsync(path);
        }
        else
        {
            StatusMessage = "请选择音乐目录...";
        }
    }

    /// <summary>
    /// 扫描指定目录
    /// </summary>
    public async Task ScanDirectoryAsync(string directoryPath)
    {
        if (IsScanning) return;

        IsScanning = true;
        ScanProgress = 0;
        StatusMessage = "正在扫描...";

        try
        {
            var progress = new Progress<int>(p => ScanProgress = p);
            var result = await _libraryService.ScanDirectoryAsync(directoryPath, progress);

            if (result.IsSuccess)
            {
                Songs = new ObservableCollection<SongDto>(result.Value ?? []);
                StatusMessage = $"扫描完成，共 {(result.Value ?? []).Count} 首歌曲";
            }
            else
            {
                StatusMessage = $"扫描失败: {result.Error}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "扫描目录失败");
            StatusMessage = $"扫描出错: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    private async Task ToggleLikeAsync(SongDto song)
    {
        var newLikeStatus = !song.IsLiked;
        var result = await _libraryService.ToggleLikeAsync(song.Id, newLikeStatus);

        if (result.IsSuccess)
        {
            song.IsLiked = newLikeStatus;
            // 通知 UI 更新
            var index = Songs.IndexOf(song);
            if (index >= 0)
            {
                Songs[index] = song;
            }
            StatusMessage = newLikeStatus ? $"已喜欢: {song.Title}" : $"已取消喜欢: {song.Title}";
        }
        else
        {
            StatusMessage = $"操作失败: {result.Error}";
        }
    }

    [RelayCommand]
    private async Task LoadAllSongsAsync()
    {
        var result = await _libraryService.GetAllSongsAsync();
        if (result.IsSuccess)
        {
            Songs = new ObservableCollection<SongDto>(result.Value ?? []);
            StatusMessage = $"已加载 {(result.Value ?? []).Count} 首歌曲";
        }
        else
        {
            StatusMessage = $"加载失败: {result.Error}";
        }
    }
}
