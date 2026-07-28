namespace Larpx.PersonalTools.FindMyFavouriteMusic.Models.Entities;

/// <summary>
/// 用户画像实体，映射数据库 UserProfile 表
/// </summary>
public class UserProfile
{
    /// <summary>主键（当前单画像场景固定为 1）</summary>
    public int Id { get; set; }

    /// <summary>声学特征均值向量</summary>
    public float[]? AcousticMeanVector { get; set; }

    /// <summary>声学特征均值向量的 BLOB 存储</summary>
    public byte[]? AcousticMeanVectorBlob { get; set; }

    /// <summary>深度特征均值向量</summary>
    public float[]? DeepMeanVector { get; set; }

    /// <summary>深度特征均值向量的 BLOB 存储</summary>
    public byte[]? DeepMeanVectorBlob { get; set; }

    /// <summary>声学均值所基于的样本数</summary>
    public int AcousticSampleCount { get; set; }

    /// <summary>深度均值所基于的样本数</summary>
    public int DeepSampleCount { get; set; }

    /// <summary>画像最后更新时间</summary>
    public DateTime LastUpdated { get; set; }
}
