using System.Globalization;
using System.Windows.Data;
using Wpf.Ui.Controls;

namespace Deskbridge.Converters;

/// <summary>
/// Converts a boolean "expanded" state to a chevron glyph.
/// True -> ChevronDown (panel is expanded, click to collapse).
/// False -> ChevronUp (panel is collapsed, click to expand).
/// See WPF-TREEVIEW-PATTERNS.md Section 3.
/// </summary>
[ValueConversion(typeof(bool), typeof(SymbolRegular))]
public sealed class BoolToChevronConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? SymbolRegular.ChevronDown24 : SymbolRegular.ChevronUp24;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
