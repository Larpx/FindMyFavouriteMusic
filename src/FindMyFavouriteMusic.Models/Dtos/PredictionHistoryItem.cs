namespace Larpx.PersonalTools.FindMyFavouriteMusic.Models.Dtos;

/// <summary>
/// 预测历史记录项，用于在预测页面展示历史分数列表。
/// </summary>
public class PredictionHistoryItem
{
    /// <summary>文件名（不含路径）</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>文件完整路径</summary>
    public string FilePath { get; init; } = string.Empty;

    /// <summary>综合匹配分数（0-100）</summary>
    public double Score { get; init; }

    /// <summary>检测类型显示文本（如"深度模式"或"声学模式"）</summary>
    public string ModeText { get; init; } = string.Empty;

    /// <summary>声学特征分数（0-100）</summary>
    public double AcousticScore { get; init; }

    /// <summary>深度特征分数（0-100），null 表示不可用</summary>
    public double? DeepScore { get; init; }

    /// <summary>模型类型显示文本（如"VGGish"或"MERT"，声学模式下为"无"）</summary>
    public string ModelType { get; init; } = string.Empty;

    /// <summary>预测时间</summary>
    public DateTime PredictedAt { get; init; } = DateTime.UtcNow;
}
