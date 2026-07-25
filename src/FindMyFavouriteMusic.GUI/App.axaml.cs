using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Audio;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Configuration;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Features;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Hardware;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Interfaces;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Prediction;
using Larpx.PersonalTools.FindMyFavouriteMusic.GUI.Services;
using Larpx.PersonalTools.FindMyFavouriteMusic.GUI.ViewModels;
using Larpx.PersonalTools.FindMyFavouriteMusic.GUI.Views;
using Larpx.PersonalTools.FindMyFavouriteMusic.Services;
using Larpx.PersonalTools.FindMyFavouriteMusic.Services.Database;
using Larpx.PersonalTools.FindMyFavouriteMusic.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.GUI;

/// <summary>
/// 应用程序入口，负责构建依赖注入容器并启动主窗口。
/// </summary>
public partial class App : Application
{
    private IHost? _host;

    /// <summary>加载 XAML 资源</summary>
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <summary>框架初始化完成时构建 Host 并显示主窗口</summary>
    public override void OnFrameworkInitializationCompleted()
    {
        // 必须在任何 ONNX Runtime API 调用之前，根据配置复制对应 EP 的 native 库到输出根目录
        // v2.0 起仅保留 OpenVINO + CPU 双 EP 架构，两者共用同一份 OpenVINO native 库（含完整 CPU EP）
        InitializeEpNativeLib();

        _host = CreateHost();

        // 必须显式启动 Host，否则注册为 IHostedService 的服务（如 DatabaseInitializer）不会执行，
        // 将导致数据库表未创建，后续扫描目录时所有入库操作都会因 "no such table" 而失败，
        // 最终表现为"扫描完成，共 0 首歌曲"。
        _host.StartAsync().GetAwaiter().GetResult();

        // 启动时自动加载深度模型：若配置启用了深度特征且指定了模型路径，则自动加载模型
        // 这样用户首次配置好模型后，后续每次启动无需手动到设置页点击"加载模型"按钮
        // 加载失败不阻断启动，仅记录日志，用户仍可手动到设置页加载
        AutoLoadDeepModel(_host.Services);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainViewModel = _host.Services.GetRequiredService<MainWindowViewModel>();
            desktop.MainWindow = new MainWindow
            {
                DataContext = mainViewModel
            };

            desktop.Exit += OnExit;
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// 启动时自动加载深度学习模型。
    /// </summary>
    /// <remarks>
    /// 仅当配置中 EnableDeepFeatures=true 且对应模型路径非空且文件存在时才尝试加载。
    /// 加载为 CPU 密集型操作（可能数秒），此处同步执行以保证主窗口显示前模型已就绪，
    /// 避免用户在模型尚未加载完成时扫描歌曲导致深度特征缺失。
    /// 失败仅记录日志，不阻断应用启动。
    /// </remarks>
    /// <param name="serviceProvider">DI 容器，用于获取配置和提取器</param>
    private static void AutoLoadDeepModel(IServiceProvider serviceProvider)
    {
        try
        {
            var onnxOptions = serviceProvider.GetRequiredService<IOptionsMonitor<OnnxModelOptions>>().CurrentValue;
            if (!onnxOptions.EnableDeepFeatures)
            {
                return;
            }

            var deepExtractor = serviceProvider.GetRequiredService<IDeepFeatureExtractor>();
            var logger = serviceProvider.GetRequiredService<ILogger<App>>();

            // 根据模型类型选择路径，路径为空或文件不存在则跳过
            var modelPath = onnxOptions.ModelType == DeepModelType.MERT
                ? onnxOptions.MertModelPath
                : onnxOptions.VggishModelPath;

            if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
            {
                logger.LogWarning("自动加载模型跳过：模型路径无效或文件不存在: {Path}", modelPath);
                return;
            }

            logger.LogInformation("启动时自动加载深度模型: Type={Type}, Path={Path}", onnxOptions.ModelType, modelPath);
            var result = deepExtractor.LoadModel(modelPath, onnxOptions.ModelType);
            if (result.IsSuccess)
            {
                // 通过硬件加速器读取实际生效的 EP，告知用户当前推理设备
                var accelerator = serviceProvider.GetRequiredService<IHardwareAccelerator>();
                logger.LogInformation("深度模型自动加载成功: {Type}（{Dim} 维, EP={EP}）",
                    onnxOptions.ModelType, deepExtractor.FeatureDimension, accelerator.ActiveExecutionProvider);
            }
            else
            {
                logger.LogWarning("深度模型自动加载失败: {Error}", result.Error);
            }
        }
        catch (Exception ex)
        {
            // 任何异常都不应阻断应用启动，用户仍可手动加载
            var logger = serviceProvider.GetService<ILogger<App>>();
            logger?.LogError(ex, "自动加载深度模型时发生异常");
        }
    }

    /// <summary>
    /// 在任何 ONNX Runtime API 调用之前，把 OpenVINO native 库复制到输出根目录。
    /// </summary>
    /// <remarks>
    /// <para>v2.0 起仅保留 OpenVINO + CPU 双 EP 架构，两者共用同一份 OpenVINO native 库
    /// （包含完整 CPU EP）。启动时由 <see cref="EpNativeLoader"/> 把
    /// <c>ep-openvino/</c> 子目录的 native 库复制到输出根目录。</para>
    /// <para>必须在进程启动早期、任何 ORT P/Invoke 之前调用，否则已加载的 onnxruntime.dll 无法替换。</para>
    /// <para>读取配置与 <see cref="CreateHost"/> 相同的优先级：环境变量 > usersettings.json > appsettings.json。</para>
    /// </remarks>
    private static void InitializeEpNativeLib()
    {
        try
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                .AddJsonFile("usersettings.json", optional: true, reloadOnChange: false)
                .AddEnvironmentVariables("FINDMYFAVOURITEMUSIC_")
                .Build();

            var onnxConfig = new OnnxModelOptions();
            config.GetSection(OnnxModelOptions.SectionName).Bind(onnxConfig);

            EpNativeLoader.Initialize(AppContext.BaseDirectory, onnxConfig.ExecutionProvider);
        }
        catch (Exception ex)
        {
            // EP native 库初始化失败不阻断启动，后续 ORT 加载可能失败由调用方处理
            // 用 Console 输出而非 ILogger，因为此时 DI 容器尚未构建
            Console.Error.WriteLine($"[EpNativeLoader] 初始化失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 构建 Host：依次配置配置源、依赖注入服务。
    /// 配置优先级（高 → 低）：环境变量 > usersettings.json > appsettings.json
    /// </summary>
    private static IHost CreateHost()
    {
        return Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((context, config) =>
            {
                // 基础配置文件
                config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
                // 用户运行时配置文件（覆盖 appsettings.json 同名键）
                config.AddJsonFile("usersettings.json", optional: true, reloadOnChange: true);
                // 环境变量覆盖（前缀 FINDMYFAVOURITEMUSIC_）
                config.AddEnvironmentVariables("FINDMYFAVOURITEMUSIC_");
            })
            .ConfigureServices((context, services) =>
            {
                // 配置项绑定
                services.Configure<FeatureExtractionOptions>(
                    context.Configuration.GetSection(FeatureExtractionOptions.SectionName));
                services.Configure<PredictionOptions>(
                    context.Configuration.GetSection(PredictionOptions.SectionName));
                services.Configure<OnnxModelOptions>(
                    context.Configuration.GetSection(OnnxModelOptions.SectionName));
                services.Configure<DatabaseOptions>(
                    context.Configuration.GetSection(DatabaseOptions.SectionName));
                services.Configure<ScanOptions>(
                    context.Configuration.GetSection(ScanOptions.SectionName));

                // Core 层：音频解码、特征提取、相似度计算
                services.AddSingleton<IAudioDecoder, AudioDecoder>();
                services.AddSingleton<IAcousticFeatureExtractor, AcousticFeatureExtractor>();
                // 硬件加速器：单例，启动时检测 NPU，供提取器与设置页共享检测结果
                services.AddSingleton<IHardwareAccelerator, HardwareAccelerator>();
                services.AddSingleton<IDeepFeatureExtractor, DeepFeatureExtractorFactory>();
                services.AddSingleton<IFeatureAggregator, FeatureAggregator>();
                services.AddSingleton<ISimilarityCalculator, CosineSimilarityCalculator>();
                services.AddSingleton<IVectorSerializer, VectorSerializer>();
                services.AddSingleton<PredictionEngine>();

                // Data 层：SQLite 仓储
                services.AddSingleton<DatabaseInitializer>();
                services.AddSingleton<SongRepository>();
                services.AddSingleton<ProfileRepository>();

                // Services 层：业务编排
                services.AddSingleton<ISongRepository, SongRepository>();
                services.AddSingleton<IProfileService, ProfileService>();
                services.AddSingleton<IPredictionService, PredictionService>();
                services.AddSingleton<IMusicLibraryService, MusicLibraryService>();
                services.AddSingleton<IUserSettingsService, UserSettingsService>();

                // Hosted Services：数据库初始化
                services.AddHostedService(sp => sp.GetRequiredService<DatabaseInitializer>());

                // ViewModels
                services.AddTransient<MainWindowViewModel>();
                services.AddTransient<MusicLibraryViewModel>();
                services.AddTransient<PredictionViewModel>();
                services.AddTransient<SettingsViewModel>();

                // GUI 服务
                services.AddSingleton<IDialogService, DialogService>();
            })
            .Build();
    }

    /// <summary>应用退出时停止 HostedService 并释放 Host 资源</summary>
    private void OnExit(object? sender, EventArgs e)
    {
        // 与 StartAsync 对应，触发 IHostedService.StopAsync 完成优雅关闭
        _host?.StopAsync().GetAwaiter().GetResult();
        _host?.Dispose();
    }
}
