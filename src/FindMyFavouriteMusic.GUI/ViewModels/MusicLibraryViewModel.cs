using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Larpx.PersonalTools.FindMyFavouriteMusic.GUI.Services;
using Larpx.PersonalTools.FindMyFavouriteMusic.Models.Dtos;
using Larpx.PersonalTools.FindMyFavouriteMusic.Services.Database;
using Larpx.PersonalTools.FindMyFavouriteMusic.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
/// 自动化流程（简化用户操作）：
/// 1. 构造时立即从数据库加载已入库歌曲，让用户启动即可见列表；
/// 2. 若配置中存在上次扫描目录，构造后异步触发后台重新扫描，自动补全新增歌曲与深度特征；
/// 3. 用户扫描完成后自动保存扫描路径，下次启动无需重新选择目录；
/// 4. 用户切换喜欢状态后自动后台重建画像，无需用户手动操作。
/// </para>
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
    // 用户设置服务：持久化扫描路径等运行时可变配置
    private readonly IUserSettingsService _userSettingsService;
    // 画像服务：用于在用户切换喜欢后自动后台重建画像
    private readonly IProfileService _profileService;
    // 扫描配置（IOptionsMonitor 支持运行时热更新，usersettings.json 修改后会自动同步）
    private readonly IOptionsMonitor<ScanOptions> _scanOptions;

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
    /// 构造函数，通过依赖注入获取业务服务和日志组件，并触发自动加载流程。
    /// </summary>
    /// <param name="libraryService">音乐库业务服务</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="dialogService">对话框服务</param>
    /// <param name="userSettingsService">用户设置服务，用于持久化扫描路径</param>
    /// <param name="profileService">画像服务，用于自动重建画像</param>
    /// <param name="scanOptions">扫描配置监视，读取 LastScanDirectory</param>
    public MusicLibraryViewModel(
        IMusicLibraryService libraryService,
        ILogger<MusicLibraryViewModel> logger,
        IDialogService dialogService,
        IUserSettingsService userSettingsService,
        IProfileService profileService,
        IOptionsMonitor<ScanOptions> scanOptions)
    {
        _libraryService = libraryService;
        _logger = logger;
        _dialogService = dialogService;
        _userSettingsService = userSettingsService;
        _profileService = profileService;
        _scanOptions = scanOptions;

        // 启动时立即从数据库加载已入库歌曲，让用户启动即可见列表（无 IO 阻塞感）
        // 不 await：构造函数不应阻塞，加载通过后台任务完成，UI 通过数据绑定自动刷新
        _ = InitializeAsync();
    }

    /// <summary>
    /// 启动初始化流程：先加载已入库歌曲立即可见，再后台异步重新扫描补全数据。
    /// </summary>
    /// <remarks>
    /// 两阶段加载策略：
    /// 1. 第一阶段从数据库加载已有歌曲（毫秒级），用户立即看到列表可操作；
    /// 2. 第二阶段若有上次扫描目录，后台触发重新扫描，补全新增文件与缺失的深度特征向量，
    ///    扫描完成后整体刷新列表。整个过程不弹对话框，不打扰用户。
    /// </remarks>
    private async Task InitializeAsync()
    {
        try
        {
            // 第一阶段：加载已入库歌曲
            await LoadAllSongsAsync();

            // 第二阶段：若有上次扫描目录，后台异步重新扫描（不弹对话框，不打扰用户）
            var lastDir = _scanOptions.CurrentValue.LastScanDirectory;
            if (!string.IsNullOrWhiteSpace(lastDir) && Directory.Exists(lastDir))
            {
                _logger.LogInformation("检测到上次扫描目录，启动后台自动扫描: {Directory}", lastDir);
                await ScanDirectoryAsync(lastDir, autoMode: true);
            }
        }
        catch (Exception ex)
        {
            // 初始化失败不影响程序启动，记录日志即可
            _logger.LogError(ex, "启动初始化失败");
        }
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
    /// 4. 扫描成功后自动保存扫描路径到用户配置，下次启动无需重新选择；
    /// 5. 无论成功失败，在 finally 中重置 <see cref="IsScanning"/>，确保状态可恢复。
    /// <para>
    /// 自动模式（<paramref name="autoMode"/> = true）用于启动时后台重新扫描，
    /// 不弹对话框、不显示错误弹窗，仅更新状态消息和列表，避免打扰用户。
    /// </para>
    /// </remarks>
    /// <param name="directoryPath">要扫描的目录路径</param>
    /// <param name="autoMode">是否为自动模式（启动时后台扫描，不弹对话框）</param>
    public async Task ScanDirectoryAsync(string directoryPath, bool autoMode = false)
    {
        // 防重入检查：避免与无参版本的扫描命令并发执行
        if (IsScanning) return;

        // 进入扫描状态：UI 据此禁用扫描按钮、显示进度条
        IsScanning = true;
        ScanProgress = 0;
        StatusMessage = autoMode ? "后台扫描中..." : "正在扫描...";

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
                // 先清空再逐项添加，确保 ObservableCollection 触发 NotifyCollectionChanged 通知
                // 直接整体替换在某些场景下可能不会让 DataGrid 立即刷新行集合
                Songs.Clear();
                foreach (var song in songList)
                {
                    Songs.Add(song);
                }
                _logger.LogInformation("扫描完成，已填充 Songs 集合，Count={Count}", Songs.Count);
                StatusMessage = $"扫描完成，共 {songList.Count} 首歌曲";

                // 扫描成功后持久化扫描路径，下次启动自动重新扫描
                // 自动模式下也保存，保证配置一致性
                _ = _userSettingsService.SaveLastScanDirectoryAsync(directoryPath);

                // 仅手动模式弹成功对话框，自动模式不打扰用户
                if (!autoMode)
                {
                    await _dialogService.ShowSuccessAsync("扫描完成", $"共扫描到 {songList.Count} 首歌曲");
                }
            }
            else
            {
                // 服务层返回失败（如目录不存在）
                StatusMessage = $"扫描失败: {result.Error}";
                // 自动模式下不弹错误对话框，避免启动时弹窗打扰用户
                if (!autoMode)
                {
                    await _dialogService.ShowErrorAsync("扫描失败", result.Error ?? "未知错误");
                }
            }
        }
        catch (Exception ex)
        {
            // 捕获未预期异常并记录日志，避免应用崩溃
            _logger.LogError(ex, "扫描目录失败");
            StatusMessage = $"扫描出错: {ex.Message}";
            if (!autoMode)
            {
                await _dialogService.ShowErrorAsync("扫描出错", ex.Message);
            }
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
    /// 2. 调用服务层持久化新状态（服务层会同步触发画像增量更新或全量重建）；
    /// 3. 服务成功后更新本地集合并通过索引器赋值触发绑定刷新；
    /// 4. 后台异步触发画像全量重建，保证深度均值等数据正确（不阻塞 UI，不弹对话框）；
    /// 5. 失败则不修改本地状态，保证数据一致性。
    /// <para>
    /// 自动重建画像说明：服务层的 ToggleLikeAsync 已会调用画像增量更新或重建，
    /// 但为应对历史数据不一致场景（如先无模型扫描后加载模型），此处再异步触发一次全量重建作为保障。
    /// 重建在后台执行，失败仅记录日志，不影响用户操作。
    /// </para>
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
            // 先调用服务持久化，确保数据一致性（服务层内部会触发画像增量更新/重建）
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

                // 后台异步重建画像：确保画像反映最新喜欢状态（含深度均值向量）
                // 不 await、不弹窗：失败仅记录日志，不干扰用户操作
                _ = RebuildProfileSilentlyAsync();
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
    /// 静默后台重建用户画像，不弹窗、不阻塞 UI。
    /// </summary>
    /// <remarks>
    /// 用于切换喜欢状态后自动刷新画像，确保后续预测使用最新的偏好数据。
    /// 失败仅记录日志，不影响用户操作流程。
    /// </remarks>
    private async Task RebuildProfileSilentlyAsync()
    {
        try
        {
            _logger.LogInformation("自动重建画像开始");
            var result = await _profileService.RebuildProfileAsync();
            if (result.IsSuccess)
            {
                _logger.LogInformation("自动重建画像成功");
            }
            else
            {
                _logger.LogWarning("自动重建画像失败: {Error}", result.Error);
            }
        }
        catch (Exception ex)
        {
            // 静默失败：画像重建异常不应影响用户标记喜欢的操作
            _logger.LogError(ex, "自动重建画像异常");
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
                // 清空后逐项添加，确保 ObservableCollection 触发集合变更通知
                var list = result.Value ?? [];
                Songs.Clear();
                foreach (var song in list)
                {
                    Songs.Add(song);
                }
                StatusMessage = $"已加载 {list.Count} 首歌曲";
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
