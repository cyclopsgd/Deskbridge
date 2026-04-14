using System.Windows.Input;
using Deskbridge.ViewModels;

namespace Deskbridge;

/// <summary>
/// Plan 05-03 D-16: static router for the Phase 5 keyboard shortcut family that
/// <see cref="MainWindow.OnPreviewKeyDown"/> delegates to. Extracted from the
/// code-behind so the routing logic can be exercised by plain unit tests without
/// instantiating a real <see cref="System.Windows.Window"/>.
///
/// <para>Handles: Ctrl+Tab / Ctrl+Shift+Tab (cycle), Ctrl+F4 (close active),
/// Ctrl+1..Ctrl+9 (jump; Ctrl+9 is the LAST tab per Chrome/VS Code convention),
/// Ctrl+Shift+T (reopen last closed). Ctrl+W stays in XAML <c>KeyBinding</c> —
/// we do not re-handle it here.</para>
/// </summary>
public static class KeyboardShortcutRouter
{
    /// <summary>
    /// Try to route <paramref name="key"/> + <paramref name="modifiers"/> to one of
    /// the Phase 5 tab commands on <paramref name="vm"/>. Returns true when a
    /// shortcut was matched (even when the command CanExecute was false — that
    /// means the key was "handled enough" not to bubble into the AxHost).
    /// </summary>
    public static bool TryRoute(MainWindowViewModel vm, Key key, ModifierKeys modifiers)
    {
        var ctrl = (modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        var shift = (modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
        var alt = (modifiers & ModifierKeys.Alt) == ModifierKeys.Alt;

        // Only plain Ctrl / Ctrl+Shift combinations. Alt-modified keys belong to
        // the remote session (AxHost handles Alt+Tab etc.).
        if (!ctrl || alt) return false;

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
