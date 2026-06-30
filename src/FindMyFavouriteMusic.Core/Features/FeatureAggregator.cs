using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Interfaces;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Core.Features;

/// <summary>
/// 特征聚合器：将帧级特征序列聚合为歌曲级固定维度向量。
/// </summary>
/// <remarks>
/// 一首歌曲会产生数百到数千帧的特征（每帧约 25ms），直接用于相似度计算维度过高且不稳定。
/// <para>聚合策略：对每个特征维度计算"均值 + 方差"两个统计量，拼接为 2×d 维向量。</para>
/// <para>均值反映特征的中央趋势，方差反映特征的波动程度。</para>
/// <para>例如 MFCC 13 维 → 聚合后 26 维（13 均值 + 13 方差）。</para>
/// <para>选择均值+方差而非单一均值的原因：</para>
/// <para>- 均值无法区分节奏变化大与节奏稳定的歌曲；</para>
/// <para>- 方差补充了时间维度上的动态信息，提升相似度判别力。</para>
/// </remarks>
public class FeatureAggregator : IFeatureAggregator
{
    /// <inheritdoc/>
    /// <summary>
    /// 将多帧特征聚合为单个向量：前 d 位为均值，后 d 位为方差。
    /// </summary>
    /// <param name="frameFeatures">帧级特征数组，每帧一个 float[] 向量</param>
    /// <returns>2×d 维聚合向量（均值 + 方差拼接）</returns>
    public float[] Aggregate(float[][] frameFeatures)
    {
        ArgumentNullException.ThrowIfNull(frameFeatures);

        if (frameFeatures.Length == 0)
        {
            throw new ArgumentException("帧特征不能为空", nameof(frameFeatures));
        }

        var dimension = frameFeatures[0].Length;
        var result = new float[dimension * 2]; // 前 d 位存均值，后 d 位存方差

        // 第一遍：累加求和，计算均值
        for (var d = 0; d < dimension; d++)
        {
            float sum = 0;
            for (var f = 0; f < frameFeatures.Length; f++)
            {
                sum += frameFeatures[f][d];
            }
            result[d] = sum / frameFeatures.Length;
        }

        // 第二遍：基于均值计算方差（总体方差，除以 N 而非 N-1）
        for (var d = 0; d < dimension; d++)
        {
            float sumSq = 0;
            var mean = result[d];
            for (var f = 0; f < frameFeatures.Length; f++)
            {
                var diff = frameFeatures[f][d] - mean;
                sumSq += diff * diff;
            }
            result[dimension + d] = sumSq / frameFeatures.Length;
        }

        return result;
    }
}
