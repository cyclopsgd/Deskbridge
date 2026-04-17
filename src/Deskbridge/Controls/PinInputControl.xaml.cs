using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Deskbridge.Controls;

/// <summary>
/// 6-cell masked PIN input control. Each cell is a single-character
/// <see cref="TextBox"/> displaying a bullet (U+2022) while the actual
/// digit is stored in a private <c>char[]</c> backing array.
///
/// <para><b>T-pin-01:</b> real digits never appear in <see cref="TextBox.Text"/>
/// -- only bullet characters are displayed. The actual digits live in
/// <see cref="_digits"/> and are exposed via the <see cref="Pin"/> DP.</para>
///
/// <para><b>T-pin-02:</b> clipboard paste is filtered to digits-only via
/// <see cref="DistributeDigits"/>. Non-digit content is silently rejected.</para>
///
/// <para>Static helpers <see cref="IsDigit"/>, <see cref="DistributeDigits"/>,
/// and <see cref="AssemblePin"/> are <c>internal static</c> so the test project
/// can exercise them directly via InternalsVisibleTo.</para>
/// </summary>
public partial class PinInputControl : UserControl
{
    private const char Bullet = '\u2022';
    private const int CellCount = 6;

    private readonly TextBox[] _cells = new TextBox[CellCount];
    private readonly char[] _digits = new char[CellCount];
    private bool _suppressTextChanged;

    /// <summary>
    /// Identifies the <see cref="Pin"/> dependency property.
    /// Two-way by default so LockOverlayDialog can bind to VM's Password / ConfirmPassword.
    /// </summary>
    public static readonly DependencyProperty PinProperty = DependencyProperty.Register(
        nameof(Pin),
        typeof(string),
        typeof(PinInputControl),
        new FrameworkPropertyMetadata(
            "",
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnPinPropertyChanged));

    /// <summary>
    /// The assembled PIN string (digits only). Bound two-way to the VM's
    /// Password or ConfirmPassword property.
    /// </summary>
    public string Pin
    {
        get => (string)GetValue(PinProperty);
        set => SetValue(PinProperty, value);
    }

    /// <summary>
    /// Raised when all 6 cells are filled. The dialog can subscribe
    /// to auto-submit on the 6th digit during unlock mode.
    /// </summary>
    public event EventHandler? PinComplete;

    public PinInputControl()
    {
        InitializeComponent();

        _cells[0] = Cell0;
        _cells[1] = Cell1;
        _cells[2] = Cell2;
        _cells[3] = Cell3;
        _cells[4] = Cell4;
        _cells[5] = Cell5;

        for (int i = 0; i < CellCount; i++)
        {
            int index = i; // capture for closures
            var cell = _cells[i];

            cell.PreviewTextInput += (_, e) => OnCellPreviewTextInput(index, e);
            cell.PreviewKeyDown += (_, e) => OnCellPreviewKeyDown(index, e);

            // Block paste via Ctrl+V and context-menu paste — route through our handler.
            DataObject.AddPastingHandler(cell, (_, e) => OnCellPaste(index, e));
        }
    }

    // ---- Public API ----

    /// <summary>
    /// Clears all cells, zeros the backing array, resets the Pin DP,
    /// and focuses the first cell.
    /// </summary>
    public void Clear()
    {
        _suppressTextChanged = true;
        try
        {
            Array.Clear(_digits);
            foreach (var cell in _cells)
                cell.Text = "";
            Pin = "";
        }
        finally
        {
            _suppressTextChanged = false;
        }
    }

    /// <summary>
    /// Moves keyboard focus to the first cell.
    /// </summary>
    public void FocusFirst()
    {
        _cells[0].Focus();
    }

    // ---- Internal static helpers (testable) ----

    /// <summary>
    /// Returns <c>true</c> if <paramref name="c"/> is an ASCII digit ('0'-'9').
    /// </summary>
    internal static bool IsDigit(char c) => c is >= '0' and <= '9';

    /// <summary>
    /// Distributes a pasted string into 6 cell values. Returns <c>null</c> if
    /// the input contains any non-digit character. If the input has more than 6
    /// digits, only the first 6 are used. Empty input returns 6 empty strings.
    /// </summary>
    internal static string[]? DistributeDigits(string input)
    {
        if (string.IsNullOrEmpty(input))
            return ["", "", "", "", "", ""];

        // Reject if ANY character is non-digit (T-pin-02 threat mitigation).
        foreach (char c in input)
        {
            if (!IsDigit(c))
                return null;
        }

        // Take at most 6 digits.
        var result = new string[CellCount];
        for (int i = 0; i < CellCount; i++)
        {
            result[i] = i < input.Length ? input[i].ToString() : "";
        }
        return result;
    }

    /// <summary>
    /// Assembles a PIN string from 6 cell values by concatenating
    /// non-empty entries. Stops at the first empty cell.
    /// </summary>
    internal static string AssemblePin(string[] cells)
    {
        var sb = new System.Text.StringBuilder(CellCount);
        foreach (var cell in cells)
        {
            if (string.IsNullOrEmpty(cell))
                break;
            sb.Append(cell);
        }
        return sb.ToString();
    }

    // ---- Event handlers ----

    private void OnCellPreviewTextInput(int index, TextCompositionEventArgs e)
    {
        e.Handled = true; // Always suppress default text entry -- we control it.

        if (e.Text.Length != 1 || !IsDigit(e.Text[0]))
            return;

        _digits[index] = e.Text[0];

        _suppressTextChanged = true;
        _cells[index].Text = Bullet.ToString();
        _suppressTextChanged = false;

        UpdatePin();

        // Auto-advance to next cell.
        if (index < CellCount - 1)
        {
            _cells[index + 1].Focus();
        }
        else
        {
            // All 6 filled -- raise PinComplete for auto-submit.
            if (Pin.Length == CellCount)
                PinComplete?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnCellPreviewKeyDown(int index, KeyEventArgs e)
    {
        if (e.Key == Key.Back)
        {
            e.Handled = true;

            if (_cells[index].Text.Length > 0)
            {
                // Clear current cell.
                _digits[index] = '\0';
                _suppressTextChanged = true;
                _cells[index].Text = "";
                _suppressTextChanged = false;
                UpdatePin();
            }
            else if (index > 0)
            {
                // Move to previous cell and clear it.
                _digits[index - 1] = '\0';
                _suppressTextChanged = true;
                _cells[index - 1].Text = "";
                _suppressTextChanged = false;
                _cells[index - 1].Focus();
                UpdatePin();
            }
        }
        else if (e.Key == Key.Delete)
        {
            // Clear current cell without moving.
            e.Handled = true;
            _digits[index] = '\0';
            _suppressTextChanged = true;
            _cells[index].Text = "";
            _suppressTextChanged = false;
            UpdatePin();
        }
        else if (e.Key == Key.Left && index > 0)
        {
            e.Handled = true;
            _cells[index - 1].Focus();
        }
        else if (e.Key == Key.Right && index < CellCount - 1)
        {
            e.Handled = true;
            _cells[index + 1].Focus();
        }
    }

    private void OnCellPaste(int startIndex, DataObjectPastingEventArgs e)
    {
        e.CancelCommand(); // Cancel default paste.

        if (!e.DataObject.GetDataPresent(DataFormats.UnicodeText) &&
            !e.DataObject.GetDataPresent(DataFormats.Text))
            return;

        var text = e.DataObject.GetData(DataFormats.UnicodeText) as string
                   ?? e.DataObject.GetData(DataFormats.Text) as string;
        if (text is null) return;

        var distributed = DistributeDigits(text);
        if (distributed is null) return; // Non-digit content rejected.

        _suppressTextChanged = true;
        try
        {
            for (int i = 0; i < CellCount; i++)
            {
                if (!string.IsNullOrEmpty(distributed[i]))
                {
                    _digits[i] = distributed[i][0];
                    _cells[i].Text = Bullet.ToString();
                }
                else
                {
                    _digits[i] = '\0';
                    _cells[i].Text = "";
                }
            }
        }
        finally
        {
            _suppressTextChanged = false;
        }

        UpdatePin();

        // Focus the last filled cell (or the next empty one).
        int focusIndex = Math.Min(text.Length, CellCount) - 1;
        if (focusIndex >= 0 && focusIndex < CellCount)
            _cells[focusIndex].Focus();

        if (Pin.Length == CellCount)
            PinComplete?.Invoke(this, EventArgs.Empty);
    }

    private void UpdatePin()
    {
        var cellValues = new string[CellCount];
        for (int i = 0; i < CellCount; i++)
        {
            cellValues[i] = _digits[i] == '\0' ? "" : _digits[i].ToString();
        }
        Pin = AssemblePin(cellValues);
    }

    // ---- Pin DP changed callback (external set, e.g. Clear from VM binding) ----

    private static void OnPinPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PinInputControl control) return;
        if (control._suppressTextChanged) return;

        var newPin = e.NewValue as string ?? "";

        // If the DP is cleared externally (e.g., VM sets Password = ""),
        // distribute the new value into cells.
        var distributed = DistributeDigits(newPin);
        if (distributed is null)
        {
            // Non-digit value pushed from VM -- clear everything.
            control.Clear();
            return;
        }

        control._suppressTextChanged = true;
        try
        {
            for (int i = 0; i < CellCount; i++)
            {
                if (!string.IsNullOrEmpty(distributed[i]))
                {
                    control._digits[i] = distributed[i][0];
                    control._cells[i].Text = Bullet.ToString();
                }
                else
                {
                    control._digits[i] = '\0';
                    control._cells[i].Text = "";
                }
            }
        }
        finally
        {
            control._suppressTextChanged = false;
        }
    }
}
