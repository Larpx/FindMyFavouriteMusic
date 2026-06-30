using FindMyFavouriteMusic.Core.Interfaces;

namespace FindMyFavouriteMusic.Core.Features;

/// <summary>
/// 特征聚合器，将帧级特征聚合为歌曲级特征（均值 + 方差拼接）
/// </summary>
public class FeatureAggregator : IFeatureAggregator
{
    /// <inheritdoc/>
    public float[] Aggregate(float[][] frameFeatures)
    {
        ArgumentNullException.ThrowIfNull(frameFeatures);

        if (frameFeatures.Length == 0)
        {
            throw new ArgumentException("帧特征不能为空", nameof(frameFeatures));
        }

        var dimension = frameFeatures[0].Length;
        var result = new float[dimension * 2]; // 均值 + 方差

        // 计算均值
        for (var d = 0; d < dimension; d++)
        {
            float sum = 0;
            for (var f = 0; f < frameFeatures.Length; f++)
            {
                sum += frameFeatures[f][d];
            }
            result[d] = sum / frameFeatures.Length;
        }

        // 计算方差
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
