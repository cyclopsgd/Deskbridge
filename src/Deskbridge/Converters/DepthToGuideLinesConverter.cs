using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Deskbridge.Converters;

/// <summary>
/// Returns a collection of horizontal positions (doubles) for vertical indent guide lines.
/// Each position is centered within its indent column: (i * IndentSize) + (IndentSize / 2).
/// A TreeViewItem at depth N produces N guide positions (for depths 0 through N-1).
/// Depth-0 items (root level) produce an empty collection (no guides needed).
/// </summary>
public sealed class DepthToGuideLinesConverter : IValueConverter
{
    public double IndentSize { get; set; } = 19.0;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not TreeViewItem tvi)
            return Array.Empty<double>();

        int depth = 0;
        DependencyObject parent = VisualTreeHelper.GetParent(tvi);
        while (parent is not null)
        {
            if (parent is TreeViewItem) depth++;
            if (parent is TreeView) break;
            parent = VisualTreeHelper.GetParent(parent);
        }

        if (depth == 0)
            return Array.Empty<double>();

        var positions = new double[depth];
        for (int i = 0; i < depth; i++)
            positions[i] = (i * IndentSize) + (IndentSize / 2.0);

        return positions;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
