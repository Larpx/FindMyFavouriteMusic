using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Larpx.PersonalTools.FindMyFavouriteMusic.GUI.Converters;

/// <summary>
/// 分数转颜色转换器，暗色科幻主题：高匹配度电光青、中等霓虹紫、低匹配度警告红
/// </summary>
public class ScoreToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var score = value as double? ?? 0;

        return score switch
        {
            >= 70 => new SolidColorBrush(Color.Parse("#00ff88")),
            >= 40 => new SolidColorBrush(Color.Parse("#ffaa00")),
            _ => new SolidColorBrush(Color.Parse("#ff3366"))
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
