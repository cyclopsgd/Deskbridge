using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Deskbridge.Converters;

/// <summary>
/// Produces a left Thickness margin based on a TreeViewItem's depth in the visual tree.
/// Used by the full-row TreeViewItem ControlTemplate so that the selection highlight
/// spans the entire row while content is indented per level.
/// See WPF-TREEVIEW-PATTERNS.md Section 2.
/// </summary>
public sealed class TreeViewItemIndentConverter : IValueConverter
{
    public double IndentSize { get; set; } = 19.0;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not TreeViewItem tvi) return new Thickness(0);
        int depth = 0;
        DependencyObject parent = VisualTreeHelper.GetParent(tvi);
        while (parent is not null)
        {
            if (parent is TreeViewItem) depth++;
            if (parent is TreeView) break;
            parent = VisualTreeHelper.GetParent(parent);
        }
        return new Thickness(IndentSize * depth, 0, 0, 0);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
