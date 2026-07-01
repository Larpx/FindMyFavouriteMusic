using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Larpx.PersonalTools.FindMyFavouriteMusic.GUI.Services;
using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Dtos;
using Larpx.PersonalTools.FindMyFavouriteMusic.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.GUI.ViewModels;

/// <summary>
/// 音乐库 ViewModel，负责音乐文件扫描、列表展示与喜欢标记等交互逻辑。
/// </summary>
/// <remarks>
/// 在 MVVM 模式中，ViewModel 充当 View（界面）与 Service（业务服务）之间的中介：
/// - 通过 <see cref="Songs"/> 等 <see cref="ObservablePropertyAttribute"/> 暴露的可观察集合供 View 数据绑定；
/// - 通过 <see cref="RelayCommandAttribute"/> 生成的命令（如 ScanDirectoryCommand）供 View 触发操作；
/// - 业务逻辑委托给 <see cref="IMusicLibraryService"/>，ViewModel 不直接访问数据库或文件系统业务实现。
/// <para>
/// 本类使用 CommunityToolkit.Mvvm 源生成器简化样板代码：
/// - 标注 <c>[ObservableProperty]</c> 的字段会自动生成同名 PascalCase 属性并实现 INotifyPropertyChanged；
/// - 标注 <c>[RelayCommand]</c> 的方法会自动生成对应的 ICommand（方法名去掉 Async 后缀加 "Command"）。
/// </para>
/// </remarks>
public partial class MusicLibraryViewModel : ViewModelBase
{
    // 业务服务：负责目录扫描、数据持久化等操作，通过 DI 注入
    private readonly IMusicLibraryService _libraryService;
    // 日志记录器：用于记录异常和关键操作，便于问题排查
    private readonly ILogger<MusicLibraryViewModel> _logger;
    // 对话框服务：用于向用户弹出操作反馈
    private readonly IDialogService _dialogService;

    /// <summary>
    /// 文件夹选择交互回调，由 View 层（Code-behind）在运行时设置。
    /// </summary>
    /// <remarks>
    /// 采用回调模式而非直接依赖 Avalonia 的 <c>StorageProvider</c>，是为了：
    /// - 保持 ViewModel 与 UI 框架解耦，便于单元测试与跨平台复用；
    /// - View 层负责具体的文件夹选择对话框交互，ViewModel 只关心返回的路径字符串。
    /// 返回 null 表示用户取消了选择。
    /// </remarks>
    public Func<Task<string?>>? FolderPicker { get; set; }

    /// <summary>
    /// 构造函数，通过依赖注入获取业务服务和日志组件。
    /// </summary>
    /// <param name="libraryService">音乐库业务服务</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="dialogService">对话框服务</param>
    public MusicLibraryViewModel(
        IMusicLibraryService libraryService,
        ILogger<MusicLibraryViewModel> logger,
        IDialogService dialogService)
    {
        _libraryService = libraryService;
        _logger = logger;
        _dialogService = dialogService;
    }

    /// <summary>
    /// 当前音乐列表，供 View 的 ListBox/DataGrid 等控件绑定。
    /// </summary>
    /// <remarks>
    /// 字段名带下划线前缀（_songs），源生成器会生成 public 属性 <c>Songs</c>。
    /// 使用 <see cref="ObservableCollection{T}"/> 以支持集合变更通知（增删时 UI 自动更新）；
    /// 整体替换集合（如重新扫描后）也会触发属性变更通知。
    /// </remarks>
    [ObservableProperty]
    private ObservableCollection<SongDto> _songs = [];

    /// <summary>
    /// 是否正在扫描目录，用于控制按钮禁用状态和防重入。
    /// </summary>
    [ObservableProperty]
    private bool _isScanning;

    /// <summary>
    /// 扫描进度（0-100），供 View 的进度条控件绑定。
    /// </summary>
    [ObservableProperty]
    private int _scanProgress;

    /// <summary>
    /// 状态消息，供 View 的状态栏文本绑定，向用户反馈当前操作结果。
    /// </summary>
    [ObservableProperty]
    private string _statusMessage = "就绪";

    /// <summary>
    /// 扫描目录命令（无参版本）：先弹出文件夹选择器，再触发实际扫描。
    /// </summary>
    /// <remarks>
    /// 源生成器会生成 <c>ScanDirectoryCommand</c> 供 View 的 Button 绑定。
    /// 此命令通常绑定到"扫描"按钮。
    /// </remarks>
    [RelayCommand]
    private async Task ScanDirectoryAsync()
    {
        // 防重入：扫描进行中直接返回，避免用户多次点击导致并发扫描
        if (IsScanning) return;

        if (FolderPicker is not null)
        {
            // 通过回调让 View 层弹出文件夹选择对话框
            var path = await FolderPicker();
            if (string.IsNullOrWhiteSpace(path))
            {
                // 用户取消选择，反馈状态而非报错
                StatusMessage = "已取消选择";
                return;
            }

            // 拿到路径后调用带参版本执行实际扫描
            await ScanDirectoryAsync(path);
        }
        else
        {
            // View 层未设置回调，提示用户而非抛异常，保证健壮性
            StatusMessage = "请选择音乐目录...";
        }
    }

    /// <summary>
    /// 扫描指定目录下的音乐文件并加载到列表。
    /// </summary>
    /// <remarks>
    /// 业务流程：
    /// 1. 设置 <see cref="IsScanning"/> 为 true 进入扫描状态（UI 据此禁用按钮、显示进度条）；
    /// 2. 通过 <see cref="Progress{T}"/> 接收服务层上报的进度并更新 <see cref="ScanProgress"/>；
    /// 3. 调用服务层执行扫描，根据返回的 Result 判断成功/失败并更新状态消息；
    /// 4. 无论成功失败，在 finally 中重置 <see cref="IsScanning"/>，确保状态可恢复。
    /// </remarks>
    /// <param name="directoryPath">要扫描的目录路径</param>
    public async Task ScanDirectoryAsync(string directoryPath)
    {
        // 防重入检查：避免与无参版本的扫描命令并发执行
        if (IsScanning) return;

        // 进入扫描状态：UI 据此禁用扫描按钮、显示进度条
        IsScanning = true;
        ScanProgress = 0;
        StatusMessage = "正在扫描...";

        try
        {
            // 使用 Progress<T> 接收服务层通过 IProgress<int> 上报的进度
            // Progress<T> 会自动切换到捕获上下文（UI 线程）执行回调，避免跨线程更新 UI
            var progress = new Progress<int>(p => ScanProgress = p);
            var result = await _libraryService.ScanDirectoryAsync(directoryPath, progress);

            if (result.IsSuccess)
            {
                // 整体替换集合以触发属性变更通知，UI 会重新渲染列表
                var songList = result.Value ?? [];
                Songs = new ObservableCollection<SongDto>(songList);
                StatusMessage = $"扫描完成，共 {songList.Count} 首歌曲";
                await _dialogService.ShowSuccessAsync("扫描完成", $"共扫描到 {songList.Count} 首歌曲");
            }
            else
            {
                // 服务层返回失败（如目录不存在），将错误信息反馈给用户
                StatusMessage = $"扫描失败: {result.Error}";
                await _dialogService.ShowErrorAsync("扫描失败", result.Error ?? "未知错误");
            }
        }
        catch (Exception ex)
        {
            // 捕获未预期异常并记录日志，避免应用崩溃
            _logger.LogError(ex, "扫描目录失败");
            StatusMessage = $"扫描出错: {ex.Message}";
            await _dialogService.ShowErrorAsync("扫描出错", ex.Message);
        }
        finally
        {
            // 无论成功失败都重置扫描状态，确保 UI 可再次操作
            IsScanning = false;
        }
    }

    /// <summary>
    /// 切换歌曲喜欢状态命令。
    /// </summary>
    /// <remarks>
    /// 采用"先服务后本地"的策略（非纯乐观更新）：
    /// 1. 计算新的喜欢状态（取反）；
    /// 2. 调用服务层持久化新状态；
    /// 3. 服务成功后更新本地集合并通过索引器赋值触发绑定刷新；
    /// 4. 失败则不修改本地状态，保证数据一致性。
    /// <para>
    /// 注意：直接修改 song.IsLiked 不会触发 SongDto 内部的属性变更通知（除非 SongDto 实现 INotifyPropertyChanged），
    /// 因此通过 <c>Songs[index] = song</c> 重新赋值触发 ObservableCollection 的替换通知来刷新 UI。
    /// </para>
    /// </remarks>
    /// <param name="song">要切换喜欢状态的歌曲</param>
    [RelayCommand]
    private async Task ToggleLikeAsync(SongDto song)
    {
        if (song is null) return;

        try
        {
            // 计算目标状态：当前喜欢的取消喜欢，反之亦然
            var newLikeStatus = !song.IsLiked;
            // 先调用服务持久化，确保数据一致性
            var result = await _libraryService.ToggleLikeAsync(song.Id, newLikeStatus);

            if (result.IsSuccess)
            {
                // 服务成功后再更新本地数据
                song.IsLiked = newLikeStatus;
                // 通过索引器重新赋值触发 ObservableCollection 的 NotifyCollectionChangedAction.Replace 通知，
                // 从而让 UI 重新渲染该项（如喜欢图标变化）
                var index = Songs.IndexOf(song);
                if (index >= 0)
                {
                    Songs[index] = song;
                }
                StatusMessage = newLikeStatus ? $"已喜欢: {song.Title}" : $"已取消喜欢: {song.Title}";
            }
            else
            {
                // 服务失败时不修改本地状态，避免 UI 与持久化数据不一致
                StatusMessage = $"操作失败: {result.Error}";
                await _dialogService.ShowErrorAsync("操作失败", result.Error ?? "未知错误");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "切换喜欢状态失败: {SongId}", song.Id);
            StatusMessage = $"操作出错: {ex.Message}";
            await _dialogService.ShowErrorAsync("操作出错", ex.Message);
        }
    }

    /// <summary>
    /// 加载所有已存储的歌曲命令（从数据库/存储中读取，不重新扫描文件系统）。
    /// </summary>
    /// <remarks>
    /// 通常在页面初始化或用户主动刷新时调用，用于恢复历史扫描结果。
    /// </remarks>
    [RelayCommand]
    private async Task LoadAllSongsAsync()
    {
        try
        {
            var result = await _libraryService.GetAllSongsAsync();
            if (result.IsSuccess)
            {
                // 整体替换集合以触发 UI 更新
                Songs = new ObservableCollection<SongDto>(result.Value ?? []);
                StatusMessage = $"已加载 {(result.Value ?? []).Count} 首歌曲";
            }
            else
            {
                StatusMessage = $"加载失败: {result.Error}";
                await _dialogService.ShowErrorAsync("加载失败", result.Error ?? "未知错误");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载歌曲列表失败");
            StatusMessage = $"加载出错: {ex.Message}";
            await _dialogService.ShowErrorAsync("加载出错", ex.Message);
        }
    }
}
