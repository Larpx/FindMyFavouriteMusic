using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Configuration;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Interfaces;
using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Core.Features;

/// <summary>
/// 深度特征提取器工厂：根据运行时模型类型动态切换 VGGish 或 MERT 提取器。
/// </summary>
/// <remarks>
/// <para>该工厂实现了 <see cref="IDeepFeatureExtractor"/> 接口，作为 VGGish 与 MERT 的统一入口。</para>
/// <para>与旧实现不同，本工厂在构造时创建两个提取器实例，通过 <see cref="LoadModel"/> 的
/// <paramref name="modelType"/> 参数在运行时切换活跃提取器，避免"配置为 VGGish 但用户想加载 MERT"时
/// 路由到错误提取器的严重 Bug。</para>
/// <para>活跃提取器的选择规则：</para>
/// <para>- 构造时根据配置的 <see cref="OnnxModelOptions.ModelType"/> 选择默认活跃提取器；</para>
/// <para>- 调用 <see cref="LoadModel"/> 时根据传入的 <paramref name="modelType"/> 切换活跃提取器。</para>
/// </remarks>
public class DeepFeatureExtractorFactory : IDeepFeatureExtractor
{
    private readonly DeepFeatureExtractor _vggishExtractor;
    private readonly MertFeatureExtractor _mertExtractor;
    private IDeepFeatureExtractor _activeExtractor;
    private readonly ILogger<DeepFeatureExtractorFactory> _logger;

    /// <summary>
    /// 构造工厂，创建 VGGish 与 MERT 两个提取器实例，并根据配置选择默认活跃提取器。
    /// </summary>
    /// <param name="options">ONNX 模型配置</param>
    /// <param name="loggerFactory">日志工厂，用于创建子提取器的 logger</param>
    /// <param name="logger">工厂自身日志</param>
    public DeepFeatureExtractorFactory(
        IOptions<OnnxModelOptions> options,
        ILoggerFactory loggerFactory,
        ILogger<DeepFeatureExtractorFactory> logger)
    {
        _logger = logger;

        // 预创建两个提取器，各自仅在配置匹配时自动加载模型，开销可忽略
        _vggishExtractor = new DeepFeatureExtractor(options,
            loggerFactory.CreateLogger<DeepFeatureExtractor>());
        _mertExtractor = new MertFeatureExtractor(options,
            loggerFactory.CreateLogger<MertFeatureExtractor>());

        // 根据配置选择默认活跃提取器
        _activeExtractor = options.Value.ModelType == DeepModelType.MERT
            ? _mertExtractor
            : _vggishExtractor;

        _logger.LogInformation("深度特征提取器已初始化，默认类型: {ModelType}", options.Value.ModelType);
    }

    /// <inheritdoc/>
    public bool IsModelLoaded => _activeExtractor.IsModelLoaded;

    /// <inheritdoc/>
    /// <remarks>返回当前活跃提取器的特征维度（VGGish=128, MERT=768）</remarks>
    public int FeatureDimension => _activeExtractor.FeatureDimension;

    /// <inheritdoc/>
    public Task<Result<float[]>> ExtractAsync(float[] samples, int sampleRate, CancellationToken ct = default)
    {
        return _activeExtractor.ExtractAsync(samples, sampleRate, ct);
    }

    /// <summary>
    /// 加载指定类型的 ONNX 模型，并切换活跃提取器。
    /// </summary>
    /// <param name="modelPath">ONNX 模型文件路径</param>
    /// <param name="modelType">模型类型，据此切换内部活跃提取器</param>
    /// <returns>加载结果</returns>
    public Result LoadModel(string modelPath, DeepModelType modelType)
    {
        // 根据传入的模型类型切换活跃提取器，确保后续 ExtractAsync 路由到正确的提取器
        _activeExtractor = modelType == DeepModelType.MERT
            ? _mertExtractor
            : _vggishExtractor;

        _logger.LogInformation("切换深度特征提取器为: {ModelType}", modelType);
        return _activeExtractor.LoadModel(modelPath, modelType);
    }

    /// <summary>当前活跃的模型类型</summary>
    public DeepModelType CurrentModelType => ReferenceEquals(_activeExtractor, _mertExtractor)
        ? DeepModelType.MERT
        : DeepModelType.VGGish;
}
