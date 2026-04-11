# Phase 2: Application Shell - Context

**Gathered:** 2026-04-11
**Status:** Ready for planning

<domain>
## Phase Boundary

Build the complete visual shell: replace the Phase 1 placeholder MainWindow content with the full layout grid — custom title bar, 36px icon rail, 240px slide-out panel (connections/search/settings), 30px tab bar, 22px status bar, and airspace-safe viewport. Dark theme with custom accent colours throughout. No functional connection management yet — just the layout regions with placeholder content.

</domain>

<decisions>
## Implementation Decisions

### Layout Approach
- **D-01:** Custom Grid layout (VS Code-style from DESIGN.md), NOT NavigationView. Manual Grid columns/rows give full control over icon rail, slide-out panel, tab bar, and viewport without fighting NavigationView's opinionated frame management.
- **D-02:** Panel width is fixed at 240px (not user-resizable). No GridSplitter.
- **D-03:** Panel show/hide is instant snap (Visibility toggle), no slide animation.

### Icon Rail Behavior
- **D-04:** VS Code toggle pattern: click icon opens panel with that content. Click same icon again closes panel. Click different icon switches content. Three icons only: Connections (top), Search (top), Settings (bottom).
- **D-05:** Exactly 3 icons for v1: Connections, Search, Settings. Settings pinned to bottom of rail (VS Code convention).

### Tab Bar Design
- **D-06:** Custom tab bar control (hand-built ItemsControl with custom tab item template). Full control for middle-click close, horizontal scroll on overflow, active tab accent border, close button per tab.
- **D-07:** Tab bar is always visible (30px height), even when no connections are open. Consistent layout — no collapsing.

### Empty Viewport
- **D-08:** Subtle branding in empty viewport: centered Deskbridge name/logo with muted text, plus a keyboard shortcut hint ("Ctrl+N to create a connection"). Minimal, professional. Disappears when first tab opens.

### Claude's Discretion
- Icon choices from WPF-UI SymbolIcon library (e.g., Folder24 for Connections, Search24, Settings24)
- Exact panel content layout for each panel mode (connections tree placeholder, search placeholder, settings placeholder)
- Status bar content layout and placeholder text
- Whether active icon in rail gets a visual indicator (accent border, background highlight)

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Architecture & UI Spec
- `REFERENCE.md` §UI Design — Layout dimensions (title bar 32px, icon rail 36px, panel 240px, tab bar 30px, status bar 22px), accent colours (#007ACC, #F44747, #89D185), airspace constraint
- `REFERENCE.md` §Architecture — MainWindowViewModel, DI composition root pattern

### WPF-UI Patterns
- `DESIGN.md` §2 — FluentWindow and TitleBar pattern (already implemented in Phase 1)
- `DESIGN.md` §4 — VS Code-style custom Grid layout with activity bar, side panel, tab bar, status bar (THE layout reference for this phase)
- `DESIGN.md` §5 — Spacing, sizing conventions (4px ramp, font sizes)
- `DESIGN.md` §6 — Colour tokens (DynamicResource, TextFillColorPrimaryBrush, ControlFillColorDefaultBrush, etc.)

### Existing Code
- `src/Deskbridge/MainWindow.xaml` — Current FluentWindow with TitleBar (replace content below TitleBar)
- `src/Deskbridge/MainWindow.xaml.cs` — Code-behind (FluentWindow base class, SystemThemeWatcher)
- `src/Deskbridge/ViewModels/MainWindowViewModel.cs` — Existing ViewModel to extend with panel/tab state
- `src/Deskbridge/App.xaml` — Dark theme setup (ThemesDictionary, ControlsDictionary)
- `src/Deskbridge/App.xaml.cs` — DI composition root

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `MainWindow.xaml`: FluentWindow with TitleBar already set up — extend, don't rebuild
- `MainWindowViewModel.cs`: Exists with basic Title property — extend with panel state, tab collection
- `App.xaml`: Dark theme configured with ThemesDictionary + ControlsDictionary
- `App.xaml.cs`: DI composition root — register new ViewModels here

### Established Patterns
- MVVM with CommunityToolkit.Mvvm ([ObservableProperty], [RelayCommand])
- DI via Microsoft.Extensions.DependencyInjection
- WPF-UI controls via `xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"`
- All colours via `{DynamicResource ...}` tokens — never hardcode

### Integration Points
- MainWindow.xaml content below TitleBar row — this is where the full layout grid goes
- MainWindowViewModel — needs properties for: active panel mode, panel visibility, tab collection, active tab, status bar text
- App.xaml.cs — register any new ViewModels or services

</code_context>

<specifics>
## Specific Ideas

- The layout grid from DESIGN.md §4 (VS Code-style) is the direct reference — 3 columns (48px activity bar, Auto side panel, * editor area) with TitleBar above and status bar below
- REFERENCE.md specifies icon rail at 36px (not 48px from DESIGN.md). Use 36px per REFERENCE.md.
- Status bar should use `SystemAccentColorPrimaryBrush` background (accent-colored, like VS Code)
- Tab bar active tab should have an accent border (top or bottom, 2px)

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 02-application-shell*
*Context gathered: 2026-04-11*
