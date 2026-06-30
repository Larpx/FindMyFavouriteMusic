using FindMyFavouriteMusic.Core.Configuration;
using FindMyFavouriteMusic.Core.Interfaces;
using FindMyFavouriteMusic.Models.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace FindMyFavouriteMusic.Core.Features;

/// <summary>
/// 基于 ONNX Runtime 的深度特征提取器（VGGish）
/// 支持优雅降级：无模型时 IsModelLoaded 为 false
/// </summary>
public class DeepFeatureExtractor : IDeepFeatureExtractor
{
    private readonly OnnxModelOptions _options;
    private readonly ILogger<DeepFeatureExtractor> _logger;
    private InferenceSession? _session;

    /// <summary>VGGish 输出维度</summary>
    private const int VggishOutputDimension = 128;

    /// <summary>VGGish 输入帧长（秒）</summary>
    private const double FrameDurationSeconds = 0.96;

    /// <summary>梅尔频谱的 bins 数</summary>
    private const int MelBins = 64;

    /// <summary>每帧的频谱时间步数</summary>
    private const int SpectrogramTimeSteps = 96;

    public DeepFeatureExtractor(
        IOptions<OnnxModelOptions> options,
        ILogger<DeepFeatureExtractor> logger)
    {
        _options = options.Value;
        _logger = logger;

        // 如果配置中启用了深度特征且有路径，自动加载模型
        if (_options.EnableDeepFeatures && !string.IsNullOrEmpty(_options.VggishModelPath))
        {
            var result = LoadModel(_options.VggishModelPath);
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

    /// <inheritdoc/>
    public Result LoadModel(string modelPath)
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
            _session = null;
            _logger.LogError(ex, "ONNX 模型加载失败: {ModelPath}", modelPath);
            return Result.Failure(ex);
        }
    }

    /// <inheritdoc/>
    public async Task<Result<float[]>> ExtractAsync(float[] samples, int sampleRate, CancellationToken ct = default)
    {
        if (!IsModelLoaded)
        {
            return Result<float[]>.Failure("深度特征模型未加载");
        }

        try
        {
            var result = await Task.Run(() => ExtractInternal(samples, sampleRate), ct);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "深度特征提取失败");
            return Result<float[]>.Failure(ex);
        }
    }

    private Result<float[]> ExtractInternal(float[] samples, int sampleRate)
    {
        // VGGish 推理流水线：
        // 1. 分帧（0.96 秒窗口）
        // 2. 每帧计算 log-mel 频谱图 (96 x 64)
        // 3. 构造输入张量 (batch, 1, 96, 64)
        // 4. ONNX 推理
        // 5. 帧级输出取时间平均 -> (128,)

        var frameSize = (int)(FrameDurationSeconds * sampleRate);
        var hopSize = frameSize / 2; // 50% 重叠

        if (samples.Length < frameSize)
        {
            return Result<float[]>.Failure("音频数据太短，无法提取深度特征");
        }

        var frameOutputs = new List<float[]>();

        for (var start = 0; start + frameSize <= samples.Length; start += hopSize)
        {
            var frame = samples[start..(start + frameSize)];
            var melSpectrogram = ComputeLogMelSpectrogram(frame, sampleRate);

            // 构造输入张量 (1, 1, 96, 64)
            var inputTensor = new DenseTensor<float>(melSpectrogram, [1, 1, SpectrogramTimeSteps, MelBins]);
            var inputMetadata = _session!.InputMetadata.First();
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor(inputMetadata.Key, inputTensor)
            };

            using var results = _session.Run(inputs);
            var outputTensor = results.First().AsTensor<float>();
            var outputVector = outputTensor.ToArray();
            frameOutputs.Add(outputVector);
        }

        // 帧级输出取均值
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
    /// 计算简化的 log-mel 频谱图，作为 VGGish 输入
    /// </summary>
    private static float[] ComputeLogMelSpectrogram(float[] frame, int sampleRate)
    {
        var melSpectrogram = new float[SpectrogramTimeSteps * MelBins];

        // 简化实现：对每段计算能量并映射到 mel bins
        var segmentSize = frame.Length / SpectrogramTimeSteps;

        for (var t = 0; t < SpectrogramTimeSteps; t++)
        {
            var segmentStart = t * segmentSize;
            var segmentEnd = Math.Min(segmentStart + segmentSize, frame.Length);

            for (var mel = 0; mel < MelBins; mel++)
            {
                var freqLow = MelToHz(mel * 4000.0 / MelBins);
                var freqHigh = MelToHz((mel + 1) * 4000.0 / MelBins);
                var binLow = (int)(freqLow / sampleRate * segmentSize);
                var binHigh = (int)(freqHigh / sampleRate * segmentSize);

                float energy = 0;
                for (var b = Math.Max(0, binLow); b < Math.Min(segmentSize, binHigh); b++)
                {
                    var idx = segmentStart + b;
                    if (idx < frame.Length)
                    {
                        energy += frame[idx] * frame[idx];
                    }
                }

                // log-mel 能量
                melSpectrogram[t * MelBins + mel] = Math.Max(0, (float)Math.Log10(energy + 1e-10));
            }
        }

        return melSpectrogram;
    }

    private static double MelToHz(double mel) => 700 * (Math.Exp(mel / 1127.0) - 1);
}
