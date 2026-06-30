using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FindMyFavouriteMusic.Models.Dtos;
using FindMyFavouriteMusic.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace FindMyFavouriteMusic.GUI.ViewModels;

/// <summary>
/// 预测 ViewModel
/// </summary>
public partial class PredictionViewModel : ViewModelBase
{
    private readonly IPredictionService _predictionService;
    private readonly IProfileService _profileService;
    private readonly ILogger<PredictionViewModel> _logger;

    public PredictionViewModel(
        IPredictionService predictionService,
        IProfileService profileService,
        ILogger<PredictionViewModel> logger)
    {
        _predictionService = predictionService;
        _profileService = profileService;
        _logger = logger;
    }

    [ObservableProperty]
    private string _selectedFilePath = string.Empty;

    [ObservableProperty]
    private double _predictionScore;

    [ObservableProperty]
    private double _acousticScore;

    [ObservableProperty]
    private double? _deepScore;

    [ObservableProperty]
    private string _currentMode = "声学模式";

    [ObservableProperty]
    private bool _isPredicting;

    [ObservableProperty]
    private string _statusMessage = "请选择音乐文件进行预测";

    [ObservableProperty]
    private bool _hasProfile;

    [RelayCommand]
    private async Task PredictAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedFilePath))
        {
            StatusMessage = "请先选择音乐文件";
            return;
        }

        // 检查画像
        HasProfile = await _profileService.HasProfileAsync();
        if (!HasProfile)
        {
            StatusMessage = "请先在音乐库中标记喜欢的歌曲以构建画像";
            return;
        }

        IsPredicting = true;
        StatusMessage = "正在预测...";

        try
        {
            var result = await _predictionService.PredictAsync(SelectedFilePath);

            if (result.IsSuccess)
            {
                var prediction = result.Value;
                PredictionScore = Math.Round(prediction.Score, 1);
                AcousticScore = Math.Round(prediction.AcousticScore, 1);
                DeepScore = prediction.DeepScore.HasValue ? Math.Round(prediction.DeepScore.Value, 1) : null;
                CurrentMode = prediction.Mode == PredictionMode.AcousticAndDeep ? "深度增强模式" : "声学模式";
                StatusMessage = $"预测完成: {prediction.SongTitle} - 匹配度 {PredictionScore}%";
            }
            else
            {
                StatusMessage = $"预测失败: {result.Error}";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "预测失败");
            StatusMessage = $"预测出错: {ex.Message}";
        }
        finally
        {
            IsPredicting = false;
        }
    }

    [RelayCommand]
    private void SelectFile()
    {
        // 文件选择由 View 层处理
        StatusMessage = "请选择音乐文件...";
    }
}
