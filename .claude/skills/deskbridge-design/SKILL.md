---
name: deskbridge-design
description: Use this skill to generate well-branded interfaces and assets for Deskbridge (a Windows RDP connection manager), either for production or throwaway prototypes/mocks. Contains essential design guidelines, Windows 11 Fluent color tokens, Segoe UI Variable type specs, Fluent System Icons references, and a React UI kit recreating the app shell.
user-invocable: true
---

Read `README.md` within this skill and explore the other available files:

- `README.md` — brand overview, content fundamentals, visual foundations, iconography
- `colors_and_type.css` — all Windows 11 Fluent tokens as CSS variables (text, fills, strokes, accent, semantic brushes, type ramp, spacing, radii, chrome sizes)
- `fonts/` — web font stand-ins (Inter for Segoe UI Variable; JetBrains Mono for Cascadia Mono)
- `assets/` — logo mark, wordmark
- `preview/` — one small card per token/component (colors, type, spacing, buttons, tabs, tree, toasts, menu, palette, dialog, iconography)
- `ui_kits/desktop/` — React UI kit: `index.html` + JSX for every shell component, with a clickable prototype

Design rules to internalize:

- Dark-first Windows 11 Fluent. Mica backdrop. 4px control radius, 8px overlay radius.
- Colours come from WinUI 3 tokens (`--text-primary`, `--control-fill-default`, etc.) — never hard-coded. Only three custom semantic hexes: error `#F44747`, warning `#FFCC02`, success `#89D185`.
- Accent is the OS system accent. It appears in exactly three places: status-bar background, 2px left stripe on selected rows, 2px top border on active tab.
- Type: Segoe UI Variable, WinUI ramp (11 uppercase section / 12 caption / 14 body / 16 card title / 20 subtitle / 28 dialog). Weights 400 and 600 only.
- Icons: Fluent UI System Icons, Regular variant. Lucide is a substitution when Fluent is unavailable — flag it.
- Copy is terse, Title Case on actions, UPPERCASE on section labels, sentence case in dialog bodies. Never emoji, never "we/you". `…` on any action opening a follow-up.
- Spacing: 4px grid. Fixed chrome sizes: TitleBar 32, icon rail 36, panel 240, tabs 30, status 22, row 28.
- No gradients, no illustrations, no shadows on cards. Toasts and dialogs get shadows; everything else is flat on Mica.

If creating visual artifacts (slides, mocks, throwaway prototypes): copy `colors_and_type.css` and the relevant UI-kit components, reference the assets directly, and build static HTML. If working on production Deskbridge code: read the design rules here, use `DynamicResource` for every brush, and base every override on `DefaultUi<Control>Style`.

If the user invokes this skill without other guidance, ask what they want to build or design, ask some questions, and act as an expert Deskbridge designer producing HTML artifacts or production-ready XAML.
