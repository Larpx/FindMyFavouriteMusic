using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace FindMyFavouriteMusic.GUI.Converters;

/// <summary>
/// 分数转颜色转换器，0-30 红色，30-70 黄色，70-100 绿色
/// </summary>
public class ScoreToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var score = value as double? ?? 0;

        return score switch
        {
            >= 70 => new SolidColorBrush(Color.Parse("#10b981")),
            >= 30 => new SolidColorBrush(Color.Parse("#f59e0b")),
            _ => new SolidColorBrush(Color.Parse("#ef4444"))
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
