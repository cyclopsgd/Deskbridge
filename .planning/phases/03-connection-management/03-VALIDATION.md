---
phase: 3
slug: connection-management
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-04-11
---

# Phase 3 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit v3 (3.2.2) — existing from Phase 1 |
| **Config file** | tests/Deskbridge.Tests/Deskbridge.Tests.csproj |
| **Quick run command** | `dotnet test tests/Deskbridge.Tests/ --no-build -q` |
| **Full suite command** | `dotnet test tests/Deskbridge.Tests/` |
| **Estimated runtime** | ~15 seconds |

---

## Sampling Rate

- **After every task commit:** Run `dotnet build Deskbridge.sln --no-restore -q`
- **After every plan wave:** Run `dotnet test tests/Deskbridge.Tests/`
- **Before `/gsd-verify-work`:** Full suite must be green + visual inspection
- **Max feedback latency:** 15 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| *(populated by planner)* | | | | | | | | | |

---

## Wave 0 Requirements

- Existing test infrastructure from Phase 1 covers framework setup
- Phase 3 needs tests for: JsonConnectionStore, CredentialService, TreeView ViewModels

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| TreeView drag-drop between groups | CONN-04 | Requires mouse interaction | Drag connection to different group, verify it moves |
| F2 inline rename | CONN-04 | Requires keyboard interaction | Select item, press F2, type new name, press Enter |
| Connection editor dialog tabs | CONN-05 | Visual layout verification | Open editor, check all 4 tabs render correctly |
| Credential inheritance indicator | CONN-09 | Visual verification | Set group credentials, check child shows "inherited from" InfoBar |
| Group key icon | CONN-10 | Visual verification | Set credentials on group, verify key icon appears |
| Quick properties panel | CONN-04 | Requires interaction | Select connection, verify fields appear below tree |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 15s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
