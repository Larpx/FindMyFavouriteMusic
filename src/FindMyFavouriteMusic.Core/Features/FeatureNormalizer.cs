namespace Larpx.PersonalTools.FindMyFavouriteMusic.Core.Features;

/// <summary>
/// Z-Score 归一化器：对特征向量做标准化处理。
/// </summary>
/// <remarks>
/// Z-Score 公式：z[i] = (x[i] - μ) / σ
/// <para>其中 μ 为向量均值，σ 为向量标准差。归一化后向量均值为 0、标准差为 1。</para>
/// <para>注意：此处为"逐向量"Z-Score（计算单个向量的 μ 和 σ），而非"逐特征维度"Z-Score。</para>
/// <para>逐向量 Z-Score 会抹平异构特征（如 MFCC vs 色度）间的尺度差异，</para>
/// <para>可能损害余弦相似度的判别力，因此默认关闭（见 FeatureExtractionOptions.EnableNormalization）。</para>
/// <para>启用场景：当不同歌曲的特征向量绝对幅值差异过大时，可用于消除幅值影响。</para>
/// </remarks>
public static class FeatureNormalizer
{
    /// <summary>
    /// 对特征向量进行 Z-Score 归一化。
    /// </summary>
    /// <param name="features">原始特征向量</param>
    /// <returns>归一化后的向量；若向量方差为 0（所有元素相同）则返回原向量</returns>
    public static float[] Normalize(float[] features)
    {
        ArgumentNullException.ThrowIfNull(features);

        if (features.Length == 0) return features;

        // 计算均值 μ
        var mean = features.Average();

        // 计算标准差 σ = sqrt(E[(x - μ)²])
        var std = Math.Sqrt(features.Average(f => (f - mean) * (f - mean)));

        // 方差为 0 时直接返回原向量，避免除零
        if (std < 1e-10) return features;

        // 应用 Z-Score：z[i] = (x[i] - μ) / σ
        var normalized = new float[features.Length];
        for (var i = 0; i < features.Length; i++)
        {
            normalized[i] = (float)((features[i] - mean) / std);
        }
        return normalized;
    }
}
