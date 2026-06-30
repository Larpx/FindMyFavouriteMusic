using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Configuration;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Interfaces;
using Larpx.PersonalTools.FindMyFavouriteMusic.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.GUI.ViewModels;

/// <summary>
/// 设置 ViewModel，提供预测权重调整、ONNX 模型加载、画像重建功能。
/// </summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly IDeepFeatureExtractor _deepExtractor;
    private readonly IOptionsMonitor<OnnxModelOptions> _onnxOptions;
    private readonly IOptionsMonitor<PredictionOptions> _predictionOptions;
    private readonly IProfileService _profileService;
    private readonly IUserSettingsService _userSettingsService;
    private readonly ILogger<SettingsViewModel> _logger;

    public SettingsViewModel(
        IDeepFeatureExtractor deepExtractor,
        IOptionsMonitor<OnnxModelOptions> onnxOptions,
        IOptionsMonitor<PredictionOptions> predictionOptions,
        IProfileService profileService,
        IUserSettingsService userSettingsService,
        ILogger<SettingsViewModel> logger)
    {
        _deepExtractor = deepExtractor;
        _onnxOptions = onnxOptions;
        _predictionOptions = predictionOptions;
        _profileService = profileService;
        _userSettingsService = userSettingsService;
        _logger = logger;

        // 从当前配置初始化 UI 显示值
        var currentOptions = _predictionOptions.CurrentValue;
        _acousticWeight = currentOptions.AcousticWeight;
        _deepWeight = currentOptions.DeepWeight;
        _onnxModelPath = _onnxOptions.CurrentValue.VggishModelPath ?? string.Empty;
        _enableDeepFeatures = _onnxOptions.CurrentValue.EnableDeepFeatures;
        _isModelLoaded = deepExtractor.IsModelLoaded;
    }

    /// <summary>声学特征相似度权重（与深度权重之和应为 1.0）</summary>
    [ObservableProperty]
    private double _acousticWeight;

    /// <summary>深度特征相似度权重</summary>
    [ObservableProperty]
    private double _deepWeight;

    /// <summary>VGGish ONNX 模型文件路径</summary>
    [ObservableProperty]
    private string _onnxModelPath;

    /// <summary>是否启用深度特征提取</summary>
    [ObservableProperty]
    private bool _enableDeepFeatures;

    /// <summary>当前模型是否已加载</summary>
    [ObservableProperty]
    private bool _isModelLoaded;

    /// <summary>状态消息（用于显示操作结果）</summary>
    [ObservableProperty]
    private string _statusMessage = "就绪";

    /// <summary>加载 ONNX 模型到推理引擎</summary>
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

    /// <summary>保存当前预测权重到 usersettings.json</summary>
    [RelayCommand]
    private async Task SaveWeightsAsync()
    {
        var result = await _userSettingsService.SavePredictionWeightsAsync(AcousticWeight, DeepWeight);
        StatusMessage = result.IsSuccess
            ? $"权重已保存（声学 {AcousticWeight}，深度 {DeepWeight}）"
            : $"保存失败: {result.Error}";
    }

    /// <summary>保存 ONNX 模型配置到 usersettings.json</summary>
    [RelayCommand]
    private async Task SaveOnnxSettingsAsync()
    {
        var result = await _userSettingsService.SaveOnnxModelSettingsAsync(
            EnableDeepFeatures, OnnxModelPath);
        StatusMessage = result.IsSuccess
            ? "ONNX 配置已保存"
            : $"保存失败: {result.Error}";
    }

    /// <summary>全量重建用户画像（基于所有已标记喜欢的歌曲）</summary>
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
