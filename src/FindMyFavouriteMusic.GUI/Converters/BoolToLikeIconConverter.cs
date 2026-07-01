using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.GUI.Converters;

/// <summary>
/// 布尔值转喜欢图标转换器，暗色科幻主题适配
/// </summary>
public class BoolToLikeIconConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // 激活态使用电光青色实心心形
        return value is true ? "\u2764" : "\u2661";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is string s && s == "\u2764";
    }
}
