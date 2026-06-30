namespace FindMyFavouriteMusic.Models.Dtos;

/// <summary>
/// 预测结果
/// </summary>
public class PredictionResult
{
    /// <summary>总评分 0-100</summary>
    public double Score { get; set; }

    /// <summary>声学特征相似度（0-100）</summary>
    public double AcousticScore { get; set; }

    /// <summary>深度特征相似度（0-100），null 表示无深度特征</summary>
    public double? DeepScore { get; set; }

    /// <summary>当前使用的预测模式</summary>
    public PredictionMode Mode { get; set; }

    /// <summary>预测的歌曲标题</summary>
    public string? SongTitle { get; set; }
}

/// <summary>
/// 预测模式
/// </summary>
public enum PredictionMode
{
    /// <summary>仅使用声学特征</summary>
    AcousticOnly,
    /// <summary>声学特征 + 深度特征加权</summary>
    AcousticAndDeep
}
