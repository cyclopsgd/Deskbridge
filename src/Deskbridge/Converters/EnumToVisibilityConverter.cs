using System.Globalization;
using System.Windows.Data;

namespace Deskbridge.Converters;

/// <summary>
/// Returns <see cref="Visibility.Visible"/> iff <c>value.ToString()</c> equals
/// <c>parameter.ToString()</c> (case-sensitive). Used by
/// <c>ReconnectOverlay.xaml</c> to show/hide the Auto-vs-Manual button rows
/// based on <c>ReconnectOverlayViewModel.Mode</c> (Plan 04-03 Task 1.3).
/// </summary>
public sealed class EnumToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null || parameter is null) return Visibility.Collapsed;
        return string.Equals(value.ToString(), parameter.ToString(), StringComparison.Ordinal)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
