---
phase: 06
slug: cross-cutting-features
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-04-15
---

# Phase 06 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

Planner fills this file as part of plan authoring. Carries over from Phase 5's xUnit v3 / NSubstitute / FluentAssertions infrastructure — no new framework installs required.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit v3 |
| **Config file** | tests/Deskbridge.Tests/Deskbridge.Tests.csproj |
| **Quick run command** | `dotnet test tests/Deskbridge.Tests/Deskbridge.Tests.csproj --filter "Category!=Slow&Category!=UAT"` |
| **Full suite command** | `dotnet test` |
| **Estimated runtime** | ~30-60s quick, ~90s full |

---

## Sampling Rate

- **After every task commit:** Run quick filter.
- **After every plan wave:** Run full suite.
- **Before `/gsd-verify-work`:** Full suite must be green.
- **Max feedback latency:** 60 seconds.

---

## Per-Task Verification Map

_Planner populates during plan authoring._

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| TBD | TBD | TBD | TBD | TBD | TBD | TBD | TBD | TBD | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

_Planner lists any prerequisite test scaffolding added before the first implementation task._

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Lock overlay visual chrome (IsFooterVisible effect) | SEC-02 | WPF visual rendering not testable headlessly | Wave 0 spike in Plan 06-04 |
| Auto-lock timer across Windows suspend/resume | SEC-03 | Requires real suspend cycle | `tests/uat/phase-06-auto-lock.md` |
| SystemEvents.SessionSwitch fires reliably | SEC-04 | Requires Windows session lock event | `tests/uat/phase-06-security.md` |
| Airspace: WindowsFormsHost hidden under lock overlay | SEC-02 | Requires live RDP session + lock trigger | `tests/uat/phase-06-security.md` |
| Crash dialog appearance + Copy Details clipboard | LOG-04 | Requires real unhandled exception | `tests/uat/phase-06-crash.md` |
| Toast visual stack (3 visible, newest top, hover pauses) | NOTF-01 | Visual timing + hover behaviour | `tests/uat/phase-06-notifications.md` |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 60s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
