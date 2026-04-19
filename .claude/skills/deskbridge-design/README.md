# Deskbridge Design System

A design system for **Deskbridge** — a modern Windows desktop RDP connection manager built to replace mRemoteNG. Designed for infrastructure teams who manage dozens to hundreds of remote connections daily.

Deskbridge is a WPF application built on the [WPF-UI](https://github.com/lepoco/wpfui) Fluent library. The visual language is strictly **Windows 11 Fluent Design**: Mica backdrop, rounded corners, dark-first, WinUI 3 colour tokens, Fluent System Icons. This is a productivity tool for sysadmins — the UI is calm, dense, keyboard-first, and never draws attention to itself.

## Sources

- **Repo**: [cyclopsgd/Deskbridge](https://github.com/cyclopsgd/Deskbridge) (main)
- **Key design references in the repo**:
  - `docs/DESIGN.md` — the canonical WPF-UI authoring guide that the app follows line-for-line
  - `src/Deskbridge/App.xaml` — theme dictionary merge and custom semantic brushes
  - `src/Deskbridge/MainWindow.xaml` — the full shell: TitleBar, icon rail, slide-out panel, tab bar, viewport, status bar
  - `src/Deskbridge/Views/ConnectionTreeControl.xaml` — the full-row TreeViewItem template, expander, quick-properties panel
  - `src/Deskbridge/Dialogs/*.xaml` — credential prompt, change-password, command palette, connection editor
- **Framework stack**: .NET 10, WPF, WPF-UI 4.2.0 (NuGet), CommunityToolkit.Mvvm, AxMsRdpClient9 ActiveX, Serilog, Velopack

## Products / surfaces represented in this system

Deskbridge has **one product surface** — the desktop WPF app. Inside it there are several distinct UI modes that make up the UI kit:

1. **Shell** — FluentWindow with Mica backdrop, TitleBar, icon rail, tab bar, status bar
2. **Connections panel** — tree view with groups, search, quick-properties, drag/drop
3. **Command palette** — Ctrl+Shift+P fuzzy-find dialog (VS Code style)
4. **Dialogs** — ContentDialogs for connection editor, change password, credential prompt, import wizard, crash
5. **Lock overlay** — full-window PIN / master-password gate
6. **Toast stack** — bottom-right notification column (max 3 visible, hover-pause)
7. **Viewport** — the RDP session host area (screen fills it; we preview the chrome around it only)

There is **no marketing website, no docs site, no mobile app**. Everything here is for the desktop client.

## Index — what's in this folder

- **README.md** *(this file)* — the brand guide, visual foundations, iconography, content voice
- **SKILL.md** — skill manifest for Claude Code / Agent Skills
- **colors_and_type.css** — CSS variables for the Windows 11 Fluent token system (colors + type), mapped to WinUI 3 token names (`--text-primary`, `--control-fill-default`, etc.) and custom Deskbridge semantic brushes
- **fonts/** — web fonts (Inter Variable as a close stand-in for Segoe UI Variable; JetBrains Mono for Cascadia Mono)
- **assets/** — logo marks, sample imagery, Fluent icon references
- **preview/** — small HTML cards that render in the Design System tab (colors, type, components, tokens)
- **ui_kits/desktop/** — the JSX recreation of the Deskbridge shell, with a clickable prototype demonstrating the core flows

---

## CONTENT FUNDAMENTALS

### Voice
Terse, technical, lower-case-first in UI chrome (e.g. `Connect`, `Edit`, `New Connection`). Never cute, never emoji. Copy treats the reader as an infrastructure engineer who is busy — short labels, no marketing fluff, no "Let's get you set up!". When a sentence is needed (toast, info bar, dialog body) it is one clear line in plain English.

### Tone rules
- **Title Case** for menu items, button labels, dialog titles: `New Connection`, `Import Connections…`, `Change Password`
- **UPPERCASE** (11px) for section labels inside panels: `CONNECTIONS`, `SECURITY`, `DATA`, `PROPERTIES`
- **Sentence case** for descriptive text inside dialogs and toasts
- **Ellipsis (`…`)** on any action that opens a follow-up dialog — `Move to…`, `Import Connections…`, `Change Password…`
- **Keyboard shortcuts** shown right-aligned in menu items: `Ctrl+N`, `Ctrl+Shift+P`, `Delete`

### I vs. You
Neither. Copy is imperative or declarative — `Saved`, `Enter master password`, `15 active sessions. Close some before opening more.` — never `We saved your changes` or `Your password is required`.

### Emoji
**Never.** Status is communicated by colored dots, icons from the Fluent Symbol set, and the accent-colored status bar — never emoji, never unicode pictographs.

### Examples from the real app
- Empty state: **Deskbridge** / `Ctrl+N to create a connection`
- Empty tree: **No connections** / `Right-click to create a connection or group`
- Toast (success): `Saved` / `Your changes have been saved.`
- Toast (warning): `15 session limit reached. Close some before opening more.`
- Dialog: `Save changes?` / `You have unsaved changes.` → **Save** / **Don't Save** / **Cancel**
- Menu: `Delete` · `Delete` shortcut · red text

### The vibe
Think **VS Code + Windows 11 Settings + a classic sysadmin tool**. Respectful of the user's attention, heavy on keyboard shortcuts, transparent about state (connecting spinner, reconnecting amber dot, error red dot). No hand-holding, no celebration moments, no gradient CTAs. When something succeeds, a small snackbar says so and disappears. When something fails, the tab gets a red dot and an error banner appears inside the viewport.

---

## VISUAL FOUNDATIONS

### Colors
A **dark-first Windows 11 Fluent** palette, using WinUI 3 token brushes via `DynamicResource`. Colours are almost never hard-coded in XAML — they come from the themes dictionary. The only hard-coded exceptions are Deskbridge's three semantic brushes:

| Name | Hex | Use |
|------|-----|-----|
| `DeskbridgeErrorBrush` | `#F44747` | error dot on tab, destructive menu items, failed connections |
| `DeskbridgeSuccessBrush` | `#89D185` | rare — success dots, rarely used because success is usually silent |
| `DeskbridgeWarningBrush` | `#FFCC02` | amber dot = reconnecting, warning info bars |

The **system accent** (WinUI `SystemAccentColorPrimaryBrush`) is the app's only "brand" accent and appears in exactly three places: the status-bar background, the 2px left stripe on selected rows, and the active-tab top border. Default is Windows blue (`#0078D4`), but Deskbridge deliberately respects the user's OS accent — a teal-accent Windows install gets a teal Deskbridge.

Everything else is greyscale on Mica:
- `ApplicationBackgroundBrush` — `#202020` dark / `#FAFAFA` light
- `TextFillColorPrimaryBrush` — `#FFFFFF` / `#000000`
- `TextFillColorSecondaryBrush` — muted (≈ `#FFFFFF` at 60%)
- `TextFillColorTertiaryBrush` — placeholder text (≈ 45%)
- `ControlFillColorDefaultBrush` — panel and card fill
- `ControlStrokeColorDefaultBrush` — 1px hairline borders between regions
- `SubtleFillColorSecondaryBrush` — hover background on rows and tabs

### Type
- **Face**: **Segoe UI Variable** (the Windows 11 system font). Not installable via web — we substitute **Inter Variable** in this design system. On the real app every `FontFamily` is left default and inherits Segoe UI Variable.
- **Monospace**: **Cascadia Mono** in the app; we substitute **JetBrains Mono** here.
- **WinUI type ramp** (all sizes used in Deskbridge):

| Size | Use |
|------|-----|
| 11px | Section labels (uppercase): `CONNECTIONS`, `SECURITY` |
| 12px | Captions, property labels, keyboard-shortcut hints, status bar |
| 14px | Body — everything: tree items, tab titles, buttons, menu items |
| 16px | Card titles, dialog body |
| 20px | Empty-state title ("Deskbridge"), page titles |
| 28px | Dialog titles (sparingly — most dialogs use 20) |

Weights: 400 (body), 600 (SemiBold for selected tab, card titles, section headers). No 700. No italics anywhere.

### Spacing
**4px grid, always.** The real values in the codebase:

- `Margin="0,0,0,8"` or `"0,0,0,12"` — between stacked form rows
- `Padding="12"` or `Padding="16"` — card internal padding
- `Margin="8,4"` — page-content margin inside the slide-out panel
- `Padding="12,6"` or `16,8"` — button padding
- `Margin="0,0,0,16"` or `"0,0,0,24"` — section spacing

Fixed component sizes (pulled directly from MainWindow.xaml):

| Element | Size |
|---------|------|
| TitleBar height | `32px` |
| Icon rail width | `36px` (one below Fluent default of 48 — Deskbridge is compact) |
| Slide-out panel width | `240px` |
| Tab bar height | `30px` |
| Status bar height | `22px` |
| Tab title | MinWidth `96`, MaxWidth `240`, CharacterEllipsis |
| Close button in tab | `16×16` |
| Row height (tree items, menu items) | `28px` minimum |
| Status dots | `8×8` |
| Progress ring in tab | `12×12` |

### Backgrounds
**Mica.** That's it. The root FluentWindow uses `WindowBackdropType="Mica"` with `WindowCornerPreference="Round"`. The desktop wallpaper bleeds through the window chrome at about 60% saturation. No gradients. No hand-drawn illustrations. No patterns. No textures. No full-bleed imagery anywhere in the app. Panels on top of Mica use `ControlFillColorDefaultBrush` (a near-transparent overlay) — never opaque dark grey.

### Animation
Almost none. Respectful, fast, Fluent defaults.
- **Hover**: background fades to `SubtleFillColorSecondaryBrush` — no timing override, so it's the WPF-UI default of ~100ms ease-out
- **Press**: Fluent's built-in press state — a subtle darker fill, no scale transforms
- **Panel show/hide**: instant Visibility swap. No slide-in.
- **Toast**: fade/slide in from the bottom-right (WPF-UI Snackbar default)
- **Progress ring**: indeterminate spin for "connecting"
- **No bounce, no parallax, no shimmer.** No loading skeletons — spinners or nothing.

### Hover states
| Element | Hover treatment |
|---------|-----------------|
| Tab (inactive) | Background → `SubtleFillColorSecondaryBrush` |
| Tree row | Background → `SubtleFillColorSecondaryBrush` |
| Button (Transparent) | Background → subtle fill |
| Button (Primary) | WPF-UI Fluent built-in (slightly darker accent) |
| Menu item | Background → accent secondary |
| Icon-only button | No color change; subtle fill on hover |

### Press states
Pure Fluent defaults — a darker version of the hover background. **Never shrink**, never scale — Deskbridge is not a touch app.

### Selected states
This is where the accent colour shows up:
- **Tree row selected** → Background `SystemAccentColorSecondaryBrush` + **2px left accent stripe** (`SystemAccentColorPrimaryBrush`, 16px tall, 1px radius, 2px from left edge, vertically centered)
- **Active tab** → **2px top border** (`SystemAccentColorPrimaryBrush`) + text becomes `TextFillColorPrimaryBrush` + FontWeight SemiBold
- **Icon rail active** → **2px left border** (`SystemAccentColorPrimaryBrush`)

### Borders
- **1px hairlines** (`ControlStrokeColorDefaultBrush`) between regions: icon-rail right edge, slide-out right edge, tab-bar bottom, quick-properties top
- **Rounded corners** on cards: `ControlCornerRadius` = **4px**
- **Window corners**: 8px (Windows 11 rounded, set by FluentWindow)
- **No drop shadows on cards**. Flat-on-Mica. The only "elevation" is toasts, which use the WPF-UI Snackbar's built-in shadow.

### Shadows
- **Toast stack**: WPF-UI Snackbar default — a subtle drop shadow, ~8px blur, ~20% opacity
- **ContentDialog**: a deeper shadow + dim overlay (`Black` at 40%) behind the modal
- **Cards / panels**: **none**. Deskbridge is a flat Fluent app.

### Transparency and blur
- The window itself is translucent (Mica blurs desktop wallpaper)
- Panels on top of the Mica window have **semi-transparent fills** (that's what `ControlFillColorDefaultBrush` is — not solid grey, but ~5% white)
- **No blur inside the app**. Mica is the only blur surface.

### Corner radii
- `ControlCornerRadius`: **4px** — buttons, textboxes, cards, tree rows, menu items
- `OverlayCornerRadius`: **8px** — dialogs, the window itself
- **No pill buttons**, no fully-round corners except on the accent stripe (1px radius).

### Layout rules (fixed regions)
```
┌────────────────────────────────────────────────────────────┐
│ TitleBar (32px)                                            │
├──┬─────────┬───────────────────────────────────────────────┤
│I │ Slide-  │ Tab bar (30px)                                │
│c │ out     ├───────────────────────────────────────────────┤
│o │ panel   │                                               │
│n │ (240px) │           Viewport (RDP session host)         │
│  │         │                                               │
│R │         │                                               │
│  │         │                                               │
├──┴─────────┴───────────────────────────────────────────────┤
│ Status bar (22px, accent-colored background)               │
└────────────────────────────────────────────────────────────┘
```

### Imagery vibe
There **is no imagery** in the app — no illustrations, no photos, no hero graphics. The "image" is the live RDP session itself. When there is no session, the viewport shows a centered two-line empty state in muted greyscale. For this design system we intentionally have no hero illustrations; use screenshots of real sessions instead.

### Cards
Used only inside Settings for grouping toggles and inside Dialogs for content regions.
- Background: `ControlFillColorDefaultBrush`
- Border: 1px `ControlStrokeColorDefaultBrush`
- CornerRadius: `4px`
- Padding: `12–16px`
- No shadow.

---

## ICONOGRAPHY

**Deskbridge uses the [Fluent UI System Icons](https://github.com/microsoft/fluentui-system-icons) set exclusively**, via WPF-UI's `SymbolRegular` enum. Every icon in the app is a glyph from that set, referenced by name and size:

```xml
<ui:SymbolIcon Symbol="Home24" />
<ui:Button Icon="{ui:SymbolIcon Save24}" />
```

The number suffix is the icon's intended size. The common sizes in the app are:

- **16px** — inline icons in tree items, inside text fields, close buttons on tabs (`Dismiss16`, `Search16`)
- **24px** — menu-item icons, button icons, icon-rail icons (`Home24`, `PlugConnected24`, `Settings24`, `Desktop24`, `Folder24`)

### Icon usage in the app
| Icon | Where | Meaning |
|------|-------|---------|
| `PlugConnected24` | Icon rail | Connections panel |
| `Settings24` | Icon rail bottom | Settings panel |
| `Desktop24` | Tree items | A remote connection |
| `Folder24` / `FolderOpen24` | Tree items | A group (open when expanded) |
| `Key24` | Tree items | Group has credentials |
| `Search16` / `Search24` | Search box icon, command palette | Search |
| `Add24` | Menus, toolbars | New connection |
| `FolderAdd24` | Menus | New group |
| `Edit24` | Menus | Edit item |
| `Copy24` | Menus | Duplicate / copy hostname |
| `Delete24` | Menus | Destructive (red text) |
| `ArrowMove24` | Menus | Move to group |
| `Play24` | Connect menu | Connect |
| `Dismiss16` | Tab close | Close tab |
| `ArrowImport24` / `ArrowExportLtr24` | Settings | Import / export |
| `ArrowDownload24` | Status bar | Update available |
| `MoreHorizontal24` | Overflow | More actions |
| `Alert24` | Title bar | Notifications |
| `CheckmarkCircle24` | Toasts | Success |
| `ErrorCircle24` | Toasts | Error |
| `Warning24` | Toasts / info bars | Warning |
| `Info24` | Info bars | Information |

### Stroke / fill style
The Fluent System Icons set comes in **Regular** (outlined, ~1.5px stroke) and **Filled** variants. Deskbridge uses **Regular everywhere** — there is no filled-icon convention for selection. Selection is indicated by the accent stripe, not by a filled-in icon.

### Emoji and unicode
**Never.** No emoji, no pictographs, no box-drawing characters, no fancy arrows. The only non-alphanumeric glyphs in the UI are:
- The ellipsis (`…`) in menu item labels
- The thin space and middle dot (`·`) in the occasional status-bar string

### In this design system
- `assets/fluent-icons/` contains a small set of extracted SVGs from the Fluent UI System Icons repo for use in mockups. For the real app, always pull icons via the `SymbolRegular` enum from WPF-UI.
- The HTML preview cards and UI kit use the **Lucide** web-icon CDN as a **close substitute** (similar stroke weight, rounded, outlined). This is a substitution — flagged below.

> **Substitution flagged:** Lucide stands in for Fluent System Icons in the HTML kit. The icons are visually close but not identical. For pixel-perfect mockups, swap in the real Fluent SVGs from [microsoft/fluentui-system-icons](https://github.com/microsoft/fluentui-system-icons) or reference them from the app repo.

---

## Font substitutions (ACTION NEEDED)

Deskbridge inherits **Segoe UI Variable** from Windows 11 — there's no font file shipped in the repo. For this HTML design system we're using **Inter Variable** (via Google Fonts), which is the closest metrically-equivalent open web font. Monospace uses **JetBrains Mono** in place of Cascadia Mono.

**> Please replace** `fonts/inter.css` **and** `fonts/jetbrains-mono.css` **with Segoe UI Variable + Cascadia Mono** (or equivalent licensed web fonts) if you want the HTML kit to match the app pixel-for-pixel.

---

## How to use this system

- Start from `colors_and_type.css` — it defines every token as a CSS variable, named after the WinUI 3 token it maps to. The JSX UI kit (`ui_kits/desktop/`) consumes those variables directly.
- Treat the `MainWindow.xaml` shell as ground truth for layout. The HTML recreations in `ui_kits/desktop/` preserve the exact row/column sizes (32/36/240/30/22).
- When mocking a new feature, follow the menu/dialog templates in `src/Deskbridge/Dialogs/*.xaml` — Deskbridge's dialogs follow the Fluent `ContentDialog` pattern with a title, body, and 1–3 buttons aligned right.
