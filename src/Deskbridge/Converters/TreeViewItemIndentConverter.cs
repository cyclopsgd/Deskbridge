using System.Globalization;
using System.Windows.Data;

namespace Deskbridge.Converters;

/// <summary>
/// Produces a left Thickness margin based on an int depth from the ViewModel.
/// STAB-05: Accepts an int depth instead of walking the visual tree,
/// making it safe for virtualized TreeView containers that recycle.
/// Used by the full-row TreeViewItem ControlTemplate so that the selection highlight
/// spans the entire row while content is indented per level.
/// See WPF-TREEVIEW-PATTERNS.md Section 2.
/// </summary>
public sealed class TreeViewItemIndentConverter : IValueConverter
{
    public double IndentSize { get; set; } = 19.0;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not int depth) return new Thickness(0);
        return new Thickness(IndentSize * depth, 0, 0, 0);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
