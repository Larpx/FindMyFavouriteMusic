namespace Larpx.PersonalTools.FindMyFavouriteMusic.Core.Interfaces;

/// <summary>
/// 特征聚合接口，将帧级特征聚合为歌曲级特征
/// </summary>
public interface IFeatureAggregator
{
    /// <summary>将多帧特征聚合为单个向量（均值+方差拼接）</summary>
    float[] Aggregate(float[][] frameFeatures);
}
