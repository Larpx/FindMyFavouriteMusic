using System.Globalization;
using Avalonia.Data.Converters;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.GUI.Converters;

/// <summary>
/// 字符串相等比较转换器，用于 RadioButton 绑定枚举/字符串值。
/// ConverterParameter 为目标字符串，当绑定值与参数相等时返回 true。
/// </summary>
public class StringEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string s && parameter is string p)
        {
            return string.Equals(s, p, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // RadioButton 取消选中时不需要回传值，直接返回 parameter
        if (value is true && parameter is string p)
        {
            return p;
        }
        return Avalonia.AvaloniaProperty.UnsetValue;
    }
}
