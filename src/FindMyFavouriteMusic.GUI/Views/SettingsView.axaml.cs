using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Larpx.PersonalTools.FindMyFavouriteMusic.GUI.ViewModels;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.GUI.Views;

/// <summary>
/// 设置视图，处理模型文件拖拽交互。
/// <para>支持将 .onnx 模型文件拖拽到模型卡片区域，自动识别模型类型并填充路径。</para>
/// </summary>
public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    /// <summary>模型卡片区域拖拽进入：高亮提示</summary>
    private void OnModelDragEnter(object? sender, DragEventArgs e)
    {
        if (!e.DataTransfer.Contains(DataFormat.File)) return;
        e.DragEffects = DragDropEffects.Copy;
        SetDropHighlight(ModelDropZone, true);
    }

    /// <summary>模型卡片区域拖拽离开：恢复外观</summary>
    private void OnModelDragLeave(object? sender, DragEventArgs e)
    {
        SetDropHighlight(ModelDropZone, false);
    }

    /// <summary>模型卡片区域放下文件：自动识别模型类型并填充路径</summary>
    private void OnModelDrop(object? sender, DragEventArgs e)
    {
        SetDropHighlight(ModelDropZone, false);

        if (DataContext is not SettingsViewModel vm) return;

        var path = ExtractDroppedFilePath(e);
        if (path is null) return;

        // 仅接受 .onnx 文件
        if (!path.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase))
        {
            vm.StatusMessage = "请拖入 ONNX 模型文件（.onnx）";
            return;
        }

        // 根据文件名自动识别模型类型：文件名含 "mert" → MERT，否则 → VGGish
        var modelType = DetectModelType(path);
        vm.SelectedModelType = modelType;

        if (modelType == "MERT")
        {
            vm.MertModelPath = path;
        }
        else
        {
            vm.VggishModelPath = path;
        }

        vm.EnableDeepFeatures = true;
        e.DragEffects = DragDropEffects.Copy;
    }

    /// <summary>从拖拽事件中提取第一个文件的本地路径</summary>
    private static string? ExtractDroppedFilePath(DragEventArgs e)
    {
        if (!e.DataTransfer.Contains(DataFormat.File)) return null;

        var files = e.DataTransfer.TryGetFiles();
        var file = files?.FirstOrDefault();
        return file?.TryGetLocalPath();
    }

    /// <summary>
    /// 根据文件名自动识别模型类型。
    /// </summary>
    /// <remarks>
    /// <para>文件名包含 "mert"（不区分大小写）识别为 MERT，否则识别为 VGGish。</para>
    /// <para><b>兜底选 VGGish 而非 MERT 的原因：</b>这是文件名启发式识别，不是默认配置。
    /// MERT 模型文件名通常含 "mert" 关键字（如 MERT-v1-95M.onnx），不含该关键字的
    /// 模型文件更可能是 VGGish 或其他通用音频模型。这与 <see cref="ViewModels.SettingsViewModel"/>
    /// 中 <c>ParseModelType</c> 的 MERT 兜底不同——后者处理的是无效输入兜底，应与 v2.0 默认配置一致。</para>
    /// </remarks>
    private static string DetectModelType(string path)
    {
        var fileName = Path.GetFileName(path);
        return fileName.Contains("mert", StringComparison.OrdinalIgnoreCase) ? "MERT" : "VGGish";
    }

    /// <summary>设置拖拽高亮/恢复外观</summary>
    private static void SetDropHighlight(Border? border, bool highlight)
    {
        if (border is null) return;

        if (highlight)
        {
            border.BorderBrush = Brushes.Cyan;
            border.BorderThickness = new Thickness(2);
        }
        else
        {
            border.ClearValue(Border.BorderBrushProperty);
            border.ClearValue(Border.BorderThicknessProperty);
        }
    }
}
