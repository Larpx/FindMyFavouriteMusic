using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Larpx.PersonalTools.FindMyFavouriteMusic.Services.Sources;
using Larpx.PersonalTools.FindMyFavouriteMusic.Sources.Abstractions;
using Microsoft.Extensions.Logging;
using QRCoder;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.GUI.ViewModels;

/// <summary>音乐源发现页：登录、导入红心、日推/历史日推打分。</summary>
public partial class DiscoverViewModel : ViewModelBase
{
    private readonly IMusicSourceRegistry _registry;
    private readonly IMusicSourceOrchestrator _orchestrator;
    private readonly RecommendResultRepository _recommendRepo;
    private readonly ILogger<DiscoverViewModel> _logger;
    private CancellationTokenSource? _cts;
    private string? _qrKey;

    public DiscoverViewModel(
        IMusicSourceRegistry registry,
        IMusicSourceOrchestrator orchestrator,
        RecommendResultRepository recommendRepo,
        ILogger<DiscoverViewModel> logger)
    {
        _registry = registry;
        _orchestrator = orchestrator;
        _recommendRepo = recommendRepo;
        _logger = logger;
    }

    [ObservableProperty] private string _authSummary = "未检查登录状态";
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _progressText = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private Bitmap? _qrImage;
    [ObservableProperty] private string? _selectedHistoryDate;
    [ObservableProperty] private ObservableCollection<string> _historyDates = [];
    [ObservableProperty] private ObservableCollection<RecommendResultRow> _results = [];

    [RelayCommand]
    private async Task RefreshAuthAsync()
    {
        var plugin = _registry.GetRequired(MusicSourceIds.Netease);
        var state = await plugin.GetAuthStateAsync();
        if (!state.IsSuccess || state.Value is null)
        {
            AuthSummary = state.Error ?? "读取登录状态失败";
            return;
        }

        var s = state.Value;
        AuthSummary = s.IsAuthenticated
            ? $"{s.DisplayName} (uid={s.UserId}) VIP={(s.IsVip ? $"是 Lv{s.VipLevel} 到期 {s.VipExpireAt?.LocalDateTime:yyyy-MM-dd}" : "否")}"
            : "未登录网易云";
    }

    [RelayCommand]
    private async Task BeginQrLoginAsync()
    {
        var plugin = _registry.GetRequired(MusicSourceIds.Netease);
        var session = await plugin.BeginQrLoginAsync();
        if (!session.IsSuccess || session.Value is null)
        {
            StatusText = session.Error ?? "获取二维码失败";
            return;
        }

        _qrKey = session.Value.Key;
        QrImage = RenderQr(session.Value.QrUrl);
        StatusText = "请使用网易云 App 扫码，并在手机上确认";

        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        _ = PollQrAsync(_cts.Token);
    }

    private async Task PollQrAsync(CancellationToken ct)
    {
        var plugin = _registry.GetRequired(MusicSourceIds.Netease);
        while (!ct.IsCancellationRequested && _qrKey is not null)
        {
            var poll = await plugin.PollQrLoginAsync(_qrKey, ct);
            if (!poll.IsSuccess || poll.Value is null)
            {
                StatusText = poll.Error ?? "轮询失败";
                break;
            }

            StatusText = poll.Value.Message ?? poll.Value.Status.ToString();
            if (poll.Value.Status == QrLoginStatus.Confirmed)
            {
                QrImage = null;
                await RefreshAuthAsync();
                StatusText = "登录成功";
                break;
            }

            if (poll.Value.Status == QrLoginStatus.Expired)
            {
                StatusText = "二维码已过期，请重新获取";
                break;
            }

            await Task.Delay(1500, ct);
        }
    }

    [RelayCommand]
    private async Task SignOutAsync()
    {
        await _registry.GetRequired(MusicSourceIds.Netease).SignOutAsync();
        AuthSummary = "已退出";
        QrImage = null;
    }

    [RelayCommand]
    private async Task ImportLikedAsync()
    {
        await RunBusyAsync(async ct =>
        {
            var progress = new Progress<LikedImportProgress>(p =>
                ProgressText = $"红心 {p.Processed}/{p.Total} 匹配{p.MatchedLocal} 下载{p.DownloadedTemp} 失败{p.Failed} {p.CurrentTitle}");
            var result = await _orchestrator.ImportLikedAsync(MusicSourceIds.Netease, progress, ct);
            StatusText = result.IsSuccess ? "红心导入完成（临时文件已删除）" : result.Error!;
        });
    }

    [RelayCommand]
    private async Task ScoreDailyAsync()
    {
        await RunBusyAsync(async ct =>
        {
            var progress = new Progress<RecommendScoreProgress>(p =>
                ProgressText = $"日推打分 {p.Processed}/{p.Total} {p.CurrentTitle}");
            var result = await _orchestrator.FetchAndScoreRecommendAsync(MusicSourceIds.Netease, null, progress, ct);
            if (!result.IsSuccess)
            {
                StatusText = result.Error!;
                return;
            }

            Results = new ObservableCollection<RecommendResultRow>(result.Value!);
            StatusText = $"日推完成：{Results.Count} 首（按分数降序）";
        });
    }

    [RelayCommand]
    private async Task LoadHistoryDatesAsync()
    {
        var plugin = _registry.GetRequired(MusicSourceIds.Netease);
        var dates = await plugin.GetHistoryRecommendDatesAsync();
        if (!dates.IsSuccess)
        {
            StatusText = dates.Error!;
            return;
        }

        HistoryDates = new ObservableCollection<string>(dates.Value!);
        SelectedHistoryDate = HistoryDates.FirstOrDefault();
        StatusText = $"历史日推日期 {HistoryDates.Count} 个";
    }

    [RelayCommand]
    private async Task ScoreHistoryAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedHistoryDate))
        {
            StatusText = "请先选择历史日期";
            return;
        }

        var date = SelectedHistoryDate;
        await RunBusyAsync(async ct =>
        {
            var progress = new Progress<RecommendScoreProgress>(p =>
                ProgressText = $"历史日推 {date} {p.Processed}/{p.Total}");
            var result = await _orchestrator.FetchAndScoreRecommendAsync(MusicSourceIds.Netease, date, progress, ct);
            if (!result.IsSuccess)
            {
                StatusText = result.Error!;
                return;
            }

            Results = new ObservableCollection<RecommendResultRow>(result.Value!);
            StatusText = $"历史日推 {date} 完成：{Results.Count} 首";
        });
    }

    [RelayCommand]
    private async Task ReloadSavedAsync()
    {
        var date = SelectedHistoryDate ?? DateTime.Now.ToString("yyyy-MM-dd");
        var rows = await _recommendRepo.GetBySourceDateAsync(MusicSourceIds.Netease, date);
        if (!rows.IsSuccess)
        {
            StatusText = rows.Error!;
            return;
        }

        Results = new ObservableCollection<RecommendResultRow>(rows.Value!);
        StatusText = $"已加载本地结果 {Results.Count} 条 ({date})";
    }

    private async Task RunBusyAsync(Func<CancellationToken, Task> action)
    {
        if (IsBusy) return;
        IsBusy = true;
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        try
        {
            await action(_cts.Token);
        }
        catch (OperationCanceledException)
        {
            StatusText = "已取消";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发现页任务失败");
            StatusText = ex.Message;
        }
        finally
        {
            IsBusy = false;
            ProgressText = "";
        }
    }

    private static Bitmap? RenderQr(string url)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(data);
        var bytes = png.GetGraphic(6);
        using var ms = new MemoryStream(bytes);
        return new Bitmap(ms);
    }
}
