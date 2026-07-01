using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Larpx.PersonalTools.FindMyFavouriteMusic.GUI.Views;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.GUI.Services;

/// <summary>
/// 对话框服务实现，基于 Avalonia Window 的模态弹窗。
/// </summary>
/// <remarks>
/// 通过查找当前应用的主窗口作为弹窗的 Owner，确保弹窗居中显示在主窗口之上。
/// 所有 UI 操作通过 <see cref="Dispatcher.UIThread"/> 调度，保证线程安全。
/// </remarks>
public class DialogService : IDialogService
{
    /// <inheritdoc/>
    public async Task ShowInfoAsync(string title, string message)
    {
        await ShowDialogAsync(DialogKind.Info, title, message);
    }

    /// <inheritdoc/>
    public async Task ShowSuccessAsync(string title, string message)
    {
        await ShowDialogAsync(DialogKind.Success, title, message);
    }

    /// <inheritdoc/>
    public async Task ShowErrorAsync(string title, string message)
    {
        await ShowDialogAsync(DialogKind.Error, title, message);
    }

    /// <summary>
    /// 核心弹窗逻辑：在 UI 线程创建并显示对话框窗口。
    /// </summary>
    /// <param name="kind">弹窗类型</param>
    /// <param name="title">标题</param>
    /// <param name="message">消息</param>
    private static async Task ShowDialogAsync(DialogKind kind, string title, string message)
    {
        // 弹窗必须在 UI 线程创建和显示
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            var dialog = new DialogWindow();
            dialog.Initialize(kind, title, message);

            // 查找当前主窗口作为 Owner，使弹窗模态附着在主窗口上
            var owner = GetMainWindow();
            if (owner is not null)
            {
                await dialog.ShowDialog(owner);
            }
            else
            {
                // 无主窗口时直接显示（降级处理）
                dialog.Show();
            }
        });
    }

    /// <summary>获取当前应用的主窗口实例</summary>
    private static Window? GetMainWindow()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime
            is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.MainWindow;
        }
        return null;
    }
}
