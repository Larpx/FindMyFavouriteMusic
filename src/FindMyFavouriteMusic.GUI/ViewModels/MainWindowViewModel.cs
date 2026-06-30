using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Dtos;
using Larpx.PersonalTools.FindMyFavouriteMusic.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.GUI.ViewModels;

/// <summary>
/// 主窗口 ViewModel，管理页面导航与子 ViewModel 生命周期。
/// </summary>
/// <remarks>
/// 导航模式说明：
/// - <see cref="CurrentPage"/> 持有当前活跃的 ViewModel 实例，View 层通过 <c>ContentControl</c> 绑定显示；
/// - View 层使用 <c>DataTemplate</c> 将不同 ViewModel 类型映射到对应的 View 控件，
///   切换 <see cref="CurrentPage"/> 即可自动切换显示的页面内容；
/// - 三个子 ViewModel（音乐库/预测/设置）通过依赖注入构造，本类持有它们的引用以便切换。
/// <para>
/// 默认页为 <see cref="MusicLibraryViewModel"/>（在构造函数中设置 <c>_currentPage = MusicLibraryViewModel</c>）。
/// </para>
/// </remarks>
public partial class MainWindowViewModel : ViewModelBase
{
    private readonly ILogger<MainWindowViewModel> _logger;

    /// <summary>
    /// 构造函数，通过依赖注入获取三个子 ViewModel 和日志组件。
    /// </summary>
    /// <remarks>
    /// 三个子 ViewModel 由 DI 容器以单例或瞬时方式提供，本类持有引用用于导航切换。
    /// 默认导航到音乐库页（<see cref="MusicLibraryViewModel"/>）。
    /// </remarks>
    /// <param name="musicLibraryViewModel">音乐库页 ViewModel</param>
    /// <param name="predictionViewModel">预测页 ViewModel</param>
    /// <param name="settingsViewModel">设置页 ViewModel</param>
    /// <param name="logger">日志记录器</param>
    public MainWindowViewModel(
        MusicLibraryViewModel musicLibraryViewModel,
        PredictionViewModel predictionViewModel,
        SettingsViewModel settingsViewModel,
        ILogger<MainWindowViewModel> logger)
    {
        _logger = logger;
        // 持有三个子 ViewModel 引用，供导航切换使用
        MusicLibraryViewModel = musicLibraryViewModel;
        PredictionViewModel = predictionViewModel;
        SettingsViewModel = settingsViewModel;
        // 默认显示音乐库页
        _currentPage = MusicLibraryViewModel;
    }

    /// <summary>
    /// 音乐库页 ViewModel，可通过 <see cref="NavigateTo"/> 命令切换至此页。
    /// </summary>
    public MusicLibraryViewModel MusicLibraryViewModel { get; }

    /// <summary>
    /// 预测页 ViewModel，可通过 <see cref="NavigateTo"/> 命令切换至此页。
    /// </summary>
    public PredictionViewModel PredictionViewModel { get; }

    /// <summary>
    /// 设置页 ViewModel，可通过 <see cref="NavigateTo"/> 命令切换至此页。
    /// </summary>
    public SettingsViewModel SettingsViewModel { get; }

    /// <summary>
    /// 当前活跃的 ViewModel，供 View 的 ContentControl 绑定显示对应页面。
    /// </summary>
    /// <remarks>
    /// 字段名带下划线前缀（_currentPage），源生成器会生成 public 属性 <c>CurrentPage</c>。
    /// 切换此属性值会触发属性变更通知，View 的 ContentControl 会自动切换到对应 DataTemplate 渲染的页面。
    /// </remarks>
    [ObservableProperty]
    private ViewModelBase _currentPage;

    /// <summary>
    /// 导航命令：根据传入的页面标识字符串切换 <see cref="CurrentPage"/>。
    /// </summary>
    /// <remarks>
    /// View 层通过 Button 的 <c>CommandParameter</c> 传递字符串参数（如 "Library"/"Prediction"/"Settings"）。
    /// 源生成器会生成 <c>NavigateToCommand</c>，支持 CommandParameter 绑定。
    /// 未知标识默认回退到音乐库页，保证健壮性。
    /// </remarks>
    /// <param name="page">页面标识：Library / Prediction / Settings</param>
    [RelayCommand]
    private void NavigateTo(string page)
    {
        // 使用 switch 表达式将字符串标识映射到对应的子 ViewModel 实例
        CurrentPage = page switch
        {
            "Library" => MusicLibraryViewModel,
            "Prediction" => PredictionViewModel,
            "Settings" => SettingsViewModel,
            // 未知标识回退到默认页，避免导航失败导致空白
            _ => MusicLibraryViewModel
        };
    }
}
