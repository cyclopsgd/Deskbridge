---
phase: 06-cross-cutting-features
created: 2026-04-15
status: ready_for_research
requirement_ids:
  - CMD-01
  - CMD-02
  - CMD-03
  - CMD-04
  - NOTF-01
  - NOTF-02
  - NOTF-03
  - NOTF-04
  - LOG-01
  - LOG-02
  - LOG-03
  - LOG-04
  - LOG-05
  - SEC-01
  - SEC-02
  - SEC-03
  - SEC-04
  - SEC-05
---

# Phase 6 Context — Cross-Cutting Features

## Scope Recap

Phase 6 delivers four orthogonal feature groups that all consume the event bus:

| Group | Requirements | Deliverable |
|---|---|---|
| Command Palette | CMD-01..04 | Ctrl+Shift+P floating search + command list + Ctrl+N/Ctrl+T/Ctrl+W/F11/Esc |
| Notifications | NOTF-01..04 | Bottom-right toast stack + window-state persistence |
| Logging & Audit | LOG-01..05 | Serilog rolling files + audit.jsonl + global exception handler |
| App Security | SEC-01..05 | PBKDF2 master password + lock overlay + auto-lock timer |

**Goal (verbatim from ROADMAP):** Users have keyboard-first workflows, visual feedback, operational visibility, and security controls — all features that consume the event bus.

## Canonical Refs (read before research/planning)

- `REFERENCE.md` — architecture, DI, disposal sequences
- `DESIGN.md` — WPF-UI patterns, colour tokens, layout
- `WPF-UI-PITFALLS.md` — 8 categories of silent failures (ContentDialog hosting critical for palette + crash dialog + lock overlay)
- `WPF-TREEVIEW-PATTERNS.md` — only relevant if palette reuses TreeView; palette likely uses ListBox
- `.planning/PROJECT.md` — core value, evolution rules
- `.planning/REQUIREMENTS.md` — acceptance criteria for CMD/NOTF/LOG/SEC IDs
- `.planning/phases/01-foundation/01-SUMMARY.md` — IEventBus, INotificationService, IConnectionQuery surface
- `.planning/phases/03-connection-management/03-SUMMARY.md` — ConnectionQueryService dual-score (100/80/60 + 40/30)
- `.planning/phases/05-tab-management/05-SUMMARY.md` (01/02/03) — TabHostManager, TabSwitchedEvent, snackbar service

## Decisions

### Command Palette

**D-01 Layout:** VS Code-style centered floating — 480×auto, `ContentDialog` host pattern, dim backdrop over the whole window. Anchored near top. NOT a title-bar drop-down.

**D-02 Empty state (no query typed):** Top 5 recent connections (sorted by `LastUsedAt` descending) followed by command entries. No placeholder hint text.

**D-03 Fuzzy ranking:** Reuse `IConnectionQuery.Search()` dual-score unchanged (100/80/60 substring + 40/30 subsequence). Commands use the same scorer over `{name, aliases, group}`.

**D-04 Commands list (CMD-02):** New Connection · Settings · Disconnect All · Quick Connect. Keyboard-bind `Ctrl+N` → New Connection, `Ctrl+T` → Quick Connect. Ctrl+W (CMD-04) already wired in Phase 5; F11/Esc fullscreen covered below.

**D-05 F11/Esc fullscreen:** F11 toggles app fullscreen (WPF `WindowState.Maximized` + `WindowStyle.None`). Esc exits fullscreen. Scope note: this is APP fullscreen, not RDP session fullscreen — the latter is AxHost-controlled in Phase 4.

### Notifications

**D-06 Toast severity mapping:**

| Event | Severity | Duration |
|---|---|---|
| connected | info | 2s auto |
| disconnected | info | 3s auto |
| reconnecting | warning | sticky until resolved or manually closed |
| failed | error | sticky until dismissed |
| updates-available | info | sticky |
| audit events | — | log-only, no toast |

**D-07 Stack behaviour:** Max 3 visible simultaneously. Newest on top. 4th toast evicts oldest. Hover pauses auto-dismiss (WPF-UI Snackbar default).

**D-08 No modals for non-critical (NOTF-02):** Only the crash dialog (D-11) and the lock overlay (D-16) are modal. All other user feedback is toasts.

**D-09 Window state persistence (NOTF-04) — own plan:** Save `{x, y, width, height, isMaximized, sidebarOpen, sidebarWidth}` to `%AppData%/Deskbridge/settings.json` on `Window.Closing`. Load on `Window.SourceInitialized` with fallback defaults. `System.Text.Json` source-generated, no Newtonsoft.

### Logging & Audit

**D-10 Audit schema (per jsonl line):**
```json
{"ts":"<ISO UTC>","type":"<EventType>","connectionId":"<guid|null>","user":"<WindowsUsername>","outcome":"<success|fail>","errorCode":"<optional>"}
```
No IPs, no durations, no source fields — defer richer fields to v1.1.

**D-11 Global exception handler:**
- **Recoverable** (per-connection, already caught in pipeline): error toast + log line, app keeps running.
- **Unhandled** (`AppDomain.UnhandledException` + `Dispatcher.UnhandledException` + `TaskScheduler.UnobservedTaskException`): minimal crash dialog — "Deskbridge encountered an unexpected error" + [Copy Details] + [Restart] buttons. No stack trace visible to user. Full trace appended to log.

**D-12 No credentials in logs (LOG-05):** Hard rule. Password fields in any object must have `[JsonIgnore]` / never be interpolated into format strings. Serilog destructuring policy rejects any property named `Password`, `Secret`, `CredentialData`, `Token`. Unit test: `assert no plaintext password appears in a full-run log sample`.

**D-13 Audit rotation:** Monthly (per LOG-02). File name pattern `audit-YYYY-MM.jsonl`. Append-only — no edits, no deletes. Old months retained (no size cap in v1).

### App Security

**D-14 Auto-lock trigger:** Deskbridge activity only — `Application.MouseDown` / `Application.KeyDown` / any WPF input event inside the main window resets the inactivity timer. System-wide idle is NOT checked (rationale: user reading docs externally with Deskbridge visible should not lock). EXCEPT: `SystemEvents.SessionSwitch` (Windows lock / user switch) DOES trigger immediate lock (SEC-04).

**D-15 Lock overlay visual:** Full-window opaque (uses `DeskbridgeBackgroundBrush`), centered password card (fixed width ~360px). NOT dim+blur — security-sensitive apps should fully hide remote desktop pixels.

**D-16 Password recovery:** NONE. Forgotten master password = delete `%AppData%/Deskbridge/auth.json` and reset settings. This is the documented recovery path. Rationale: any recovery mechanism weakens PBKDF2 to the strength of the recovery channel; for a desktop app with local attacker access there's no trusted out-of-band channel.

**D-17 Master-password ↔ credential access — INDEPENDENT.** Master password gates the Deskbridge UI (connection tree, settings, tab switching, palette). Windows Credential Manager is OS-level and always accessible to the logged-in Windows user; connection secrets are read on-demand from WCM after unlock. No double-prompt.

**Attack-model note (for research):** if an attacker bypasses the Deskbridge lock they can only see connection METADATA (hosts, groups, history), not secrets — secrets require Windows user session access, which is outside Deskbridge's trust boundary. Document this in the phase's threat model.

**D-18 Ctrl+L manual lock (SEC-04):** Registered at `MainWindow.PreviewKeyDown` level (same pattern as Phase 5 tab shortcuts). Idempotent — pressing again when already locked is a no-op.

**D-19 Lock-on-minimise (SEC-05):** Configurable in Settings, default OFF. When ON: `Window.StateChanged` → if `WindowState == Minimized`, lock immediately.

### Phase Sequencing

**D-20 Plan order (4 plans, 4 waves):**

| Plan | Group | Wave | Why this order |
|---|---|---|---|
| 06-01 | Logging & Audit | 1 | Everyone else's observability foundation. No UI, no deps. |
| 06-02 | Notifications + Window State | 2 | Consumes event bus, uses logging infrastructure |
| 06-03 | Command Palette + Shortcuts | 3 | Isolated feature, no dependencies on security |
| 06-04 | App Security | 4 | Heaviest surface (PBKDF2, overlay, timer, session hook). No downstream deps in Phase 6. Last so everything else is testable without lock friction. |

Rationale for not parallelising: each plan touches MainWindow.xaml (toast host, palette host, lock overlay host, window-state bindings) — serialised file avoids intra-wave `files_modified` conflicts.

## Specifics Worth Preserving

- **Palette reuses IConnectionQuery** — don't invent a second fuzzy scorer. CMD-03 says so explicitly.
- **No Newtonsoft.Json anywhere** — project constraint, settings.json + audit.jsonl use `System.Text.Json` with source generators.
- **WPF-UI Snackbar vs ContentDialog** — Snackbars are fire-and-forget non-blocking (D-06/07). ContentDialog is modal and used for crash dialog (D-11) and lock overlay (D-16).
- **Audit.jsonl is append-only** — no log rotation utility edits old entries. Monthly file roll, old months preserved.
- **BinaryFormatter removed in .NET 10** — audit schema must be JSON (already decided), no binary format fallback.

## Deferred Ideas (out of Phase 6 scope)

- Per-connection audit filtering / export UI — v1.1 or Phase 7
- Session recording — explicitly `Out of Scope` for v1 (REQUIREMENTS.md)
- Remote log shipping (syslog, Seq) — v1.1+
- Biometric unlock (Windows Hello) — v1.2 Enterprise
- Multiple master-password profiles / shared-vault mode — not in v1
- Richer audit fields (IPs, durations, source host) — v1.1
- Toast grouping / do-not-disturb mode — v1.1
- Command palette extension API / user-defined commands — not in v1

## Open Questions for Research

1. **Serilog .Destructure.ByTransforming<Credential>** — confirm correct API shape in Serilog 4.3.1 for stripping password properties before formatting.
2. **PBKDF2 parameters** — iteration count (OWASP 2026 recommendation for SHA-256), salt length, output length. Research should cite current guidance.
3. **SystemEvents.SessionSwitch lifetime** — does the event subscription need a strong reference to prevent GC, and does it fire reliably on modern Windows (server vs workstation SKUs)?
4. **WPF-UI ContentDialog vs custom overlay for lock screen** — ContentDialog's Primary/Secondary buttons may not fit the "password only" model. Research whether an ApplicationBackdropType override + custom UserControl is cleaner.
5. **File.AppendAllTextAsync + concurrent writers** — audit.jsonl append-only semantics under multi-threaded event bus dispatch. Need a serialised writer or System.IO.File lock strategy.

## Exit Criteria

Research (`/gsd-research-phase 6`) should produce a RESEARCH.md covering:
- Serilog destructuring for credentials
- PBKDF2 parameter recommendations (2026)
- `SystemEvents.SessionSwitch` usage + pitfalls
- Audit.jsonl concurrent-write strategy
- ContentDialog vs custom UserControl for lock overlay
- Timer/DispatcherTimer pattern for auto-lock (must survive suspend/resume)
- CommandPaletteViewModel fuzzy-match performance with large connection lists (>500)

Planning (`/gsd-plan-phase 6`) produces 4 plans as sequenced in D-20. Each plan's `autonomous: true` except 06-04 (Security) which needs UAT for lock overlay correctness and auto-lock timing.
