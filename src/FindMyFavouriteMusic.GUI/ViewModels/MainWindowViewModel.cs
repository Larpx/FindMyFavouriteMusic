using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FindMyFavouriteMusic.Models.Dtos;
using FindMyFavouriteMusic.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace FindMyFavouriteMusic.GUI.ViewModels;

/// <summary>
/// 主窗口 ViewModel，管理导航
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    private readonly ILogger<MainWindowViewModel> _logger;

    public MainWindowViewModel(
        MusicLibraryViewModel musicLibraryViewModel,
        PredictionViewModel predictionViewModel,
        SettingsViewModel settingsViewModel,
        ILogger<MainWindowViewModel> logger)
    {
        _logger = logger;
        MusicLibraryViewModel = musicLibraryViewModel;
        PredictionViewModel = predictionViewModel;
        SettingsViewModel = settingsViewModel;
        _currentPage = MusicLibraryViewModel;
    }

    public MusicLibraryViewModel MusicLibraryViewModel { get; }
    public PredictionViewModel PredictionViewModel { get; }
    public SettingsViewModel SettingsViewModel { get; }

    [ObservableProperty]
    private ViewModelBase _currentPage;

    [RelayCommand]
    private void NavigateTo(string page)
    {
        CurrentPage = page switch
        {
            "Library" => MusicLibraryViewModel,
            "Prediction" => PredictionViewModel,
            "Settings" => SettingsViewModel,
            _ => MusicLibraryViewModel
        };
    }
}
