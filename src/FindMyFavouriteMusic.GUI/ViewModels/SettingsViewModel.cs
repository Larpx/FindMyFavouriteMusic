using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FindMyFavouriteMusic.Core.Configuration;
using FindMyFavouriteMusic.Core.Interfaces;
using FindMyFavouriteMusic.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FindMyFavouriteMusic.GUI.ViewModels;

/// <summary>
/// 设置 ViewModel
/// </summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly IDeepFeatureExtractor _deepExtractor;
    private readonly IOptionsMonitor<OnnxModelOptions> _onnxOptions;
    private readonly IOptionsMonitor<PredictionOptions> _predictionOptions;
    private readonly IProfileService _profileService;
    private readonly ILogger<SettingsViewModel> _logger;

    public SettingsViewModel(
        IDeepFeatureExtractor deepExtractor,
        IOptionsMonitor<OnnxModelOptions> onnxOptions,
        IOptionsMonitor<PredictionOptions> predictionOptions,
        IProfileService profileService,
        ILogger<SettingsViewModel> logger)
    {
        _deepExtractor = deepExtractor;
        _onnxOptions = onnxOptions;
        _predictionOptions = predictionOptions;
        _profileService = profileService;
        _logger = logger;

        var currentOptions = _predictionOptions.CurrentValue;
        _acousticWeight = currentOptions.AcousticWeight;
        _deepWeight = currentOptions.DeepWeight;
        _onnxModelPath = _onnxOptions.CurrentValue.VggishModelPath ?? string.Empty;
        _enableDeepFeatures = _onnxOptions.CurrentValue.EnableDeepFeatures;
        _isModelLoaded = deepExtractor.IsModelLoaded;
    }

    [ObservableProperty]
    private double _acousticWeight;

    [ObservableProperty]
    private double _deepWeight;

    [ObservableProperty]
    private string _onnxModelPath;

    [ObservableProperty]
    private bool _enableDeepFeatures;

    [ObservableProperty]
    private bool _isModelLoaded;

    [ObservableProperty]
    private string _statusMessage = "就绪";

    [RelayCommand]
    private void LoadModel()
    {
        if (string.IsNullOrWhiteSpace(OnnxModelPath))
        {
            StatusMessage = "请先指定模型文件路径";
            return;
        }

        var result = _deepExtractor.LoadModel(OnnxModelPath);
        IsModelLoaded = _deepExtractor.IsModelLoaded;
        StatusMessage = result.IsSuccess
            ? "模型加载成功"
            : $"模型加载失败: {result.Error}";
    }

    [RelayCommand]
    private async Task RebuildProfileAsync()
    {
        StatusMessage = "正在重建画像...";
        var result = await _profileService.RebuildProfileAsync();
        StatusMessage = result.IsSuccess
            ? "画像重建完成"
            : $"画像重建失败: {result.Error}";
    }
}
