using System.Globalization;
using System.Windows.Data;

namespace Deskbridge.Converters;

/// <summary>
/// Phase 22 Plan 22-02 (UI-SPEC §"Acceptance #3" — best-effort visual greying):
/// inverts a boolean. Used in <see cref="Dialogs.ImportWizardDialog"/>'s
/// <c>Loaded</c> handler to bind the close-button's <c>IsEnabled</c> property
/// to <c>!IsImportWriteInProgress</c>.
/// </summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? false : true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? false : true;
}
