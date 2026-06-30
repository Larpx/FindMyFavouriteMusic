using CommunityToolkit.Mvvm.ComponentModel;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.GUI.ViewModels;

/// <summary>
/// 所有 ViewModel 的抽象基类，统一继承自 <see cref="ObservableObject"/>。
/// </summary>
/// <remarks>
/// <see cref="ObservableObject"/> 是 CommunityToolkit.Mvvm 提供的基类，
/// 实现了 <see cref="System.ComponentModel.INotifyPropertyChanged"/> 接口，
/// 为派生类提供属性变更通知能力（配合 <c>[ObservableProperty]</c> 源生成器使用）。
/// <para>
/// 即使当前为空，作为统一基类便于未来扩展公共属性与逻辑，例如：
/// - <c>IsBusy</c>：全局加载状态；
/// - <c>Title</c>：页面标题；
/// - 公共的错误处理或导航辅助方法。
/// </para>
/// 所有具体 ViewModel（如 <see cref="MusicLibraryViewModel"/>、<see cref="PredictionViewModel"/>）
/// 均派生自此基类，便于 <see cref="MainWindowViewModel"/> 以统一类型（<c>ViewModelBase</c>）管理导航。
/// </remarks>
public abstract class ViewModelBase : ObservableObject
{
}
