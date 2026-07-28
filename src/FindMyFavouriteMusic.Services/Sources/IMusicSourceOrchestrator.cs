using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Results;
using Larpx.PersonalTools.FindMyFavouriteMusic.Sources.Abstractions;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Services.Sources;

public sealed class LikedImportProgress
{
    public int Total { get; init; }
    public int Processed { get; init; }
    public int MatchedLocal { get; init; }
    public int DownloadedTemp { get; init; }
    public int Failed { get; init; }
    public string? CurrentTitle { get; init; }
}

public sealed class RecommendScoreProgress
{
    public int Total { get; init; }
    public int Processed { get; init; }
    public string? CurrentTitle { get; init; }
}

public interface IMusicSourceOrchestrator
{
    /// <summary>
    /// 从指定源导入喜欢：匹配本地并标记；缺失则临时下载→提特征→更新画像→删除临时文件。
    /// </summary>
    Task<Result> ImportLikedAsync(
        string sourceId,
        IProgress<LikedImportProgress>? progress = null,
        CancellationToken ct = default);

    /// <summary>
    /// 拉取日推（或历史日推），临时下载打分后删除文件，结果写入 RecommendResults 并按分数降序。
    /// </summary>
    Task<Result<IReadOnlyList<RecommendResultRow>>> FetchAndScoreRecommendAsync(
        string sourceId,
        string? historyDate = null,
        IProgress<RecommendScoreProgress>? progress = null,
        CancellationToken ct = default);
}
