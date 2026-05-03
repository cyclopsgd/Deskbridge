using System.Globalization;
using System.Windows.Data;

namespace Deskbridge.Converters;

/// <summary>
/// Phase 22 Plan 22-02 (UI-SPEC §"Failure List Layout"): renders the overflow
/// footer text "+ {N} more (see log)" where <c>N = value - parameter</c>. Used
/// by the import wizard's failure-list when the visible cap (50 rows) is exceeded.
/// </summary>
public sealed class OverflowMoreTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var n = value switch
        {
            int i => i,
            null => 0,
            _ => System.Convert.ToInt32(value, culture),
        };
        var threshold = parameter switch
        {
            int i => i,
            string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var t) => t,
            null => 0,
            _ => System.Convert.ToInt32(parameter, CultureInfo.InvariantCulture),
        };
        var more = Math.Max(0, n - threshold);
        return $"+ {more} more (see log)";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
