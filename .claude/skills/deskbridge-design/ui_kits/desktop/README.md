# Deskbridge Desktop UI Kit

Hi-fi recreation of the Deskbridge WPF app shell as a clickable React prototype.

## What's in the kit

- **Icons.jsx** — small Fluent-style icon set (stand-in for Fluent UI System Icons)
- **Shell.jsx** — `TitleBar`, `IconRail`, `SectionLabel`, `Card`, `Toggle`, `StatusBar`, `ToastStack`, `Toast`
- **ConnectionTree.jsx** — search box + full-row tree with groups, expand/collapse, selection (2px accent stripe), `Key` affordance for groups with saved credentials
- **Tabs.jsx** — `TabStrip` with connecting spinner, reconnecting amber dot, error red dot, active top border
- **Viewport.jsx** — empty state, connecting spinner, error state, fake connected RDP session
- **CommandPalette.jsx** — Ctrl+Shift+P modal with fuzzy highlight, keyboard navigation
- **App.jsx** — wiring: double-click a tree row or pick from the palette to open a session tab

## What's recreated, not invented

Every layout value comes from `MainWindow.xaml` and `ConnectionTreeControl.xaml`:

| Element | Value |
|---------|-------|
| TitleBar | 32 |
| Icon rail | 36 (Deskbridge uses 36, not Fluent default 48) |
| Slide-out panel | 240 |
| Tab bar | 30 |
| Status bar | 22 |
| Row height | 28 |
| Tree indent | 19 per depth |
| Status dot | 8 |
| Accent stripe | 2×16 |

## Deliberate omissions

- No drag-and-drop tab reorder (the WPF `TabReorderBehavior` is non-trivial and the UI kit is cosmetic)
- No multi-select in the tree (covered by `TreeViewMultiSelectBehavior` — omitted in the kit)
- No quick-properties panel at the bottom of the connection tree (present in the app; skipped here for brevity)
- No Mica — we fake it with a radial gradient + backdrop-filter; real Mica is a Windows DWM effect

## Substitutions

- **Icons**: Lucide-style SVGs stand in for Fluent System Icons. Visually close but not identical.
- **Font**: Inter stands in for Segoe UI Variable. Monospace uses JetBrains Mono.
