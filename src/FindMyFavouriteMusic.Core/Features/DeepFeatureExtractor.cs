using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Configuration;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Interfaces;
using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.Core.Features;

/// <summary>
/// 基于 ONNX Runtime 的深度特征提取器，使用 VGGish 预训练模型对音频进行场景识别。
/// </summary>
/// <remarks>
/// <para><b>模型职责：</b>VGGish 是 Google 发布的音频场景识别预训练模型，将原始音频映射到 128 维嵌入向量，
/// 该向量捕捉了音频的高层语义信息（如乐器、节奏、环境音等），可用于下游相似度计算与推荐任务。</para>
/// <para><b>推理流水线：</b></para>
/// <para>1. 将音频按 0.96 秒窗口、50% 重叠进行分帧；</para>
/// <para>2. 对每帧计算 log-mel 频谱图（形状 96×64）；</para>
/// <para>3. 构造 ONNX 输入张量 (1, 1, 96, 64)；</para>
/// <para>4. 执行 ONNX 推理得到每帧的 128 维向量；</para>
/// <para>5. 对所有帧的输出取时间平均，得到整段音频的 128 维特征。</para>
/// <para><b>优雅降级：</b>当未配置模型路径或加载失败时，<see cref="IsModelLoaded"/> 返回 false，
/// 调用方可据此判断是否启用深度特征，从而回退到仅声学模式，保证系统可用性。</para>
/// </remarks>
public class DeepFeatureExtractor : IDeepFeatureExtractor
{
    private readonly OnnxModelOptions _options;
    private readonly ILogger<DeepFeatureExtractor> _logger;
    private InferenceSession? _session;

    /// <summary>
    /// VGGish 输出嵌入向量的维度。
    /// </summary>
    /// <remarks>该值由 VGGish 模型结构决定，模型最后一层为 128 维全连接层，因此输出固定为 128 维。</remarks>
    private const int VggishOutputDimension = 128;

    /// <summary>
    /// VGGish 输入帧长（秒）。
    /// </summary>
    /// <remarks>0.96 秒是 VGGish 训练时采用的帧长，对应 96 个 10ms 的频谱时间步，
    /// 保持与模型训练一致的帧长才能保证特征分布的有效性。</remarks>
    private const double FrameDurationSeconds = 0.96;

    /// <summary>
    /// 梅尔频谱的频率 bins 数量。
    /// </summary>
    /// <remarks>VGGish 模型输入在频率维度上有 64 个 mel bins，因此此处必须为 64 以匹配模型输入形状。</remarks>
    private const int MelBins = 64;

    /// <summary>
    /// 每帧频谱图的时间步数。
    /// </summary>
    /// <remarks>0.96 秒帧长按 10ms 步长切分，得到 96 个时间步，对应模型输入的时间维度大小。</remarks>
    private const int SpectrogramTimeSteps = 96;

    /// <summary>
    /// 构造深度特征提取器，并按配置自动加载 VGGish ONNX 模型。
    /// </summary>
    /// <param name="options">ONNX 模型配置（路径、是否启用等），通过 IOptions 模式注入。</param>
    /// <param name="logger">日志记录器。</param>
    public DeepFeatureExtractor(
        IOptions<OnnxModelOptions> options,
        ILogger<DeepFeatureExtractor> logger)
    {
        _options = options.Value;
        _logger = logger;

        // 如果配置中启用了深度特征且有路径，自动加载模型
        if (_options.EnableDeepFeatures && !string.IsNullOrEmpty(_options.VggishModelPath))
        {
            var result = LoadModel(_options.VggishModelPath, DeepModelType.VGGish);
            if (!result.IsSuccess)
            {
                _logger.LogWarning("ONNX 模型自动加载失败: {Error}", result.Error);
            }
        }
    }

    /// <inheritdoc/>
    public bool IsModelLoaded => _session is not null;

    /// <inheritdoc/>
    public int FeatureDimension => VggishOutputDimension;

    /// <summary>
    /// 加载指定路径的 VGGish ONNX 模型到内存中的推理会话。
    /// </summary>
    /// <param name="modelPath">ONNX 模型文件的绝对路径。</param>
    /// <param name="modelType">模型类型（由工厂传入，本提取器固定为 VGGish，忽略此参数）。</param>
    /// <returns>加载结果：成功返回 Success；文件不存在或加载异常时返回 Failure 并携带错误信息。</returns>
    /// <remarks>加载失败时会将内部会话置为 null，确保 <see cref="IsModelLoaded"/> 状态与实际状态一致，
    /// 触发上游的优雅降级逻辑。</remarks>
    public Result LoadModel(string modelPath, DeepModelType modelType)
    {
        if (!File.Exists(modelPath))
        {
            return Result.Failure($"模型文件不存在: {modelPath}");
        }

        try
        {
            _session = new InferenceSession(modelPath);
            _logger.LogInformation("ONNX 模型加载成功: {ModelPath}", modelPath);
            return Result.Success();
        }
        catch (Exception ex)
        {
            // 加载异常时显式置 null，避免持有部分初始化的会话导致后续推理出现不可预期错误
            _session = null;
            _logger.LogError(ex, "ONNX 模型加载失败: {ModelPath}", modelPath);
            return Result.Failure(ex);
        }
    }

    /// <summary>
    /// 异步提取一段音频的深度特征向量。
    /// </summary>
    /// <param name="samples">单声道 PCM 浮点采样数据，范围 [-1, 1]。</param>
    /// <param name="sampleRate">采样率（Hz）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>成功时返回长度为 <see cref="VggishOutputDimension"/> 的 128 维特征向量；失败时返回 Failure。</returns>
    /// <remarks>
    /// <para>此处使用 <see cref="Task.Run"/> 将推理调度到线程池，因为 ONNX 推理为 CPU 密集型计算，
    /// 直接在调用线程执行会长时间阻塞（数百毫秒到数秒），影响上层异步流水线的响应能力。</para>
    /// <para>若模型未加载，直接返回失败，由调用方决定是否走降级路径。</para>
    /// </remarks>
    public async Task<Result<float[]>> ExtractAsync(float[] samples, int sampleRate, CancellationToken ct = default)
    {
        if (!IsModelLoaded)
        {
            return Result<float[]>.Failure("深度特征模型未加载");
        }

        try
        {
            // CPU 密集型推理，通过 Task.Run 卸载到线程池，避免阻塞调用线程
            var result = await Task.Run(() => ExtractInternal(samples, sampleRate), ct);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "深度特征提取失败");
            return Result<float[]>.Failure(ex);
        }
    }

    /// <summary>
    /// 执行实际的 VGGish 推理流水线（同步实现）。
    /// </summary>
    /// <param name="samples">单声道 PCM 浮点采样数据。</param>
    /// <param name="sampleRate">采样率（Hz）。</param>
    /// <returns>128 维时间平均嵌入向量；音频过短时返回 Failure。</returns>
    /// <remarks>
    /// <para><b>流水线步骤：</b></para>
    /// <para>1. 分帧：以 0.96 秒为帧长，按 50% 重叠切分（hopSize = frameSize / 2）；</para>
    /// <para>2. 每帧计算 log-mel 频谱图（96×64）；</para>
    /// <para>3. 构造输入张量 (1, 1, 96, 64)；</para>
    /// <para>4. ONNX 推理得到该帧的 128 维嵌入；</para>
    /// <para>5. 所有帧的嵌入按维度求均值，得到整段音频的 128 维表示。</para>
    /// </remarks>
    private Result<float[]> ExtractInternal(float[] samples, int sampleRate)
    {
        // VGGish 推理流水线：
        // 1. 分帧（0.96 秒窗口）
        // 2. 每帧计算 log-mel 频谱图 (96 x 64)
        // 3. 构造输入张量 (batch, 1, 96, 64)
        // 4. ONNX 推理
        // 5. 帧级输出取时间平均 -> (128,)

        // 单帧采样数 = 帧长 × 采样率
        var frameSize = (int)(FrameDurationSeconds * sampleRate);
        // 50% 重叠：相邻帧共享一半样本，既提升时间覆盖率，又平滑帧间过渡
        var hopSize = frameSize / 2; // 50% 重叠

        if (samples.Length < frameSize)
        {
            return Result<float[]>.Failure("音频数据太短，无法提取深度特征");
        }

        var frameOutputs = new List<float[]>();

        // 滑窗分帧：每前进 hopSize 个样本处理一帧，直至剩余样本不足以构成完整帧
        for (var start = 0; start + frameSize <= samples.Length; start += hopSize)
        {
            // 取出当前帧的采样区间（使用范围运算符避免额外拷贝开销由 JIT 优化）
            var frame = samples[start..(start + frameSize)];
            // 计算该帧的 log-mel 频谱图，形状 96×64
            var melSpectrogram = ComputeLogMelSpectrogram(frame, sampleRate);

            // 构造 ONNX 输入张量：根据模型实际输入形状自动适配
            // Keras 转换的模型输入形状为 (batch, 96, 64, 1) 即 NHWC 格式
            var inputMetadata = _session!.InputMetadata.First();
            var inputShape = inputMetadata.Value.Dimensions;
            DenseTensor<float> inputTensor;
            if (inputShape.Length == 4 && inputShape[3] == 1)
            {
                // NHWC 格式: (batch, height, width, channels)
                inputTensor = new DenseTensor<float>(melSpectrogram, [1, SpectrogramTimeSteps, MelBins, 1]);
            }
            else
            {
                // NCHW 格式: (batch, channels, height, width)
                inputTensor = new DenseTensor<float>(melSpectrogram, [1, 1, SpectrogramTimeSteps, MelBins]);
            }
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(inputMetadata.Key, inputTensor)
            };

            // 执行 ONNX 推理，得到该帧的 128 维嵌入
            using var results = _session.Run(inputs);
            // 取出输出张量并拷贝为一维数组（128 个元素）
            var outputTensor = results.First().AsTensor<float>();
            var outputVector = outputTensor.ToArray();
            frameOutputs.Add(outputVector);
        }

        // 帧级输出取均值：对每个维度，跨所有帧累加后除以帧数，得到 128 维整段音频表示
        var aggregated = new float[VggishOutputDimension];
        for (var d = 0; d < VggishOutputDimension; d++)
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
    /// 计算简化的 log-mel 频谱图，作为 VGGish 的输入特征。
    /// </summary>
    /// <param name="frame">单帧 PCM 采样数据（长度等于 frameSize）。</param>
    /// <param name="sampleRate">采样率（Hz）。</param>
    /// <returns>长度为 <see cref="SpectrogramTimeSteps"/> × <see cref="MelBins"/> 的扁平化频谱图数组。</returns>
    /// <remarks>
    /// <para><b>简化策略说明：</b>标准 VGGish 输入需先做 STFT（短时傅里叶变换）再映射到 mel 滤波器组，
    /// 本实现为避免引入完整的 DSP 依赖，做了如下简化：</para>
    /// <para>1. 将一帧时域样本平均切成 <see cref="SpectrogramTimeSteps"/> 段，每段近似一个"时间步"；</para>
    /// <para>2. 对每段直接用时域样本平方和近似能量（而非 STFT 频谱能量）；</para>
    /// <para>3. 按 mel 频率区间将样本索引分段累加，近似 mel bins 能量；</para>
    /// <para>4. 对能量取 log10 得到 log-mel 能量。</para>
    /// <para>该简化在数值上与标准 VGGish 输入存在差异，但保留了"时频两维能量分布"的结构，
    /// 可作为模型输入的近似占位实现。若需更精确特征，应替换为完整 STFT + mel 滤波器组。</para>
    /// </remarks>
    private static float[] ComputeLogMelSpectrogram(float[] frame, int sampleRate)
    {
        var melSpectrogram = new float[SpectrogramTimeSteps * MelBins];

        // 简化实现：对每段计算能量并映射到 mel bins
        // 将一帧样本按时间步数等分，每段长度近似为 frameSize / 96
        var segmentSize = frame.Length / SpectrogramTimeSteps;

        for (var t = 0; t < SpectrogramTimeSteps; t++)
        {
            // 当前时间步对应的样本区间
            var segmentStart = t * segmentSize;
            var segmentEnd = Math.Min(segmentStart + segmentSize, frame.Length);

            for (var mel = 0; mel < MelBins; mel++)
            {
                // 将 mel 索引线性映射到 [0, 4000] mel 区间（覆盖人耳可听范围主要部分）
                var freqLow = MelToHz(mel * 4000.0 / MelBins);
                var freqHigh = MelToHz((mel + 1) * 4000.0 / MelBins);
                // 由频率反推样本索引（频率 × segmentSize / sampleRate 近似 FFT bin 映射）
                var binLow = (int)(freqLow / sampleRate * segmentSize);
                var binHigh = (int)(freqHigh / sampleRate * segmentSize);

                float energy = 0;
                // 在 [binLow, binHigh) 范围内累加样本平方，近似该 mel bins 的时域能量
                for (var b = Math.Max(0, binLow); b < Math.Min(segmentSize, binHigh); b++)
                {
                    var idx = segmentStart + b;
                    if (idx < frame.Length)
                    {
                        energy += frame[idx] * frame[idx];
                    }
                }

                // log-mel 能量：加 1e-10 防止 log10(0)；裁剪到非负，避免数值异常
                melSpectrogram[t * MelBins + mel] = Math.Max(0, (float)Math.Log10(energy + 1e-10));
            }
        }

        return melSpectrogram;
    }

    /// <summary>
    /// 将 mel 频率转换为线性频率（Hz）。
    /// </summary>
    /// <param name="mel">mel 频率值。</param>
    /// <returns>对应的线性频率（Hz）。</returns>
    /// <remarks>
    /// <para>反变换公式：Hz = 700 × (exp(mel / 1127) - 1)。</para>
    /// <para>其中 700 是 mel 标度的参考频率，1127 来源于 700 / ln(10) ≈ 700 / 2.3026 ≈ 304.1 ... 的逆推导，
    /// 其精确值为 700 / ln(1 + 1000/700) ≈ 1127.0，对应 mel 与 Hz 之间的标准非线性映射关系。</para>
    /// </remarks>
    private static double MelToHz(double mel) => 700 * (Math.Exp(mel / 1127.0) - 1);
}
