---
created: 2026-04-12
source: Phase 4 execution user feedback
origin_phase: 03-connection-management
type: ui-polish
---

# Quick properties UI polish

Surfaced during Plan 04-02 live verification. Two small tweaks:

1. **Password field: show starred placeholder when password is stored.**
   Currently blank on load whether a password is saved or not — user cannot tell at a glance if credentials are set.
   Suggested: populate with `********` (or equivalent masked glyphs) when `ICredentialService.GetForConnection` returns a credential. Cleared on focus / overwrite.
   Requires care: never render the actual password length or characters.

2. **Quick-properties Username / Password / Domain row spacing.**
   These three boxes are flush/touching in the current layout. Other quick-properties rows have consistent vertical spacing. Match that — likely a `Margin` adjustment in the DataTemplate / StackPanel in `ConnectionTreeControl.xaml` or the equivalent quick-properties view.

Both are Phase 3 polish, not blocking Phase 4. Recommend folding into Plan 04-03 (reconnection + polish pass) or Phase 6 (UI phase) depending on scope.
