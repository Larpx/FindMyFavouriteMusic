namespace FindMyFavouriteMusic.Core.Features;

/// <summary>
/// Z-Score 归一化器
/// </summary>
public static class FeatureNormalizer
{
    /// <summary>
    /// 对特征向量进行 Z-Score 归一化
    /// </summary>
    public static float[] Normalize(float[] features)
    {
        ArgumentNullException.ThrowIfNull(features);

        if (features.Length == 0) return features;

        var mean = features.Average();
        var std = Math.Sqrt(features.Average(f => (f - mean) * (f - mean)));

        if (std < 1e-10) return features;

        var normalized = new float[features.Length];
        for (var i = 0; i < features.Length; i++)
        {
            normalized[i] = (float)((features[i] - mean) / std);
        }
        return normalized;
    }
}
