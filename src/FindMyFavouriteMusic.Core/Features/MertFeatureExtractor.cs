using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Configuration;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Interfaces;
using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Core.Features;

/// <summary>
/// 基于 ONNX Runtime 的 MERT 深度特征提取器。
/// </summary>
/// <remarks>
/// <para><b>模型职责：</b>MERT（Music Entertainment Representation Transformer）
/// 是专为音乐理解设计的自监督预训练模型，将原始音频波形映射到 768 维嵌入向量，
/// 该向量捕捉了乐器、旋律、和声、氛围等高层音乐语义信息。</para>
/// <para><b>与 VGGish 的区别：</b></para>
/// <para>- VGGish：通用音频场景识别，128 维，需手动计算 mel 频谱图作为输入；</para>
/// <para>- MERT：音乐专用，768 维，直接接受原始波形输入，语义表达更丰富。</para>
/// <para><b>推理流水线：</b></para>
/// <para>1. 将音频重采样至 24kHz；</para>
/// <para>2. 以 5 秒窗口、50% 重叠分帧；</para>
/// <para>3. 构造 ONNX 输入张量 (1, 序列长度)；</para>
/// <para>4. 执行推理，取最后一层隐藏状态；</para>
/// <para>5. 对所有 token 的隐藏状态取时间平均，得到 768 维特征。</para>
/// </remarks>
public class MertFeatureExtractor : IDeepFeatureExtractor
{
    private readonly OnnxModelOptions _options;
    private readonly ILogger<MertFeatureExtractor> _logger;
    private InferenceSession? _session;

    /// <summary>MERT 输出嵌入向量的维度</summary>
    private const int MertOutputDimension = 768;

    /// <summary>MERT 模型要求的采样率：24kHz</summary>
    private const int MertTargetSampleRate = 24000;

    /// <summary>MERT 推理帧长（秒），5 秒为一帧</summary>
    private const double FrameDurationSeconds = 5.0;

    /// <summary>帧间重叠比例（50%）</summary>
    private const double OverlapRatio = 0.5;

    /// <summary>
    /// 构造 MERT 特征提取器，并按配置自动加载模型。
    /// </summary>
    /// <param name="options">ONNX 模型配置</param>
    /// <param name="logger">日志记录器</param>
    public MertFeatureExtractor(
        IOptions<OnnxModelOptions> options,
        ILogger<MertFeatureExtractor> logger)
    {
        _options = options.Value;
        _logger = logger;

        // 如果配置中启用了深度特征且有 MERT 模型路径，自动加载
        if (_options.EnableDeepFeatures
            && _options.ModelType == DeepModelType.MERT
            && !string.IsNullOrEmpty(_options.MertModelPath))
        {
            var result = LoadModel(_options.MertModelPath, DeepModelType.MERT);
            if (!result.IsSuccess)
            {
                _logger.LogWarning("MERT ONNX 模型自动加载失败: {Error}", result.Error);
            }
        }
    }

    /// <inheritdoc/>
    public bool IsModelLoaded => _session is not null;

    /// <inheritdoc/>
    public int FeatureDimension => MertOutputDimension;

    /// <summary>
    /// 加载指定路径的 MERT ONNX 模型到推理会话。
    /// </summary>
    /// <param name="modelPath">ONNX 模型文件的绝对路径。</param>
    /// <param name="modelType">模型类型（由工厂传入，本提取器固定为 MERT，忽略此参数）。</param>
    /// <returns>加载结果</returns>
    public Result LoadModel(string modelPath, DeepModelType modelType)
    {
        if (!File.Exists(modelPath))
        {
            return Result.Failure($"模型文件不存在: {modelPath}");
        }

        try
        {
            _session = new InferenceSession(modelPath);
            _logger.LogInformation("MERT ONNX 模型加载成功: {ModelPath}", modelPath);
            return Result.Success();
        }
        catch (Exception ex)
        {
            _session = null;
            _logger.LogError(ex, "MERT ONNX 模型加载失败: {ModelPath}", modelPath);
            return Result.Failure(ex);
        }
    }

    /// <summary>
    /// 异步提取一段音频的 MERT 深度特征向量。
    /// </summary>
    /// <param name="samples">单声道 PCM 浮点采样数据，范围 [-1, 1]。</param>
    /// <param name="sampleRate">采样率（Hz）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>成功时返回 768 维特征向量；失败时返回 Failure。</returns>
    public async Task<Result<float[]>> ExtractAsync(float[] samples, int sampleRate, CancellationToken ct = default)
    {
        if (!IsModelLoaded)
        {
            return Result<float[]>.Failure("MERT 模型未加载");
        }

        try
        {
            var result = await Task.Run(() => ExtractInternal(samples, sampleRate), ct);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MERT 深度特征提取失败");
            return Result<float[]>.Failure(ex);
        }
    }

    /// <summary>
    /// 执行 MERT 推理流水线（同步实现）。
    /// </summary>
    /// <param name="samples">单声道 PCM 浮点采样数据。</param>
    /// <param name="sampleRate">采样率（Hz）。</param>
    /// <returns>768 维时间平均嵌入向量</returns>
    /// <remarks>
    /// <para>流水线步骤：</para>
    /// <para>1. 重采样至 24kHz（线性插值）；</para>
    /// <para>2. 以 5 秒窗口、50% 重叠分帧；</para>
    /// <para>3. 每帧构造输入张量送入 ONNX 推理；</para>
    /// <para>4. 取最后一层隐藏状态，对所有 token 求均值；</para>
    /// <para>5. 所有帧的嵌入按维度求均值，得到 768 维最终特征。</para>
    /// </remarks>
    private Result<float[]> ExtractInternal(float[] samples, int sampleRate)
    {
        // 步骤 1：重采样至 24kHz
        var resampled = Resample(samples, sampleRate, MertTargetSampleRate);

        // 步骤 2：分帧参数
        var frameSize = (int)(FrameDurationSeconds * MertTargetSampleRate);
        var hopSize = (int)(frameSize * (1.0 - OverlapRatio));

        if (resampled.Length < frameSize)
        {
            // 音频不足一帧时，直接用整段音频推理
            return InferenceSingleChunk(resampled);
        }

        // 步骤 3：滑窗分帧推理
        var frameOutputs = new List<float[]>();

        for (var start = 0; start + frameSize <= resampled.Length; start += hopSize)
        {
            var frame = resampled[start..(start + frameSize)];
            var chunkResult = InferenceSingleChunk(frame);
            if (chunkResult.IsSuccess)
            {
                frameOutputs.Add(chunkResult.Value!);
            }
        }

        if (frameOutputs.Count == 0)
        {
            return Result<float[]>.Failure("所有帧推理均失败");
        }

        // 步骤 5：帧级输出取均值，得到 768 维最终特征
        var aggregated = new float[MertOutputDimension];
        for (var d = 0; d < MertOutputDimension; d++)
        {
            float sum = 0;
            foreach (var frame in frameOutputs)
            {
                sum += frame[d];
            }
            aggregated[d] = sum / frameOutputs.Count;
        }

        return Result<float[]>.Success(aggregated);
    }

    /// <summary>
    /// 对单段音频执行 MERT ONNX 推理，返回 768 维特征向量。
    /// </summary>
    /// <param name="chunk">一段 PCM 采样数据（已重采样至 24kHz）</param>
    /// <returns>768 维特征向量</returns>
    /// <remarks>
    /// MERT 模型输入为原始波形，输出通常包含：
    /// - last_hidden_state: (1, seq_len, 768)，所有 token 的隐藏状态
    /// 取所有 token 的均值作为该段音频的表示。
    /// </remarks>
    private Result<float[]> InferenceSingleChunk(float[] chunk)
    {
        // 构造输入张量 (batch=1, seq_len)
        var inputTensor = new DenseTensor<float>(chunk, [1, chunk.Length]);

        // 从模型元数据获取输入名称
        var inputMetadata = _session!.InputMetadata.First();
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(inputMetadata.Key, inputTensor)
        };

        using var results = _session.Run(inputs);

        // MERT 输出通常是 (1, seq_len, 768) 的隐藏状态
        // 取输出张量并计算所有 token 的时间均值
        var outputTensor = results.First().AsTensor<float>();
        var shape = outputTensor.Dimensions.ToArray();

        if (shape.Length == 3 && shape[2] == MertOutputDimension)
        {
            // 形状 (1, seq_len, 768)：对所有 seq_len 个 token 取均值
            var seqLen = shape[1];
            var result = new float[MertOutputDimension];

            for (var d = 0; d < MertOutputDimension; d++)
            {
                float sum = 0;
                for (var t = 0; t < seqLen; t++)
                {
                    sum += outputTensor[0, t, d];
                }
                result[d] = sum / seqLen;
            }

            return Result<float[]>.Success(result);
        }

        if (shape.Length == 2 && shape[1] == MertOutputDimension)
        {
            // 形状 (1, 768)：已经是池化后的输出，直接返回
            var result = new float[MertOutputDimension];
            for (var d = 0; d < MertOutputDimension; d++)
            {
                result[d] = outputTensor[0, d];
            }
            return Result<float[]>.Success(result);
        }

        // 形状不匹配时的兜底处理：尝试取前 768 个元素
        var flatOutput = outputTensor.ToArray();
        if (flatOutput.Length >= MertOutputDimension)
        {
            var result = new float[MertOutputDimension];
            Array.Copy(flatOutput, result, MertOutputDimension);
            return Result<float[]>.Success(result);
        }

        return Result<float[]>.Failure($"MERT 输出形状不匹配: [{string.Join(",", shape)}]");
    }

    /// <summary>
    /// 线性插值重采样：将音频从原始采样率转换为目标采样率。
    /// </summary>
    /// <param name="samples">原始 PCM 采样数据</param>
    /// <param name="originSampleRate">原始采样率</param>
    /// <param name="targetSampleRate">目标采样率</param>
    /// <returns>重采样后的 PCM 数据</returns>
    /// <remarks>
    /// 使用线性插值进行重采样。虽然不如 sinc 插值精确，
    /// 但对于特征提取场景足够，且计算量小。
    /// 若原始采样率已等于目标采样率，直接返回原数据。
    /// </remarks>
    private static float[] Resample(float[] samples, int originSampleRate, int targetSampleRate)
    {
        if (originSampleRate == targetSampleRate)
        {
            return samples;
        }

        var ratio = (double)targetSampleRate / originSampleRate;
        var outputLength = (int)(samples.Length * ratio);
        var result = new float[outputLength];

        for (var i = 0; i < outputLength; i++)
        {
            // 计算在原始采样中的浮点位置
            var srcIndex = i / ratio;
            var srcIndexInt = (int)srcIndex;
            var fraction = srcIndex - srcIndexInt;

            // 线性插值：在相邻两个采样点之间按比例取值
            if (srcIndexInt + 1 < samples.Length)
            {
                result[i] = (float)(samples[srcIndexInt] * (1.0 - fraction)
                                    + samples[srcIndexInt + 1] * fraction);
            }
            else
            {
                result[i] = samples[Math.Min(srcIndexInt, samples.Length - 1)];
            }
        }

        return result;
    }
}
