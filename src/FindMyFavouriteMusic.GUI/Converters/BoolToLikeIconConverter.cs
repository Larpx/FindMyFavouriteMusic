using System.Globalization;
using Avalonia.Data.Converters;

namespace FindMyFavouriteMusic.GUI.Converters;

/// <summary>
/// 布尔值转喜欢图标转换器
/// </summary>
public class BoolToLikeIconConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? "♥" : "♡";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is string s && s == "♥";
    }
}
