---
phase: 18
slug: settings-infrastructure
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-04-26
---

# Phase 18 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | MSTest / .NET 10 test host |
| **Config file** | `tests/Deskbridge.Tests/Deskbridge.Tests.csproj` |
| **Quick run command** | `dotnet test tests/Deskbridge.Tests --no-build --filter "Category=Settings"` |
| **Full suite command** | `dotnet test tests/Deskbridge.Tests` |
| **Estimated runtime** | ~15 seconds |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test tests/Deskbridge.Tests --no-build --filter "Category=Settings"`
- **After every plan wave:** Run `dotnet test tests/Deskbridge.Tests`
- **Before `/gsd-verify-work`:** Full suite must be green
- **Max feedback latency:** 15 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Threat Ref | Secure Behavior | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|------------|-----------------|-----------|-------------------|-------------|--------|
| 18-01-01 | 01 | 1 | SET-01 | — | N/A | manual | Visual: settings panel shows sections | — | ⬜ pending |
| 18-01-02 | 01 | 1 | SET-02 | — | N/A | unit | `dotnet test --filter "BulkOperationsRecord"` | ❌ W0 | ⬜ pending |
| 18-01-03 | 01 | 1 | SET-03 | — | N/A | unit | `dotnet test --filter "UninstallRecord"` | ❌ W0 | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] Unit tests for `BulkOperationsRecord` defaults and serialization
- [ ] Unit tests for `UninstallRecord` defaults and serialization
- [ ] Verify `AppSettingsContext` includes new record types in JSON source gen

*Existing infrastructure covers framework setup — only new test files needed.*

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Settings panel shows all 5 sections in correct order | SET-01 | Visual layout verification | Open Settings, verify Appearance → Security → Bulk Operations → Data → Uninstall order |
| GDI threshold NumberBox respects 5-30 range | SET-02 | WPF control interaction | Enter values outside range, verify clamping |
| Uninstall toggle persists across restarts | SET-03 | Requires app restart cycle | Toggle on, close app, reopen, verify state |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 15s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
