using System.Windows.Input;
using Deskbridge.ViewModels;

namespace Deskbridge;

/// <summary>
/// Plan 05-03 D-16 + Plan 06-03 CMD-01/CMD-04: static router for the Phase 5 + 6
/// keyboard shortcut family that <see cref="MainWindow.OnPreviewKeyDown"/> delegates
/// to. Extracted from the code-behind so the routing logic can be exercised by
/// plain unit tests without instantiating a real <see cref="System.Windows.Window"/>.
///
/// <para>Handles:</para>
/// <list type="bullet">
/// <item>Phase 5: Ctrl+Tab / Ctrl+Shift+Tab (cycle), Ctrl+F4 (close active),
/// Ctrl+1..Ctrl+9 (jump; Ctrl+9 = LAST per Chrome/VS Code convention),
/// Ctrl+Shift+T (reopen last closed).</item>
/// <item>Phase 6 Plan 06-03: Ctrl+Shift+P (open command palette — no-op command,
/// the actual dialog open is in <see cref="MainWindow.OnPreviewKeyDown"/> since
/// the router has no IContentDialogService dependency), Ctrl+N (new connection —
/// delegates to <see cref="MainWindowViewModel.ConnectionTree"/>), Ctrl+T (quick
/// connect), F11 (toggle fullscreen, D-05), Esc (exit fullscreen when active;
/// pass through otherwise so ContentDialog gets its native backdrop-close).</item>
/// <item>Phase 6 Plan 06-04: Ctrl+L (manual app lock — SEC-04 / D-18).
/// LockAppCommand delegates to AppLockController.LockAsync which is idempotent.</item>
/// </list>
///
/// <para>Ctrl+W stays in XAML <c>KeyBinding</c> — we do not re-handle it here.</para>
/// </summary>
public static class KeyboardShortcutRouter
{
    /// <summary>
    /// Try to route <paramref name="key"/> + <paramref name="modifiers"/> to a
    /// command on <paramref name="vm"/>. Returns true when a shortcut was matched
    /// (even when the command CanExecute was false — that means the key was
    /// "handled enough" not to bubble into the AxHost).
    /// </summary>
    public static bool TryRoute(MainWindowViewModel vm, Key key, ModifierKeys modifiers)
    {
        var ctrl = (modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        var shift = (modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
        var alt = (modifiers & ModifierKeys.Alt) == ModifierKeys.Alt;

        // ---------------- Phase 6 Plan 06-03: F11 / Esc (NO Ctrl required) ----------------

        // D-05 (CMD-04): F11 toggles APP fullscreen (WindowState.Maximized + WindowStyle.None).
        // Must not require Ctrl; Alt-modified F11 belongs to the remote session (AxHost).
        if (key == Key.F11 && !ctrl && !alt)
        {
            vm.ToggleFullscreenCommand.Execute(null);
            return true;
        }

        // D-05 (CMD-04): Esc exits APP fullscreen IF fullscreen. Otherwise we must
        // NOT swallow it — ContentDialog (e.g. CommandPaletteDialog) relies on Esc
        // for its native backdrop-close. Plain Esc (no modifiers) only.
        if (key == Key.Escape && !ctrl && !shift && !alt)
        {
            if (vm.IsFullscreen)
            {
                vm.ExitFullscreenCommand.Execute(null);
                return true;
            }
            return false;  // let Esc bubble to ContentDialog / focused control
        }

        // ---------------- Ctrl-modified shortcuts (Phase 5 + 6) ----------------

        // Only plain Ctrl / Ctrl+Shift combinations. Alt-modified keys belong to
        // the remote session (AxHost handles Alt+Tab etc.).
        if (!ctrl || alt) return false;

        // Plan 06-03 Ctrl+Shift+P — open command palette (CMD-01). Router only
        // EXECUTES the no-op placeholder command on the VM; the actual dialog is
        // opened in MainWindow.OnPreviewKeyDown so the IAppLockState gate (Q6) and
        // the IContentDialogService hosting live alongside the Window.
        if (shift && key == Key.P)
        {
            vm.OpenCommandPaletteCommand.Execute(null);
            return true;
        }

        // Plan 06-03 Ctrl+N — new connection (CMD-04). Delegates to ConnectionTreeViewModel
        // which already owns the NewConnectionCommand from Phase 3.
        if (!shift && key == Key.N)
        {
            if (vm.ConnectionTree.NewConnectionCommand.CanExecute(null))
            {
                vm.ConnectionTree.NewConnectionCommand.Execute(null);
            }
            return true;
        }

        // Plan 06-04 Ctrl+L — manual app lock (SEC-04, D-18). LockAppCommand itself is
        // idempotent; the controller's LockAsync checks IAppLockState.IsLocked before
        // mutating state. Return true so the key doesn't bubble to the focused AxHost.
        if (!shift && key == Key.L)
        {
            if (vm.LockAppCommand.CanExecute(null))
            {
                vm.LockAppCommand.Execute(null);
            }
            return true;
        }

        // Plan 06-03 Ctrl+T — quick connect (CMD-04). Ctrl+Shift+T matches earlier
        // (ReopenLastClosed) so this branch runs only when Shift is NOT held.
        if (!shift && key == Key.T)
        {
            if (vm.QuickConnectCommand.CanExecute(null))
            {
                vm.QuickConnectCommand.Execute(null);
            }
            return true;
        }

        // Ctrl+Tab / Ctrl+Shift+Tab — cycle.
        if (key == Key.Tab)
        {
            CycleTab(vm, forward: !shift);
            return true;
        }

        // Ctrl+F4 — close active tab.
        if (!shift && key == Key.F4)
        {
            if (vm.ActiveTab is { } active
                && vm.CloseTabCommand.CanExecute(active))
            {
                vm.CloseTabCommand.Execute(active);
            }
            return true;
        }

        // Ctrl+Shift+T — reopen last closed.
        if (shift && key == Key.T)
        {
            if (vm.ReopenLastClosedCommand.CanExecute(null))
            {
                vm.ReopenLastClosedCommand.Execute(null);
            }
            return true;
        }

        // Ctrl+1..Ctrl+9 — jump to tab N. Ctrl+9 = LAST tab (Chrome convention).
        if (!shift && key >= Key.D1 && key <= Key.D9)
        {
            var count = vm.Tabs.Count;
            if (count == 0) return true;  // handled, no-op
            int idx = key == Key.D9 ? count - 1 : (int)(key - Key.D1);
            if (idx >= 0 && idx < count)
            {
                var tab = vm.Tabs[idx];
                if (vm.SwitchTabCommand.CanExecute(tab))
                {
                    vm.SwitchTabCommand.Execute(tab);
                }
            }
            return true;
        }

        return false;
    }

    /// <summary>
    /// Cycle the active tab. forward=true → next (wrap first after last),
    /// forward=false → previous (wrap last before first). No-op on zero tabs.
    /// When no tab is active, Ctrl+Tab jumps to first and Ctrl+Shift+Tab jumps to last.
    /// </summary>
    private static void CycleTab(MainWindowViewModel vm, bool forward)
    {
        var count = vm.Tabs.Count;
        if (count == 0) return;

        var currentIndex = vm.ActiveTab is null
            ? -1
            : vm.Tabs.IndexOf(vm.ActiveTab);

        int nextIndex = currentIndex < 0
            ? (forward ? 0 : count - 1)
            : (forward ? (currentIndex + 1) % count
                       : (currentIndex - 1 + count) % count);

        var next = vm.Tabs[nextIndex];
        if (vm.SwitchTabCommand.CanExecute(next))
        {
            vm.SwitchTabCommand.Execute(next);
        }
    }
}
