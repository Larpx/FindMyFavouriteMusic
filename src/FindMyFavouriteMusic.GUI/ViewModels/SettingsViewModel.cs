using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Configuration;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Features;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Hardware;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Interfaces;
using Larpx.PersonalTools.FindMyFavouriteMusic.GUI.Services;
using Larpx.PersonalTools.FindMyFavouriteMusic.Services.Database;
using Larpx.PersonalTools.FindMyFavouriteMusic.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.GUI.ViewModels;

/// <summary>
/// 设置 ViewModel，提供预测权重调整、深度模型选择与加载、画像重建功能。
/// </summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly IDeepFeatureExtractor _deepExtractor;
    private readonly IOptionsMonitor<OnnxModelOptions> _onnxOptions;
    private readonly IOptionsMonitor<PredictionOptions> _predictionOptions;
    private readonly IProfileService _profileService;
    private readonly IUserSettingsService _userSettingsService;
    private readonly ILogger<SettingsViewModel> _logger;
    private readonly IDialogService _dialogService;
    private readonly IHardwareAccelerator _hardwareAccelerator;

    public SettingsViewModel(
        IDeepFeatureExtractor deepExtractor,
        IOptionsMonitor<OnnxModelOptions> onnxOptions,
        IOptionsMonitor<PredictionOptions> predictionOptions,
        IOptionsMonitor<ScanOptions> scanOptions,
        IProfileService profileService,
        IUserSettingsService userSettingsService,
        ILogger<SettingsViewModel> logger,
        IDialogService dialogService,
        IHardwareAccelerator hardwareAccelerator)
    {
        _deepExtractor = deepExtractor;
        _onnxOptions = onnxOptions;
        _predictionOptions = predictionOptions;
        _profileService = profileService;
        _userSettingsService = userSettingsService;
        _logger = logger;
        _dialogService = dialogService;
        _hardwareAccelerator = hardwareAccelerator;

        // 从当前配置初始化 UI 显示值
        var currentOptions = _predictionOptions.CurrentValue;
        _acousticWeight = currentOptions.AcousticWeight;
        _deepWeight = currentOptions.DeepWeight;
        _acousticOnlyWeight = currentOptions.AcousticOnlyWeight;

        var onnxConfig = _onnxOptions.CurrentValue;
        _selectedModelType = onnxConfig.ModelType.ToString();
        _vggishModelPath = onnxConfig.VggishModelPath ?? string.Empty;
        _mertModelPath = onnxConfig.MertModelPath ?? string.Empty;
        _enableDeepFeatures = onnxConfig.EnableDeepFeatures;
        _selectedExecutionProvider = onnxConfig.ExecutionProvider.ToString();
        _selectedOpenVinoDevice = onnxConfig.OpenVinoDevice.ToString();
        _openVinoCacheDir = onnxConfig.OpenVinoCacheDir ?? string.Empty;
        _maxConcurrentProcessing = scanOptions.CurrentValue.MaxConcurrentProcessing;
        _isModelLoaded = deepExtractor.IsModelLoaded;
        UpdateAcceleratorStatus();
    }

    /// <summary>声学特征相似度权重（与深度权重之和应为 1.0）</summary>
    [ObservableProperty]
    private double _acousticWeight;

    /// <summary>深度特征相似度权重</summary>
    [ObservableProperty]
    private double _deepWeight;

    /// <summary>仅声学模式下的权重（通常为 1.0）</summary>
    [ObservableProperty]
    private double _acousticOnlyWeight;

    /// <summary>OpenVINO 编译缓存目录</summary>
    [ObservableProperty]
    private string _openVinoCacheDir = string.Empty;

    /// <summary>扫描最大并发数</summary>
    [ObservableProperty]
    private int _maxConcurrentProcessing;

    /// <summary>当前选择的深度模型类型：VGGish 或 MERT</summary>
    [ObservableProperty]
    private string _selectedModelType;

    /// <summary>VGGish ONNX 模型文件路径</summary>
    [ObservableProperty]
    private string _vggishModelPath;

    /// <summary>MERT ONNX 模型文件路径</summary>
    [ObservableProperty]
    private string _mertModelPath;

    /// <summary>是否启用深度特征提取</summary>
    [ObservableProperty]
    private bool _enableDeepFeatures;

    /// <summary>
    /// 当前选择的 Execution Provider 模式：CPU 或 OpenVINO。
    /// </summary>
    /// <remarks>
    /// <para>v2.0 起仅支持 CPU 与 OpenVINO 两种模式（DirectML 已移除）。</para>
    /// <para>切换 EP 需重启应用生效（native 库在启动时加载，无法运行时切换）。</para>
    /// </remarks>
    [ObservableProperty]
    private string _selectedExecutionProvider;

    /// <summary>
    /// OpenVINO 目标设备：GPU / NPU / AUTO，仅当 <see cref="SelectedExecutionProvider"/> = OpenVINO 时生效。
    /// </summary>
    /// <remarks>
    /// <para>GPU：Intel 集成或独立 GPU（默认，测试最优，对 MERT 有 2.24x 加速）；</para>
    /// <para>NPU：Intel AI Boost NPU；</para>
    /// <para>AUTO：OpenVINO 运行时自动选择最佳设备。</para>
    /// </remarks>
    [ObservableProperty]
    private string _selectedOpenVinoDevice;

    /// <summary>
    /// 是否选择了 OpenVINO EP，用于控制 OpenVINO 设备选择控件的启用状态。
    /// </summary>
    public bool IsOpenVinoSelected => string.Equals(SelectedExecutionProvider, "OpenVINO", StringComparison.OrdinalIgnoreCase);

    /// <summary>OpenVINO 设备选择是否可用（仅 OpenVINO 模式下允许选择设备）</summary>
    public bool CanSelectOpenVinoDevice => IsOpenVinoSelected;

    partial void OnSelectedExecutionProviderChanged(string value)
    {
        OnPropertyChanged(nameof(IsOpenVinoSelected));
        OnPropertyChanged(nameof(CanSelectOpenVinoDevice));
    }

    /// <summary>当前模型是否已加载</summary>
    [ObservableProperty]
    private bool _isModelLoaded;

    /// <summary>是否正在加载模型，用于禁用按钮防止重入</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadModelCommand))]
    private bool _isLoadingModel;

    /// <summary>是否正在重建画像，用于禁用按钮防止重入</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RebuildProfileCommand))]
    private bool _isRebuilding;

    /// <summary>状态消息（用于显示操作结果）</summary>
    [ObservableProperty]
    private string _statusMessage = "就绪";

    /// <summary>
    /// NPU 检测结果与当前生效 EP 的合并显示文本，供设置页只读展示。
    /// </summary>
    /// <remarks>
    /// <para>该属性在以下时机刷新：</para>
    /// <para>- SettingsViewModel 构造完成时（启动后首次进入设置页）；</para>
    /// <para>- LoadModelCommand 完成后（手动加载模型会改变实际生效的 EP）。</para>
    /// </remarks>
    [ObservableProperty]
    private string _acceleratorStatusInfo = string.Empty;

    /// <summary>当前模型输出的特征维度</summary>
    public string FeatureDimensionInfo
    {
        get
        {
            if (!IsModelLoaded) return "未加载";
            return SelectedModelType == "MERT" ? "768 维" : "128 维";
        }
    }

    partial void OnSelectedModelTypeChanged(string value)
    {
        OnPropertyChanged(nameof(FeatureDimensionInfo));
    }

    partial void OnIsModelLoadedChanged(bool value)
    {
        OnPropertyChanged(nameof(FeatureDimensionInfo));
    }

    /// <summary>
    /// 从 <see cref="IHardwareAccelerator"/> 读取最新检测结果与生效 EP，刷新显示文本。
    /// </summary>
    /// <remarks>方法无副作用，仅更新 <see cref="AcceleratorStatusInfo"/> 属性。</remarks>
    private void UpdateAcceleratorStatus()
    {
        var npuPart = _hardwareAccelerator.IsNpuAvailable
            ? $"NPU 已检测到: {_hardwareAccelerator.NpuDeviceName}"
            : "NPU 未检测到";
        AcceleratorStatusInfo = $"{npuPart} | 当前推理设备: {_hardwareAccelerator.ActiveExecutionProvider}";
    }

    /// <summary>
    /// 将 UI 中的模型类型字符串解析为枚举，无效值回退为 MERT（与 v2.0 默认配置一致）。
    /// </summary>
    private static DeepModelType ParseModelType(string? modelType)
    {
        return Enum.TryParse<DeepModelType>(modelType, ignoreCase: true, out var result)
            ? result
            : DeepModelType.MERT;
    }

    /// <summary>
    /// 异步加载 ONNX 模型到推理引擎。
    /// </summary>
    /// <remarks>
    /// ONNX 模型加载为 CPU 密集型操作（可能数秒），通过 Task.Run 卸载到线程池避免冻结 UI。
    /// 通过 <see cref="IsLoadingModel"/> 禁用按钮防止用户重复点击。
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanLoadModel))]
    private async Task LoadModelAsync()
    {
        var modelType = ParseModelType(SelectedModelType);
        var modelPath = modelType == DeepModelType.MERT ? MertModelPath : VggishModelPath;

        if (string.IsNullOrWhiteSpace(modelPath))
        {
            StatusMessage = $"请先指定 {SelectedModelType} 模型文件路径";
            return;
        }

        IsLoadingModel = true;
        StatusMessage = $"正在加载 {SelectedModelType} 模型...";

        try
        {
            // ONNX InferenceSession 构造为 CPU 密集型，用 Task.Run 避免阻塞 UI 线程
            var result = await Task.Run(() => _deepExtractor.LoadModel(modelPath, modelType));
            IsModelLoaded = _deepExtractor.IsModelLoaded;
            // 加载完成后刷新 NPU/EP 状态显示，反映实际生效的推理设备
            UpdateAcceleratorStatus();
            if (result.IsSuccess)
            {
                StatusMessage = $"{SelectedModelType} 模型加载成功（{_deepExtractor.FeatureDimension} 维, EP={_hardwareAccelerator.ActiveExecutionProvider}）";
                await _dialogService.ShowSuccessAsync("模型加载成功",
                    $"{SelectedModelType} 模型已加载\n特征维度: {_deepExtractor.FeatureDimension} 维\n推理设备: {_hardwareAccelerator.ActiveExecutionProvider}");
            }
            else
            {
                StatusMessage = $"模型加载失败: {result.Error}";
                await _dialogService.ShowErrorAsync("模型加载失败", result.Error ?? "未知错误");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载模型失败: {ModelPath}", modelPath);
            StatusMessage = $"模型加载出错: {ex.Message}";
            await _dialogService.ShowErrorAsync("模型加载出错", ex.Message);
        }
        finally
        {
            IsLoadingModel = false;
        }
    }

    /// <summary>模型加载按钮可用条件：非加载中状态</summary>
    private bool CanLoadModel() => !IsLoadingModel;

    /// <summary>
    /// 保存当前预测权重到 usersettings.json。
    /// </summary>
    /// <remarks>
    /// 保存前校验权重范围 [0, 1] 及两者之和约为 1.0，防止用户输入非法值导致预测异常。
    /// </remarks>
    [RelayCommand]
    private async Task SaveWeightsAsync()
    {
        // 权重范围校验：允许 [0, 1] 区间，超出则提示用户
        if (AcousticWeight < 0 || AcousticWeight > 1 || DeepWeight < 0 || DeepWeight > 1)
        {
            StatusMessage = "权重值必须在 0~1 范围内";
            await _dialogService.ShowErrorAsync("输入无效", "权重值必须在 0~1 范围内");
            return;
        }

        // 权重之和校验：允许 0.05 容差，避免浮点精度导致误报
        var sum = AcousticWeight + DeepWeight;
        if (Math.Abs(sum - 1.0) > 0.05)
        {
            StatusMessage = $"声学权重与深度权重之和应为 1.0，当前为 {sum:F2}";
            await _dialogService.ShowErrorAsync("权重校验失败",
                $"声学权重与深度权重之和应为 1.0\n当前为 {sum:F2}");
            return;
        }

        try
        {
            var result = await _userSettingsService.SavePredictionWeightsAsync(
                AcousticWeight, DeepWeight, AcousticOnlyWeight);
            if (result.IsSuccess)
            {
                StatusMessage = $"权重已保存（声学 {AcousticWeight}，深度 {DeepWeight}，仅声学 {AcousticOnlyWeight}）";
                await _dialogService.ShowSuccessAsync("保存成功",
                    $"声学权重: {AcousticWeight}\n深度权重: {DeepWeight}\n仅声学权重: {AcousticOnlyWeight}");
            }
            else
            {
                StatusMessage = $"保存失败: {result.Error}";
                await _dialogService.ShowErrorAsync("保存失败", result.Error ?? "未知错误");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存预测权重失败");
            StatusMessage = $"保存出错: {ex.Message}";
            await _dialogService.ShowErrorAsync("保存出错", ex.Message);
        }
    }

    /// <summary>保存 ONNX 模型配置到 usersettings.json</summary>
    [RelayCommand]
    private async Task SaveOnnxSettingsAsync()
    {
        try
        {
            var result = await _userSettingsService.SaveOnnxModelSettingsAsync(
                EnableDeepFeatures, SelectedModelType, VggishModelPath, MertModelPath,
                SelectedExecutionProvider, SelectedOpenVinoDevice, OpenVinoCacheDir);
            if (result.IsSuccess)
            {
                StatusMessage = $"{SelectedModelType} 模型配置已保存（EP={SelectedExecutionProvider}，需重启应用生效）";
                await _dialogService.ShowSuccessAsync("配置已保存",
                    $"{SelectedModelType} 模型配置已保存\n推理 EP: {SelectedExecutionProvider}\n需重启应用后生效");
            }
            else
            {
                StatusMessage = $"保存失败: {result.Error}";
                await _dialogService.ShowErrorAsync("保存失败", result.Error ?? "未知错误");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存 ONNX 模型配置失败");
            StatusMessage = $"保存出错: {ex.Message}";
            await _dialogService.ShowErrorAsync("保存出错", ex.Message);
        }
    }

    /// <summary>保存扫描并发配置到 usersettings.json</summary>
    [RelayCommand]
    private async Task SaveScanSettingsAsync()
    {
        try
        {
            var result = await _userSettingsService.SaveScanSettingsAsync(MaxConcurrentProcessing);
            if (result.IsSuccess)
            {
                StatusMessage = $"扫描配置已保存（并发 {MaxConcurrentProcessing}，重启后完全生效）";
                await _dialogService.ShowSuccessAsync("保存成功",
                    $"最大并发处理数: {MaxConcurrentProcessing}\n建议重启应用后生效");
            }
            else
            {
                StatusMessage = $"保存失败: {result.Error}";
                await _dialogService.ShowErrorAsync("保存失败", result.Error ?? "未知错误");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存扫描配置失败");
            StatusMessage = $"保存出错: {ex.Message}";
            await _dialogService.ShowErrorAsync("保存出错", ex.Message);
        }
    }

    /// <summary>
    /// 全量重建用户画像（基于所有已标记喜欢的歌曲）。
    /// </summary>
    /// <remarks>
    /// 画像重建涉及全量音频特征提取，可能耗时较长，通过 <see cref="IsRebuilding"/> 禁用按钮防重入。
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanRebuildProfile))]
    private async Task RebuildProfileAsync()
    {
        IsRebuilding = true;
        StatusMessage = "正在重建画像...";

        try
        {
            var result = await _profileService.RebuildProfileAsync();
            if (result.IsSuccess)
            {
                StatusMessage = "画像重建完成";
                await _dialogService.ShowSuccessAsync("画像重建完成", "用户画像已基于最新喜好数据重建");
            }
            else
            {
                StatusMessage = $"画像重建失败: {result.Error}";
                await _dialogService.ShowErrorAsync("画像重建失败", result.Error ?? "未知错误");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "画像重建失败");
            StatusMessage = $"画像重建出错: {ex.Message}";
            await _dialogService.ShowErrorAsync("画像重建出错", ex.Message);
        }
        finally
        {
            IsRebuilding = false;
        }
    }

    /// <summary>画像重建按钮可用条件：非重建中状态</summary>
    private bool CanRebuildProfile() => !IsRebuilding;
}
