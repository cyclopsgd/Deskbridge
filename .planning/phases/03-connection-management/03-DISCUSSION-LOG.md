# Phase 3: Connection Management - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-04-11
**Phase:** 3-Connection Management
**Areas discussed:** JSON structure, TreeView approach, Editor dialog, Credential flow

---

## JSON Structure

### Layout
| Option | Description | Selected |
|--------|-------------|----------|
| Flat arrays (Recommended) | Two arrays: connections and groups, GroupId references | ✓ |
| Nested tree | Groups contain children | |
| You decide | | |

### Schema versioning
| Option | Description | Selected |
|--------|-------------|----------|
| Yes, version field | Top-level "version": 1 | ✓ |
| No, defer | Add when needed | |

### Atomic writes
| Option | Description | Selected |
|--------|-------------|----------|
| Yes, temp+rename | Write to .tmp, File.Move overwrite | ✓ |
| You decide | | |

### Dev path
| Option | Description | Selected |
|--------|-------------|----------|
| %AppData% always | Same in dev and prod | |
| Local in debug | ./data/ for Debug config | |
| You decide | Claude picks | ✓ |

---

## TreeView Approach

### Tree control
| Option | Description | Selected |
|--------|-------------|----------|
| WPF TreeView (Recommended) | Auto-styled by WPF-UI, HierarchicalDataTemplate | ✓ |
| Custom ItemsControl tree | Build from scratch | |

### Drag-drop
| Option | Description | Selected |
|--------|-------------|----------|
| Full drag-drop | Between groups, reorder, visual indicator | |
| Move via context menu | Right-click Move to... submenu | |
| Both | Drag-drop AND context menu move | ✓ |

### Context menu
| Option | Description | Selected |
|--------|-------------|----------|
| Standard set | Connect, Edit, Delete, Rename, Move to..., New Connection, New Group | ✓ |
| Copy hostname | Right-click → Copy Hostname to clipboard | ✓ |
| Duplicate | Creates copy with (Copy) suffix | ✓ |

**User note:** Also requested inline edit box at bottom like mRemoteNG.

### Quick properties panel
| Option | Description | Selected |
|--------|-------------|----------|
| Quick properties panel | Collapsible panel below tree, key fields for inline editing | ✓ |
| Just hostname/port | Minimal | |
| No inline edit | Full editor only | |

### Multi-select
| Option | Description | Selected |
|--------|-------------|----------|
| No, single select | Standard TreeView | |
| Yes, multi-select | Ctrl+click, Shift+click for bulk operations | ✓ |

---

## Editor Dialog

### Type
| Option | Description | Selected |
|--------|-------------|----------|
| WPF-UI ContentDialog | Modal overlay inside main window | ✓ |
| Separate Window | New FluentWindow as modal | |

### Tabs
| Option | Description | Selected |
|--------|-------------|----------|
| Keep as specified | General, Credentials, Display, Notes | ✓ |
| Adjust tabs | Different arrangement | |

### Group editor
**User's choice:** Both — inline quick edit in properties panel + right-click Edit opens ContentDialog with full view.
**Notes:** User suggested this dual approach for efficiency.

### Inheritance indicator
| Option | Description | Selected |
|--------|-------------|----------|
| Info bar + disabled fields | WPF-UI InfoBar: "Credentials inherited from [Group]", fields grayed | ✓ |
| Text label only | Simple TextBlock, no fields until switched to Own | |

---

## Credential Flow

### Prompt timing
| Option | Description | Selected |
|--------|-------------|----------|
| At connect time | Pipeline's ResolveCredentialsStage (Phase 4/5). Phase 3 stores mode only. | ✓ |
| At edit time | User enters in editor, stored in memory | |

### Implementation
| Option | Description | Selected |
|--------|-------------|----------|
| Full implementation | Wire AdysTech.CredentialManager for real Store/Get/Delete | ✓ |
| Interface + stub | ICredentialService interface only, in-memory stub | |

---

## Claude's Discretion

- Dev path for connections.json
- Quick properties panel layout details
- Drag-drop visual indicator style
- Multi-select implementation pattern
- Search filter behavior in tree

## Deferred Ideas

None
