namespace Larpx.PersonalTools.FindMyFavouriteMusic.GUI.Services;

/// <summary>
/// 对话框服务接口，提供模态弹窗反馈。
/// </summary>
/// <remarks>
/// 在 ViewModel 中通过 DI 注入，用于在关键操作完成后向用户弹出反馈对话框。
/// 接口定义在 GUI 层，因为对话框是 UI 关注点；ViewModel 同属 GUI 层，可直接引用。
/// </remarks>
public interface IDialogService
{
    /// <summary>显示信息提示弹窗（电光青色调）</summary>
    /// <param name="title">弹窗标题</param>
    /// <param name="message">弹窗消息内容</param>
    Task ShowInfoAsync(string title, string message);

    /// <summary>显示成功反馈弹窗（绿色调）</summary>
    /// <param name="title">弹窗标题</param>
    /// <param name="message">弹窗消息内容</param>
    Task ShowSuccessAsync(string title, string message);

    /// <summary>显示错误反馈弹窗（红色调）</summary>
    /// <param name="title">弹窗标题</param>
    /// <param name="message">弹窗消息内容</param>
    Task ShowErrorAsync(string title, string message);
}
