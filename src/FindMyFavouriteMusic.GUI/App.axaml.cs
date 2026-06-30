using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Audio;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Configuration;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Features;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Interfaces;
using Larpx.PersonalTools.FindMyFavouriteMusic.Core.Prediction;
using Larpx.PersonalTools.FindMyFavouriteMusic.GUI.ViewModels;
using Larpx.PersonalTools.FindMyFavouriteMusic.GUI.Views;
using Larpx.PersonalTools.FindMyFavouriteMusic.Services;
using Larpx.PersonalTools.FindMyFavouriteMusic.Services.Database;
using Larpx.PersonalTools.FindMyFavouriteMusic.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
        _host = CreateHost();

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
                services.AddSingleton<IDeepFeatureExtractor, DeepFeatureExtractor>();
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
            })
            .Build();
    }

    /// <summary>应用退出时释放 Host 资源</summary>
    private void OnExit(object? sender, EventArgs e)
    {
        _host?.Dispose();
    }
}
