using System.Globalization;
using System.Windows.Data;

namespace Deskbridge.Converters;

/// <summary>
/// Phase 6 Plan 06-03: hides a control when the bound value is null or an empty
/// string. Used by the <c>CommandPaletteDialog</c> to suppress the Subtitle
/// TextBlock and Shortcut badge rendering when those fields are unset
/// (<c>CommandPaletteRowViewModel.Subtitle</c> and <c>.Shortcut</c>).
/// </summary>
public sealed class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null) return Visibility.Collapsed;
        if (value is string s && string.IsNullOrEmpty(s)) return Visibility.Collapsed;
        return Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
