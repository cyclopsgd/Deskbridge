---
phase: quick
plan: 260417-bfz
type: execute
wave: 1
depends_on: []
files_modified:
  - src/Deskbridge/Controls/PinInputControl.xaml
  - src/Deskbridge/Controls/PinInputControl.xaml.cs
  - src/Deskbridge/Dialogs/LockOverlayDialog.xaml
  - src/Deskbridge/Dialogs/LockOverlayDialog.xaml.cs
  - tests/Deskbridge.Tests/Controls/PinInputControlTests.cs
autonomous: true
must_haves:
  truths:
    - "PIN mode shows 6 individual masked digit cells instead of a single PasswordBox"
    - "Typing a digit fills the current cell with a bullet and auto-advances to the next cell"
    - "Backspace clears current cell or moves back to previous cell if current is empty"
    - "Non-digit characters are rejected (digits-only filter)"
    - "Pasting a 6-digit string distributes across all cells"
    - "When all 6 cells filled, the Pin dependency property contains the full 6-digit string"
    - "Password mode still uses the existing PasswordBox (no regression)"
    - "First-run confirm field shows a second PinInputControl when IsPinMode is true"
  artifacts:
    - path: "src/Deskbridge/Controls/PinInputControl.xaml"
      provides: "6-cell PIN input UserControl XAML"
    - path: "src/Deskbridge/Controls/PinInputControl.xaml.cs"
      provides: "PinInputControl code-behind with Pin DP, auto-advance, backspace, paste, digits-only"
    - path: "tests/Deskbridge.Tests/Controls/PinInputControlTests.cs"
      provides: "Unit tests for digit filter logic, paste distribution, Pin DP assembly"
  key_links:
    - from: "src/Deskbridge/Controls/PinInputControl.xaml.cs"
      to: "LockOverlayViewModel.Password"
      via: "Pin dependency property two-way bound"
      pattern: "Pin.*DependencyProperty"
    - from: "src/Deskbridge/Dialogs/LockOverlayDialog.xaml"
      to: "src/Deskbridge/Controls/PinInputControl.xaml"
      via: "DataTrigger on IsPinMode swaps Visibility"
      pattern: "PinInputControl"
---

<objective>
Replace the single PasswordBox used in PIN mode with a 6-cell PinInputControl -- six individual masked digit cells in a horizontal row with auto-advance, backspace-back, paste support, and a digits-only filter. Swap the control into LockOverlayDialog.xaml via Visibility bindings on IsPinMode, with a second PinInputControl for the first-run Confirm PIN field.

Purpose: Provide a purpose-built PIN entry UX that communicates "6 digits" visually, prevents non-digit input at the control level, and auto-submits when complete.
Output: PinInputControl UserControl, updated LockOverlayDialog, unit tests.
</objective>

<execution_context>
@C:\Users\cyclo\.claude\get-shit-done\workflows\execute-plan.md
@C:\Users\cyclo\.claude\get-shit-done\templates\summary.md
</execution_context>

<context>
@CLAUDE.md
@DESIGN.md (colour tokens, spacing, control styling)
@WPF-UI-PITFALLS.md (Pitfall 5: Color vs Brush keys, Pitfall 8a: avoid explicit Height on WPF-UI controls)
@src/Deskbridge/Controls/ToastStackControl.xaml (reference UserControl pattern)
@src/Deskbridge/Controls/ToastStackControl.xaml.cs (reference UserControl code-behind)
@src/Deskbridge/Dialogs/LockOverlayDialog.xaml (current PIN PasswordBox to replace)
@src/Deskbridge/Dialogs/LockOverlayDialog.xaml.cs (PasswordChanged handlers, Pitfall 8 Enter handler)
@src/Deskbridge/ViewModels/LockOverlayViewModel.cs (Password/ConfirmPassword properties, IsPinMode)

<interfaces>
<!-- Key types and contracts the executor needs. -->

From src/Deskbridge/ViewModels/LockOverlayViewModel.cs:
```csharp
// Properties the PinInputControl.Pin DP will bind to:
public partial string Password { get; set; }       // [ObservableProperty]
public partial string ConfirmPassword { get; set; } // [ObservableProperty]
public partial bool IsPinMode { get; set; }         // [ObservableProperty]
public partial bool IsFirstRun { get; set; }        // [ObservableProperty]
public partial bool IsPasswordMode { get; set; }    // [ObservableProperty]

// Events the dialog code-behind subscribes to:
public event EventHandler? UnlockSucceeded;
public event EventHandler? RequestFocusPassword;

// Command bound to Unlock button:
[RelayCommand] public void Unlock();
```

From src/Deskbridge/Dialogs/LockOverlayDialog.xaml.cs:
```csharp
// Existing code-behind handlers that remain for the PasswordBox path:
private void PasswordField_PasswordChanged(object sender, RoutedEventArgs e)
    => _vm.Password = ((Wpf.Ui.Controls.PasswordBox)sender).Password;
private void ConfirmField_PasswordChanged(object sender, RoutedEventArgs e)
    => _vm.ConfirmPassword = ((Wpf.Ui.Controls.PasswordBox)sender).Password;

// RequestFocusPassword handler clears PasswordField and re-focuses.
// IsPinMode PropertyChanged handler clears both fields.
```
</interfaces>
</context>

<tasks>

<task type="auto" tdd="true">
  <name>Task 1: Create PinInputControl UserControl</name>
  <files>
    src/Deskbridge/Controls/PinInputControl.xaml
    src/Deskbridge/Controls/PinInputControl.xaml.cs
    tests/Deskbridge.Tests/Controls/PinInputControlTests.cs
  </files>
  <behavior>
    - Test: IsDigit filter accepts '0'-'9', rejects 'a', ' ', '!', etc. (test the static helper method)
    - Test: DistributeDigits("123456") returns ["1","2","3","4","5","6"] (6 elements)
    - Test: DistributeDigits("12") returns ["1","2","","","",""] (partial fill)
    - Test: DistributeDigits("abc123") returns empty/ignored (non-digit paste rejected)
    - Test: DistributeDigits("1234567890") truncates to first 6 digits
    - Test: AssemblePin(["1","2","3","4","5","6"]) returns "123456"
    - Test: AssemblePin(["1","2","","","",""])  returns "12" (partial)
  </behavior>
  <action>
Create `src/Deskbridge/Controls/PinInputControl.xaml` as a UserControl following the ToastStackControl pattern:
- Namespace: `xmlns:local="clr-namespace:Deskbridge.Controls"`
- Content: A horizontal `StackPanel` (or `UniformGrid Columns="6"`) containing 6 single-character `TextBox` controls.
- Each TextBox: Width="44", Height="44" (use explicit Height here -- these are stock WPF TextBox, not WPF-UI TextBox, so Pitfall 8a does not apply), MaxLength="1", TextAlignment="Center", FontSize="22", FontFamily="Segoe UI", CornerRadius via a Style.
- Use standard WPF TextBox (not ui:TextBox) to avoid WPF-UI template overhead on tiny single-char cells.
- All colours via DynamicResource: Background=ControlFillColorDefaultBrush, BorderBrush=ControlStrokeColorDefaultBrush, Foreground=TextFillColorPrimaryBrush.
- CornerRadius="4" via a Style setter on the Border template (or use the WPF-UI auto-restyled TextBox which already has rounded corners -- test which renders better).
- Margin="4,0" between cells for ~8px total gap.
- Masking: use the `PasswordChar` concept by storing the actual digit in a backing array and displaying a bullet character (U+2022) in the TextBox. Alternatively, set `FontFamily="Segoe UI Symbol"` and replace typed digits with the bullet on TextChanged -- the backing `_digits` char array holds the real values.

Create `src/Deskbridge/Controls/PinInputControl.xaml.cs`:
- DependencyProperty `Pin` (string, default "", BindsTwoWayByDefault, with a PropertyChangedCallback that distributes incoming value into cells when set programmatically -- needed for Clear scenarios).
- DependencyProperty `PinComplete` as a RoutedEvent (optional, for auto-submit).
- Private `TextBox[] _cells` array populated in Loaded event by finding the 6 named TextBoxes (Cell0..Cell5) or by generating them in code-behind and adding to the StackPanel.
- Private `char[] _digits = new char[6]` stores actual digit values.
- `PreviewTextInput` handler on each cell: if `e.Text` is not a single digit char, set `e.Handled = true` (reject). If it is a digit, store in `_digits[index]`, display bullet in cell, advance focus to next cell, update Pin DP. If all 6 filled, raise PinComplete event.
- `PreviewKeyDown` handler on each cell: if Key.Back and cell is empty, move focus to previous cell and clear it. If Key.Back and cell has content, clear it.
- Paste handler via `DataObject.Pasting` or `CommandManager.PreviewExecuted` for Paste: extract clipboard text, filter to digits only, if 6+ digits take first 6, distribute into cells and _digits, update Pin DP, focus last filled cell.
- `Clear()` public method: zeros _digits array, clears all cell text, focuses Cell0, sets Pin to "".
- `FocusFirst()` public method: focuses Cell0.
- Make `IsDigit`, `DistributeDigits`, and `AssemblePin` as `internal static` methods so the test project can call them directly.
- Add `[assembly: InternalsVisibleTo("Deskbridge.Tests")]` if not already present (check first -- it may already exist from prior phases).

Create `tests/Deskbridge.Tests/Controls/PinInputControlTests.cs`:
- Test class `PinInputControlTests` with tests for the static helper methods listed in the behavior section above.
- These are pure logic tests -- no STA thread or UI required.
- Use xUnit v3, FluentAssertions, matching project GlobalUsings.
  </action>
  <verify>
    <automated>dotnet test tests/Deskbridge.Tests --filter "FullyQualifiedName~PinInputControlTests" --no-restore</automated>
  </verify>
  <done>PinInputControl.xaml and .cs exist under src/Deskbridge/Controls/. Static helper methods (IsDigit, DistributeDigits, AssemblePin) have passing unit tests. Pin dependency property declared. Build passes with 0 warnings.</done>
</task>

<task type="auto">
  <name>Task 2: Swap PinInputControl into LockOverlayDialog</name>
  <files>
    src/Deskbridge/Dialogs/LockOverlayDialog.xaml
    src/Deskbridge/Dialogs/LockOverlayDialog.xaml.cs
  </files>
  <action>
Modify `src/Deskbridge/Dialogs/LockOverlayDialog.xaml`:

1. Add xmlns for controls: `xmlns:ctrl="clr-namespace:Deskbridge.Controls"`.

2. Replace the current PasswordField block (lines 78-93) with TWO controls sharing the same grid slot, toggled by Visibility:

   a) The EXISTING `ui:PasswordBox x:Name="PasswordField"` -- keep it, but wrap its Visibility so it only shows when IsPinMode is False. Use a Style DataTrigger: default Visibility="Visible", DataTrigger on IsPinMode=True sets Visibility="Collapsed". Remove the MaxLength DataTrigger (PIN mode no longer uses this control).

   b) A NEW `ctrl:PinInputControl x:Name="PinField"` -- default Visibility="Collapsed", DataTrigger on IsPinMode=True sets Visibility="Visible". Bind Pin="{Binding Password, Mode=TwoWay}".

3. Replace the current ConfirmField block (lines 97-114) with TWO controls, same pattern:

   a) The EXISTING `ui:PasswordBox x:Name="ConfirmField"` -- keep it but add IsPinMode=False visibility guard AND IsFirstRun visibility (both must be true to show). Simplest: wrap in a StackPanel or use MultiBinding converter. OR: use two nested Visibility conditions. Pragmatic approach: keep the existing IsFirstRun BoolToVisibility binding, add a DataTrigger that collapses when IsPinMode=True.

   b) A NEW `ctrl:PinInputControl x:Name="ConfirmPinField"` -- visible only when IsFirstRun=True AND IsPinMode=True. Bind Pin="{Binding ConfirmPassword, Mode=TwoWay}". Use DataTrigger or dual Visibility binding.

Modify `src/Deskbridge/Dialogs/LockOverlayDialog.xaml.cs`:

1. In the IsPinMode PropertyChanged handler (the lambda in the constructor):
   - Add: `PinField.Clear(); ConfirmPinField.Clear();` alongside the existing PasswordField/ConfirmField clears.

2. In the RequestFocusPassword handler:
   - If `_vm.IsPinMode`, call `PinField.Clear(); PinField.FocusFirst();` instead of clearing/focusing PasswordField.

3. In OnLoaded:
   - If `_vm.IsPinMode`, focus PinField.FocusFirst() instead of PasswordField.Focus().

4. In Dialog_PreviewKeyDown (Pitfall 8 handler):
   - The existing guard checks for `Wpf.Ui.Controls.PasswordBox or System.Windows.Controls.PasswordBox`. Add `or System.Windows.Controls.TextBox` so Enter in a PinInputControl cell also triggers UnlockCommand. (The PinInputControl cells are standard TextBox instances.)

5. Wire PinComplete event (optional auto-submit): In the constructor, subscribe to `PinField.PinComplete += (_, _) => { if (_vm.UnlockCommand.CanExecute(null)) _vm.UnlockCommand.Execute(null); };` This gives instant submit when the 6th digit is entered during unlock mode (not first-run, where they still need to fill the confirm field).

6. The existing PasswordField_PasswordChanged and ConfirmField_PasswordChanged handlers remain unchanged -- they still serve the password (non-PIN) path.
  </action>
  <verify>
    <automated>dotnet build src/Deskbridge/Deskbridge.csproj --no-restore -c Release -warnaserror</automated>
  </verify>
  <done>
    - PIN mode in LockOverlayDialog shows 6-cell PinInputControl instead of PasswordBox.
    - Password mode still shows the PasswordBox (no regression).
    - First-run + PIN mode shows two PinInputControls (entry + confirm).
    - Enter key in PIN cells triggers UnlockCommand (Pitfall 8 maintained).
    - Typing 6th digit in unlock mode auto-submits.
    - Mode switching clears all fields.
    - Build passes with 0 warnings in Release config.
    - All existing tests pass: `dotnet test tests/Deskbridge.Tests --no-restore`
  </done>
</task>

</tasks>

<threat_model>
## Trust Boundaries

| Boundary | Description |
|----------|-------------|
| Clipboard -> PinInputControl | Untrusted paste data enters the control |
| Keyboard -> PinInputControl | Untrusted key input enters the control |

## STRIDE Threat Register

| Threat ID | Category | Component | Disposition | Mitigation Plan |
|-----------|----------|-----------|-------------|-----------------|
| T-pin-01 | Information Disclosure | PinInputControl cells | mitigate | Display bullet characters only, never show actual digits in TextBox.Text. Store real digits in private char[] backing array. |
| T-pin-02 | Tampering | Clipboard paste | mitigate | Filter pasted text to digits-only via static DistributeDigits method. Reject non-digit content silently. |
| T-pin-03 | Information Disclosure | Pin DP value | accept | Pin DP holds plaintext digits in memory (same as existing PasswordBox approach). SecureString banned per CLAUDE.md. LockOverlayViewModel already scrubs Password on submit (T-06-05). |
</threat_model>

<verification>
1. `dotnet build src/Deskbridge/Deskbridge.csproj -c Release -warnaserror` -- 0 warnings
2. `dotnet test tests/Deskbridge.Tests --no-restore` -- all tests pass including new PinInputControlTests
3. Existing LockOverlayViewModelTests pass unchanged (VM logic not modified)
4. LockOverlayDialog_HasPitfall8EnterHandler source-grep test still passes (Dialog_PreviewKeyDown + Key.Enter + UnlockCommand + e.Handled still present)
</verification>

<success_criteria>
- PinInputControl renders 6 masked cells in PIN mode
- Auto-advance, backspace-back, paste-distribute, digits-only all work
- Password mode renders existing PasswordBox (no regression)
- First-run shows confirm PinInputControl
- All existing tests pass, new unit tests pass
- Build: 0 warnings
</success_criteria>

<output>
After completion, create `.planning/quick/260417-bfz-replace-pin-passwordbox-with-6-cell-pini/260417-bfz-SUMMARY.md`
</output>
