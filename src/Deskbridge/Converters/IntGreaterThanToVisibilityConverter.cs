using System.Globalization;
using System.Windows.Data;

namespace Deskbridge.Converters;

/// <summary>
/// Phase 22 Plan 22-02 (UI-SPEC §"Failure List Layout"): collapses a control
/// unless the bound integer exceeds the threshold supplied via
/// <see cref="IValueConverter.Convert"/>'s <c>parameter</c>. Used by the
/// import wizard's "+ N more" overflow footer (visible iff
/// <c>FailedCount &gt; 50</c>).
/// </summary>
public sealed class IntGreaterThanToVisibilityConverter : IValueConverter
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
        return n > threshold ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
