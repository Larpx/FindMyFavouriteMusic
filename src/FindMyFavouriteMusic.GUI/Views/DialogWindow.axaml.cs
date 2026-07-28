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

    public void Initialize(DialogKind kind, string title, string message)
    {
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
            case DialogKind.Confirm:
                accentBrush = new SolidColorBrush(Color.Parse("#f0a020"));
                icon = "?";
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
        ShowCancelButton = kind == DialogKind.Confirm;
        PrimaryButtonText = kind == DialogKind.Confirm ? "是" : "确定";

        CloseCommand = new RelayCommand(() => Close(kind == DialogKind.Confirm ? true : null));
        CancelCommand = new RelayCommand(() => Close(false));

        DataContext = this;
    }

    public IBrush AccentBrush { get; private set; } = Brushes.Cyan;
    public string IconText { get; private set; } = "i";
    public new string Title { get; private set; } = string.Empty;
    public string Message { get; private set; } = string.Empty;
    public bool ShowCancelButton { get; private set; }
    public string PrimaryButtonText { get; private set; } = "确定";
    public IRelayCommand CloseCommand { get; private set; } = null!;
    public IRelayCommand CancelCommand { get; private set; } = null!;
}
