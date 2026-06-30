using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using FindMyFavouriteMusic.Core.Audio;
using FindMyFavouriteMusic.Core.Configuration;
using FindMyFavouriteMusic.Core.Features;
using FindMyFavouriteMusic.Core.Interfaces;
using FindMyFavouriteMusic.Core.Prediction;
using FindMyFavouriteMusic.GUI.ViewModels;
using FindMyFavouriteMusic.GUI.Views;
using FindMyFavouriteMusic.Services;
using FindMyFavouriteMusic.Services.Database;
using FindMyFavouriteMusic.Services.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FindMyFavouriteMusic.GUI;

public partial class App : Application
{
    private IHost? _host;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // 移除 Avalonia 的数据验证插件，避免与 CommunityToolkit.Mvvm 冲突
        DataValidators.RemoveAt(0);

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

    private static IHost CreateHost()
    {
        return Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration((context, config) =>
            {
                config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
                config.AddEnvironmentVariables("FINDMYFAVOURITEMUSIC_");
            })
            .ConfigureServices((context, services) =>
            {
                // 配置
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

                // Core 层
                services.AddSingleton<IAudioDecoder, AudioDecoder>();
                services.AddSingleton<IAcousticFeatureExtractor, AcousticFeatureExtractor>();
                services.AddSingleton<IDeepFeatureExtractor, DeepFeatureExtractor>();
                services.AddSingleton<IFeatureAggregator, FeatureAggregator>();
                services.AddSingleton<ISimilarityCalculator, CosineSimilarityCalculator>();
                services.AddSingleton<IVectorSerializer, VectorSerializer>();
                services.AddSingleton<PredictionEngine>();

                // Data 层
                services.AddSingleton<DatabaseInitializer>();
                services.AddSingleton<SongRepository>();
                services.AddSingleton<ProfileRepository>();

                // Services 层
                services.AddSingleton<ISongRepository, SongRepository>();
                services.AddSingleton<IProfileService, ProfileService>();
                services.AddSingleton<IPredictionService, PredictionService>();
                services.AddSingleton<IMusicLibraryService, MusicLibraryService>();

                // Hosted Services
                services.AddHostedService(sp => sp.GetRequiredService<DatabaseInitializer>());

                // ViewModels
                services.AddTransient<MainWindowViewModel>();
                services.AddTransient<MusicLibraryViewModel>();
                services.AddTransient<PredictionViewModel>();
                services.AddTransient<SettingsViewModel>();
            })
            .Build();
    }

    private void OnExit(object? sender, EventArgs e)
    {
        _host?.Dispose();
    }
}
