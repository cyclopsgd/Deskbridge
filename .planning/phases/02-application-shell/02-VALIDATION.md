---
phase: 2
slug: application-shell
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-04-11
---

# Phase 2 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit v3 (3.2.2) — existing from Phase 1 |
| **Config file** | tests/Deskbridge.Tests/Deskbridge.Tests.csproj |
| **Quick run command** | `dotnet test tests/Deskbridge.Tests/ --no-build -q` |
| **Full suite command** | `dotnet test tests/Deskbridge.Tests/` |
| **Estimated runtime** | ~8 seconds |

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

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- Existing test infrastructure from Phase 1 covers framework setup
- Phase 2 is primarily visual — most verification is build + visual inspection

*Existing infrastructure covers framework requirements.*

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Dark Fluent window with Mica backdrop | SHEL-01 | Requires display session | Launch app, verify dark theme and Mica transparency |
| Icon rail toggles slide-out panel | SHEL-03, SHEL-04 | Requires mouse/keyboard interaction | Click each icon, verify panel opens/closes/switches |
| Tab bar scroll overflow | SHEL-05 | Requires multiple placeholder tabs | Add 10+ placeholder tabs, verify horizontal scroll |
| Viewport fills remaining space (airspace-safe) | SHEL-07 | Visual verification only | Resize window, verify no overlapping elements |
| Custom accent colours applied | SHEL-08 | Requires visual colour comparison | Check status bar (#007ACC), active indicators |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 15s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
