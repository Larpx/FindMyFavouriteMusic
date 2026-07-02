using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Larpx.PersonalTools.FindMyFavouriteMusic.GUI.ViewModels;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.GUI;

/// <summary>
/// 根据传入的 ViewModel 实例查找并构建对应的 View 控件。
/// </summary>
/// <remarks>
/// ViewLocator 是 Avalonia DataTemplate 机制的核心：当 ContentControl 的 Content
/// 被设置为一个 ViewModel 实例时，Avalonia 会调用 ViewLocator.Match 判断是否匹配，
/// 匹配后调用 ViewLocator.Build 构建对应的 View 控件，并自动将 ViewModel 设置为 View 的 DataContext。
/// <para>
/// 类型查找策略：
/// 1. 先用 <see cref="Type.GetType(string)"/> 在当前程序集及系统程序集中查找；
/// 2. 若未命中，遍历当前 AppDomain 已加载的所有程序集查找（处理跨程序集引用场景）。
/// </para>
/// </remarks>
[RequiresUnreferencedCode(
    "Default implementation of ViewLocator involves reflection which may be trimmed away.",
    Url = "https://docs.avaloniaui.net/docs/concepts/view-locator")]
public class ViewLocator : IDataTemplate
{
    /// <summary>
    /// 根据 ViewModel 实例构建对应的 View 控件。
    /// </summary>
    /// <param name="param">ViewModel 实例</param>
    /// <returns>对应的 View 控件；若未找到则返回提示文本</returns>
    public Control? Build(object? param)
    {
        if (param is null)
            return null;

        var name = param.GetType().FullName!.Replace("ViewModel", "View", StringComparison.Ordinal);
        var type = FindType(name);

        if (type != null)
        {
            return (Control)Activator.CreateInstance(type)!;
        }

        return new TextBlock { Text = "Not Found: " + name };
    }

    /// <summary>
    /// 判断给定数据是否由本 ViewLocator 处理（所有 ViewModelBase 派生类）。
    /// </summary>
    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }

    /// <summary>
    /// 按全名查找类型：先查当前程序集，再遍历所有已加载程序集。
    /// </summary>
    /// <param name="fullName">类型的全限定名（含命名空间）</param>
    /// <returns>找到的类型；未找到返回 null</returns>
    private static Type? FindType(string fullName)
    {
        // 优先使用 Type.GetType：在当前程序集和系统程序集中查找
        var type = Type.GetType(fullName);
        if (type != null)
        {
            return type;
        }

        // 兜底：遍历已加载的所有程序集查找
        // 这处理 View 类型所在程序集与 ViewLocator 不在同一程序集，或类型加载顺序导致 GetType 返回 null 的场景
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            type = assembly.GetType(fullName);
            if (type != null)
            {
                return type;
            }
        }

        return null;
    }
}
