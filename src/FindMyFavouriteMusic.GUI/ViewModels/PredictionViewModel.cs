using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Larpx.PersonalTools.FindMyFavouriteMusic.GUI.Services;
using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Dtos;
using Larpx.PersonalTools.FindMyFavouriteMusic.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.GUI.ViewModels;

/// <summary>
/// 预测 ViewModel，负责音乐文件的特征预测与匹配度展示。
/// </summary>
/// <remarks>
/// 在 MVVM 模式中，本类通过 <see cref="ObservablePropertyAttribute"/> 暴露预测结果供 View 绑定，
/// 通过 <see cref="RelayCommandAttribute"/> 暴露预测和文件选择命令供 View 触发。
/// 业务逻辑委托给 <see cref="IPredictionService"/>（特征提取与匹配）和 <see cref="IProfileService"/>（用户画像检查）。
/// <para>
/// 与 <c>MusicLibraryViewModel</c> 一样，采用 FilePicker 回调模式避免直接依赖 Avalonia 的 StorageProvider，
/// 保持 ViewModel 与 UI 框架解耦。
/// </para>
/// </remarks>
public partial class PredictionViewModel : ViewModelBase
{
    // 预测服务：负责音频特征提取与匹配度计算，通过 DI 注入
    private readonly IPredictionService _predictionService;
    // 画像服务：用于检查用户是否已建立喜好画像，通过 DI 注入
    private readonly IProfileService _profileService;
    // 日志记录器：用于记录异常和关键操作
    private readonly ILogger<PredictionViewModel> _logger;
    // 对话框服务：用于向用户弹出操作反馈
    private readonly IDialogService _dialogService;

    /// <summary>
    /// 文件选择交互回调，由 View 层（Code-behind）在运行时设置。
    /// </summary>
    /// <remarks>
    /// 采用回调模式而非直接依赖 Avalonia 的 <c>StorageProvider</c>，是为了：
    /// - 保持 ViewModel 与 UI 框架解耦，便于单元测试与跨平台复用；
    /// - View 层负责具体的文件选择对话框交互，ViewModel 只关心返回的路径字符串。
    /// 返回 null 表示用户取消了选择。
    /// </remarks>
    public Func<Task<string?>>? FilePicker { get; set; }

    /// <summary>
    /// 构造函数，通过依赖注入获取预测服务、画像服务和日志组件。
    /// </summary>
    /// <param name="predictionService">预测服务</param>
    /// <param name="profileService">用户画像服务</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="dialogService">对话框服务</param>
    public PredictionViewModel(
        IPredictionService predictionService,
        IProfileService profileService,
        ILogger<PredictionViewModel> logger,
        IDialogService dialogService)
    {
        _predictionService = predictionService;
        _profileService = profileService;
        _logger = logger;
        _dialogService = dialogService;
    }

    /// <summary>
    /// 当前选中的音乐文件路径，供 View 的文件路径显示控件绑定。
    /// </summary>
    [ObservableProperty]
    private string _selectedFilePath = string.Empty;

    /// <summary>
    /// 综合预测得分（0-100），融合声学和深度特征，供 View 的得分显示控件绑定。
    /// </summary>
    [ObservableProperty]
    private double _predictionScore;

    /// <summary>
    /// 声学特征匹配得分（0-100），无论何种预测模式都会有值。
    /// </summary>
    [ObservableProperty]
    private double _acousticScore;

    /// <summary>
    /// 深度特征匹配得分（0-100），可空。
    /// </summary>
    /// <remarks>
    /// 仅在 <see cref="PredictionMode.AcousticAndDeep"/> 模式下有值；
    /// 在 <see cref="PredictionMode.AcousticOnly"/> 模式下为 null，UI 据此隐藏深度得分显示。
    /// </remarks>
    [ObservableProperty]
    private double? _deepScore;

    /// <summary>
    /// 当前预测模式的 UI 显示文本（如"声学模式"/"深度增强模式"），供 View 的模式标签绑定。
    /// </summary>
    [ObservableProperty]
    private string _currentMode = "声学模式";

    /// <summary>
    /// 是否正在预测，用于控制按钮禁用状态和防重入。
    /// </summary>
    [ObservableProperty]
    private bool _isPredicting;

    /// <summary>
    /// 状态消息，供 View 的状态栏文本绑定，向用户反馈当前操作结果。
    /// </summary>
    [ObservableProperty]
    private string _statusMessage = "请选择音乐文件进行预测";

    /// <summary>
    /// 是否已存在用户画像，用于预检查和 UI 提示。
    /// </summary>
    /// <remarks>
    /// 预测依赖用户喜好画像（由喜欢的歌曲构建），无画像时预测无意义。
    /// 此属性供 View 据此显示引导提示。
    /// </remarks>
    [ObservableProperty]
    private bool _hasProfile;

    /// <summary>
    /// 执行预测命令：对选中的音乐文件进行特征提取与匹配度计算。
    /// </summary>
    /// <remarks>
    /// 业务流程：
    /// 1. 校验是否已选择文件；
    /// 2. 预检查用户画像（无画像时预测无意义，提前返回引导用户先标记喜欢的歌曲）；
    /// 3. 调用预测服务执行特征提取与匹配；
    /// 4. 将结果四舍五入到一位小数后更新到可观察属性，供 UI 显示；
    /// 5. 根据预测模式映射 UI 显示文本。
    /// </remarks>
    [RelayCommand]
    private async Task PredictAsync()
    {
        // 前置校验：未选择文件时直接提示，避免无效的服务调用
        if (string.IsNullOrWhiteSpace(SelectedFilePath))
        {
            StatusMessage = "请先选择音乐文件";
            return;
        }

        // 画像预检查：避免无画像时执行无意义的音频解码和特征提取（耗时操作）
        HasProfile = await _profileService.HasProfileAsync();
        if (!HasProfile)
        {
            StatusMessage = "请先在音乐库中标记喜欢的歌曲以构建画像";
            return;
        }

        // 进入预测状态：UI 据此禁用按钮、显示加载指示
        IsPredicting = true;
        StatusMessage = "正在预测...";

        try
        {
            var result = await _predictionService.PredictAsync(SelectedFilePath);

            if (result.IsSuccess)
            {
                var prediction = result.Value!;
                // Math.Round 保留一位小数，避免 UI 显示过长浮点数（如 78.456789 -> 78.5）
                PredictionScore = Math.Round(prediction.Score, 1);
                AcousticScore = Math.Round(prediction.AcousticScore, 1);
                // 深度得分可空：仅声学模式下为 null，需特殊处理避免对 null 调用 Round
                DeepScore = prediction.DeepScore.HasValue ? Math.Round(prediction.DeepScore.Value, 1) : null;
                // 将预测模式枚举映射为用户友好的中文显示文本
                CurrentMode = prediction.Mode == PredictionMode.AcousticAndDeep ? "深度增强模式" : "声学模式";
                StatusMessage = $"预测完成: {prediction.SongTitle} - 匹配度 {PredictionScore}%";
                await _dialogService.ShowSuccessAsync("预测完成",
                    $"{prediction.SongTitle}\n匹配度: {PredictionScore}%");
            }
            else
            {
                StatusMessage = $"预测失败: {result.Error}";
                await _dialogService.ShowErrorAsync("预测失败", result.Error ?? "未知错误");
            }
        }
        catch (Exception ex)
        {
            // 捕获未预期异常并记录日志，避免应用崩溃
            _logger.LogError(ex, "预测失败");
            StatusMessage = $"预测出错: {ex.Message}";
            await _dialogService.ShowErrorAsync("预测出错", ex.Message);
        }
        finally
        {
            // 无论成功失败都重置预测状态，确保 UI 可再次操作
            IsPredicting = false;
        }
    }

    /// <summary>
    /// 选择音乐文件命令：通过 FilePicker 回调弹出文件选择对话框。
    /// </summary>
    /// <remarks>
    /// 选择成功后更新 <see cref="SelectedFilePath"/>，触发属性变更通知，
    /// UI 会显示新路径并启用预测按钮。
    /// </remarks>
    [RelayCommand]
    private async Task SelectFileAsync()
    {
        if (FilePicker is not null)
        {
            // 通过回调让 View 层弹出文件选择对话框
            var path = await FilePicker();
            if (!string.IsNullOrWhiteSpace(path))
            {
                // 更新选中路径，触发 UI 刷新
                SelectedFilePath = path;
            }
        }
        else
        {
            // View 层未设置回调，提示用户而非抛异常，保证健壮性
            StatusMessage = "请选择音乐文件...";
        }
    }
}
