---
created: 2026-04-13
source: Phase 4 execution user feedback
origin_phase: 03-connection-management
type: bug
priority: medium
---

# Editor dialog ↔ quick-properties sync

User observation during Plan 04-03 verification:
- Username/password edits in the editor dialog don't propagate back to the quick-properties panel
- Password field always appears empty on load (doesn't show starred placeholder even when a password is stored)

Likely causes:
- Missing INotifyPropertyChanged on ConnectionModel.Username/Domain setters, OR
- Quick-props is bound to a stale snapshot, not the live tree-selected model reference, OR
- Editor saves a new ConnectionModel instance to the store while quick-props still holds the original reference

Folds into Plan 04-03 polish follow-up or Phase 6 UI refinement phase.
