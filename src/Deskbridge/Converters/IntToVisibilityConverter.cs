using System.Globalization;
using System.Windows.Data;

namespace Deskbridge.Converters;

/// <summary>
/// Phase 22 Plan 22-02 (UI-SPEC §"Failure List Layout"): collapses a control
/// when the bound integer value is zero, shows it otherwise. Used by the
/// import wizard Step 4 failure-list section to hide the heading + ItemsControl
/// when <c>FailedCount==0</c>.
/// </summary>
public sealed class IntToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var n = value switch
        {
            int i => i,
            null => 0,
            _ => System.Convert.ToInt32(value, culture),
        };
        return n > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
