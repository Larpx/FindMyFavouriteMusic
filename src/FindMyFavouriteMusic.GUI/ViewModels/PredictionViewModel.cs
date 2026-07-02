using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Configuration;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Interfaces;
using Larpx.PersonalTools.FindMyFavouriteMusic.GUI.Services;
using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Dtos;
using Larpx.PersonalTools.FindMyFavouriteMusic.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.GUI.ViewModels;

/// <summary>
/// 预测 ViewModel，负责音乐文件的特征预测与匹配度展示。
/// </summary>
/// <remarks>
/// 功能：
/// - 单文件预测：拖拽或选择单个音频文件进行预测，展示实时进度和详细分数
/// - 批量预测：拖拽多个文件或选择目录进行批量预测，结果汇总到历史列表
/// - 历史分数：所有预测结果（单文件和批量）都记录在历史列表中
/// - 真实进度：基于预测各阶段（解码/声学提取/深度提取/评分）上报真实进度
/// </remarks>
public partial class PredictionViewModel : ViewModelBase
{
    private readonly IPredictionService _predictionService;
    private readonly IProfileService _profileService;
    private readonly IDeepFeatureExtractor _deepExtractor;
    private readonly ILogger<PredictionViewModel> _logger;
    private readonly IDialogService _dialogService;

    /// <summary>文件夹选择交互回调，用于批量预测时选择目录</summary>
    public Func<Task<string?>>? FolderPicker { get; set; }

    /// <summary>文件选择交互回调（单文件）</summary>
    public Func<Task<string?>>? FilePicker { get; set; }

    /// <summary>多文件选择交互回调，用于批量预测</summary>
    public Func<Task<IReadOnlyList<string>?>>? MultiFilePicker { get; set; }

    /// <summary>
    /// 构造函数
    /// </summary>
    public PredictionViewModel(
        IPredictionService predictionService,
        IProfileService profileService,
        IDeepFeatureExtractor deepExtractor,
        ILogger<PredictionViewModel> logger,
        IDialogService dialogService)
    {
        _predictionService = predictionService;
        _profileService = profileService;
        _deepExtractor = deepExtractor;
        _logger = logger;
        _dialogService = dialogService;

        _currentMode = _deepExtractor.IsModelLoaded ? "深度模式" : "声学模式";
        _modelTypeText = _deepExtractor.IsModelLoaded ? GetModelTypeText() : "无";
    }

    /// <summary>当前选中的音乐文件路径</summary>
    [ObservableProperty]
    private string _selectedFilePath = string.Empty;

    /// <summary>综合预测得分（0-100）</summary>
    [ObservableProperty]
    private double _predictionScore;

    /// <summary>声学特征匹配得分（0-100）</summary>
    [ObservableProperty]
    private double _acousticScore;

    /// <summary>深度特征匹配得分（0-100），可空</summary>
    [ObservableProperty]
    private double? _deepScore;

    /// <summary>当前预测模式的 UI 显示文本</summary>
    [ObservableProperty]
    private string _currentMode;

    /// <summary>当前模型类型显示文本</summary>
    [ObservableProperty]
    private string _modelTypeText;

    /// <summary>是否正在预测</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PredictCommand))]
    [NotifyCanExecuteChangedFor(nameof(BatchPredictCommand))]
    private bool _isPredicting;

    /// <summary>预测进度（0-100），真实进度</summary>
    [ObservableProperty]
    private int _predictionProgress;

    /// <summary>进度描述文本（如"正在解码音频..."、"正在提取声学特征..."）</summary>
    [ObservableProperty]
    private string _progressDescription = string.Empty;

    /// <summary>状态消息</summary>
    [ObservableProperty]
    private string _statusMessage = "请选择音乐文件进行预测";

    /// <summary>是否已存在用户画像</summary>
    [ObservableProperty]
    private bool _hasProfile;

    /// <summary>
    /// 预测历史记录列表，所有预测结果（单文件和批量）都追加到此列表。
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<PredictionHistoryItem> _historyItems = [];

    /// <summary>
    /// 批量预测选中的目录路径
    /// </summary>
    [ObservableProperty]
    private string _batchDirectoryPath = string.Empty;

    /// <summary>
    /// 获取当前模型类型的显示文本
    /// </summary>
    private string GetModelTypeText()
    {
        if (!_deepExtractor.IsModelLoaded) return "无";
        return _deepExtractor.FeatureDimension switch
        {
            768 => "MERT",
            128 => "VGGish",
            _ => $"未知({_deepExtractor.FeatureDimension}维)"
        };
    }

    /// <summary>
    /// 根据进度值推断进度描述文本
    /// </summary>
    private static string GetProgressDescription(int progress)
    {
        return progress switch
        {
            < 5 => "正在加载用户画像...",
            < 25 => "正在解码音频...",
            < 50 => "正在提取声学特征...",
            < 75 => "正在提取深度特征...",
            < 90 => "正在计算匹配度...",
            < 100 => "正在生成结果...",
            100 => "预测完成",
            _ => string.Empty
        };
    }

    /// <summary>
    /// 将预测结果转换为历史记录项
    /// </summary>
    private PredictionHistoryItem ToHistoryItem(string filePath, PredictionResult prediction)
    {
        var modeText = prediction.Mode == PredictionMode.AcousticAndDeep ? "深度模式" : "声学模式";
        return new PredictionHistoryItem
        {
            FileName = Path.GetFileNameWithoutExtension(filePath),
            FilePath = filePath,
            Score = Math.Round(prediction.Score, 1),
            ModeText = modeText,
            AcousticScore = Math.Round(prediction.AcousticScore, 1),
            DeepScore = prediction.DeepScore.HasValue ? Math.Round(prediction.DeepScore.Value, 1) : null,
            ModelType = prediction.Mode == PredictionMode.AcousticAndDeep ? GetModelTypeText() : "无",
            PredictedAt = DateTime.Now
        };
    }

    /// <summary>
    /// 执行单文件预测命令
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanPredict))]
    private async Task PredictAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedFilePath))
        {
            StatusMessage = "请先选择音乐文件";
            return;
        }

        await ExecutePredictionAsync([SelectedFilePath]);
    }

    /// <summary>
    /// 批量预测命令（无参版本：通过文件选择器选择文件）
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanPredict))]
    private async Task BatchPredictAsync()
    {
        // 优先使用多文件选择器，否则使用目录选择
        List<string> files;

        if (MultiFilePicker is not null)
        {
            var selectedFiles = await MultiFilePicker();
            if (selectedFiles is null || selectedFiles.Count == 0)
            {
                StatusMessage = "已取消选择";
                return;
            }
            files = [.. selectedFiles];
        }
        else if (FolderPicker is not null)
        {
            var dir = await FolderPicker();
            if (string.IsNullOrWhiteSpace(dir))
            {
                StatusMessage = "已取消选择";
                return;
            }

            // 扫描目录中的音频文件
            var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { ".mp3", ".wav", ".flac", ".ogg", ".m4a" };
            try
            {
                files = Directory.EnumerateFiles(dir, "*.*")
                    .Where(f => extensions.Contains(Path.GetExtension(f)))
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "扫描目录失败: {Dir}", dir);
                StatusMessage = $"扫描目录失败: {ex.Message}";
                return;
            }

            if (files.Count == 0)
            {
                StatusMessage = "目录中未找到音频文件";
                return;
            }
        }
        else
        {
            StatusMessage = "请先配置文件选择器";
            return;
        }

        await ExecutePredictionAsync(files);
    }

    /// <summary>
    /// 批量预测命令（带参版本：直接传入文件路径列表，用于拖拽多文件场景）。
    /// 源生成器会生成 BatchPredictWithFilesCommand。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanPredict))]
    private async Task BatchPredictWithFilesAsync(IReadOnlyList<string> filePaths)
    {
        await ExecutePredictionAsync(filePaths);
    }

    /// <summary>
    /// 执行预测（单文件或批量），带真实进度和历史记录。
    /// </summary>
    private async Task ExecutePredictionAsync(IReadOnlyList<string> filePaths)
    {
        // 画像预检查
        HasProfile = await _profileService.HasProfileAsync();
        if (!HasProfile)
        {
            StatusMessage = "请先在音乐库中标记喜欢的歌曲以构建画像";
            return;
        }

        IsPredicting = true;
        PredictionProgress = 0;
        ProgressDescription = "正在准备...";
        var isBatch = filePaths.Count > 1;
        StatusMessage = isBatch ? $"正在批量预测 {filePaths.Count} 个文件..." : "正在预测...";

        try
        {
            if (isBatch)
            {
                var progress = new Progress<int>(p =>
                {
                    PredictionProgress = p;
                    ProgressDescription = GetProgressDescription(p);
                    StatusMessage = $"批量预测中... ({p}%)";
                });

                var results = await _predictionService.PredictBatchAsync(filePaths, progress);

                // 处理批量结果
                var successCount = 0;
                for (var i = 0; i < results.Count; i++)
                {
                    if (results[i].IsSuccess && results[i].Value is not null)
                    {
                        var item = ToHistoryItem(filePaths[i], results[i].Value!);
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => HistoryItems.Insert(0, item));
                        successCount++;
                    }
                }

                StatusMessage = $"批量预测完成: {successCount}/{filePaths.Count} 成功";
            }
            else
            {
                // 单文件预测：同时更新当前展示区
                var progress = new Progress<int>(p =>
                {
                    PredictionProgress = p;
                    ProgressDescription = GetProgressDescription(p);
                });

                var result = await _predictionService.PredictWithProgressAsync(filePaths[0], progress);

                if (result.IsSuccess && result.Value is not null)
                {
                    var prediction = result.Value!;
                    PredictionScore = Math.Round(prediction.Score, 1);
                    AcousticScore = Math.Round(prediction.AcousticScore, 1);
                    DeepScore = prediction.DeepScore.HasValue ? Math.Round(prediction.DeepScore.Value, 1) : null;

                    CurrentMode = prediction.Mode == PredictionMode.AcousticAndDeep
                        ? "深度模式"
                        : _deepExtractor.IsModelLoaded ? "声学模式（降级）" : "声学模式";

                    ModelTypeText = GetModelTypeText();

                    // 添加到历史记录
                    var item = ToHistoryItem(filePaths[0], prediction);
                    HistoryItems.Insert(0, item);

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
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "预测已取消";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "预测失败");
            StatusMessage = $"预测出错: {ex.Message}";
            await _dialogService.ShowErrorAsync("预测出错", ex.Message);
        }
        finally
        {
            IsPredicting = false;
            PredictionProgress = 0;
            ProgressDescription = string.Empty;
        }
    }

    /// <summary>预测按钮可用条件</summary>
    private bool CanPredict() => !IsPredicting;

    /// <summary>选择音乐文件命令</summary>
    [RelayCommand]
    private async Task SelectFileAsync()
    {
        if (FilePicker is not null)
        {
            var path = await FilePicker();
            if (!string.IsNullOrWhiteSpace(path))
            {
                SelectedFilePath = path;
            }
        }
        else
        {
            StatusMessage = "请选择音乐文件...";
        }
    }

    /// <summary>文件保存对话框回调，用于导出历史记录时选择保存路径</summary>
    public Func<Task<string?>>? SaveFilePicker { get; set; }

    /// <summary>清空历史记录命令</summary>
    [RelayCommand]
    private void ClearHistory()
    {
        HistoryItems.Clear();
        StatusMessage = "历史记录已清空";
    }

    /// <summary>导出历史记录命令</summary>
    [RelayCommand]
    private async Task ExportHistoryAsync()
    {
        if (HistoryItems.Count == 0)
        {
            StatusMessage = "没有可导出的历史记录";
            return;
        }

        if (SaveFilePicker is null)
        {
            StatusMessage = "文件保存对话框不可用";
            return;
        }

        var savePath = await SaveFilePicker();
        if (string.IsNullOrWhiteSpace(savePath))
        {
            // 用户取消了保存对话框
            return;
        }

        try
        {
            // 导出为 CSV 格式
            var lines = new List<string>
            {
                "文件名,综合分数,检测类型,声学分数,深度分数,模型,路径,预测时间"
            };

            foreach (var item in HistoryItems)
            {
                var deepScore = item.DeepScore.HasValue ? $"{item.DeepScore:F1}%" : "-";
                lines.Add(
                    $"\"{item.FileName}\",{item.Score:F1}%,{item.ModeText},{item.AcousticScore:F1}%,{deepScore},{item.ModelType},\"{item.FilePath}\",{item.PredictedAt:yyyy-MM-dd HH:mm:ss}");
            }

            await File.WriteAllLinesAsync(savePath, lines);
            StatusMessage = $"历史记录已导出到: {savePath}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "导出历史记录失败");
            StatusMessage = $"导出失败: {ex.Message}";
        }
    }
}
