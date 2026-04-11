---
phase: 1
slug: foundation
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-04-11
---

# Phase 1 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit v3 (3.2.2) |
| **Config file** | tests/Deskbridge.Tests/Deskbridge.Tests.csproj |
| **Quick run command** | `dotnet test tests/Deskbridge.Tests/ --no-build -q` |
| **Full suite command** | `dotnet test tests/Deskbridge.Tests/` |
| **Estimated runtime** | ~5 seconds |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test tests/Deskbridge.Tests/ --no-build -q`
- **After every plan wave:** Run `dotnet test tests/Deskbridge.Tests/`
- **Before `/gsd-verify-work`:** Full suite must be green
- **Max feedback latency:** 10 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| *(populated by planner)* | | | | | | | | | |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `tests/Deskbridge.Tests/Deskbridge.Tests.csproj` — test project with xUnit v3, FluentAssertions, NSubstitute
- [ ] `tests/Deskbridge.Tests/GlobalUsings.cs` — shared test usings (Xunit, FluentAssertions, NSubstitute)

*Test infrastructure is created as part of the scaffold — Wave 0 is integrated into the solution setup.*

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| App launches and shows empty WPF window | PROJ-05 | Requires visual/GUI verification | Run app, verify window appears with dark theme |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 10s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
