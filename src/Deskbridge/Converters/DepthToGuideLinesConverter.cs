using System.Globalization;
using System.Windows.Data;

namespace Deskbridge.Converters;

/// <summary>
/// Returns a collection of horizontal positions (doubles) for vertical indent guide lines.
/// Each position is centered within its indent column: (i * IndentSize) + (IndentSize / 2).
/// STAB-05: Accepts an int depth from the ViewModel instead of walking the visual tree,
/// making it safe for virtualized TreeView containers that recycle.
/// Depth-0 items (root level) produce an empty collection (no guides needed).
/// </summary>
public sealed class DepthToGuideLinesConverter : IValueConverter
{
    public double IndentSize { get; set; } = 19.0;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not int depth || depth <= 0)
            return Array.Empty<double>();

        var positions = new double[depth];
        for (int i = 0; i < depth; i++)
            positions[i] = (i * IndentSize) + (IndentSize / 2.0);

        return positions;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
