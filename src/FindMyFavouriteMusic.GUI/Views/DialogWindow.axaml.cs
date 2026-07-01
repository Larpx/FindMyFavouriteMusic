using Avalonia.Controls;
using Avalonia.Media;
using CommunityToolkit.Mvvm.Input;
using Larpx.PersonalTools.FindMyFavouriteMusic.GUI.Services;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.GUI.Views;

/// <summary>
/// 反馈弹窗窗口，根据 <see cref="DialogKind"/> 显示不同色调和图标。
/// </summary>
public partial class DialogWindow : Window
{
    public DialogWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 初始化弹窗内容并绑定数据。
    /// </summary>
    /// <param name="kind">弹窗类型（Info/Success/Error）</param>
    /// <param name="title">标题</param>
    /// <param name="message">消息</param>
    public void Initialize(DialogKind kind, string title, string message)
    {
        // 根据弹窗类型选择色调和图标符号
        IBrush accentBrush;
        string icon;
        switch (kind)
        {
            case DialogKind.Success:
                accentBrush = Brushes.Green;
                icon = "✓";
                break;
            case DialogKind.Error:
                accentBrush = Brushes.Red;
                icon = "✕";
                break;
            default:
                accentBrush = new SolidColorBrush(Color.Parse("#00d4ff"));
                icon = "i";
                break;
        }

        AccentBrush = accentBrush;
        IconText = icon;
        Title = title;
        Message = message;

        // 绑定关闭命令到窗口的 Close 方法
        CloseCommand = new RelayCommand(() => Close());

        DataContext = this;
    }

    /// <summary>顶部色条与图标使用的强调色画刷</summary>
    public IBrush AccentBrush { get; private set; } = Brushes.Cyan;

    /// <summary>状态图标文本（✓ / ✕ / i）</summary>
    public string IconText { get; private set; } = "i";

    /// <summary>弹窗标题</summary>
    public new string Title { get; private set; } = string.Empty;

    /// <summary>弹窗消息内容</summary>
    public string Message { get; private set; } = string.Empty;

    /// <summary>关闭按钮命令</summary>
    public IRelayCommand CloseCommand { get; private set; } = null!;
}
