using Avalonia.Controls;
using Larpx.PersonalTools.FindMyFavouriteMusic.GUI.ViewModels;
using System.ComponentModel;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.GUI.Views;

/// <summary>
/// 主窗口视图，管理导航按钮的视觉状态
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    /// <summary>订阅 ViewModel 属性变更以更新导航高亮</summary>
    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.PropertyChanged += OnViewModelPropertyChanged;
            // 初始化导航状态
            UpdateNavState(vm.CurrentPage);
        }
    }

    /// <summary>监听 CurrentPage 变更，更新导航按钮的激活样式</summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.CurrentPage))
        {
            if (sender is MainWindowViewModel vm)
            {
                UpdateNavState(vm.CurrentPage);
            }
        }
    }

    /// <summary>根据当前页面设置导航按钮的 active 伪类</summary>
    private void UpdateNavState(ViewModelBase currentPage)
    {
        NavLibrary.Classes.Set("active", currentPage is MusicLibraryViewModel);
        NavPrediction.Classes.Set("active", currentPage is PredictionViewModel);
        NavSettings.Classes.Set("active", currentPage is SettingsViewModel);
    }
}
