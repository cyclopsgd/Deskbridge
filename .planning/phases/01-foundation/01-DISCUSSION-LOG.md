# Phase 1: Foundation - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-04-11
**Phase:** 1-Foundation
**Areas discussed:** Test infrastructure, Disconnect pipeline, Solution conventions, NuGet strategy

---

## Test Infrastructure

| Option | Description | Selected |
|--------|-------------|----------|
| Yes, xUnit project | Add Deskbridge.Tests with xUnit + FluentAssertions. Write basic tests for event bus pub/sub and pipeline stage ordering. | ✓ |
| Yes, minimal | Add Deskbridge.Tests with xUnit only. Empty project, tests added as features land. | |
| No, defer tests | No test project in Phase 1. Add when there's meaningful behavior to test. | |

**User's choice:** Yes, xUnit project
**Notes:** None

### Follow-up: Test layout

| Option | Description | Selected |
|--------|-------------|----------|
| tests/Deskbridge.Tests/ | Separate tests/ folder at repo root (standard .NET convention) | ✓ |
| src/Deskbridge.Tests/ | Keep alongside source projects under src/ | |

**User's choice:** tests/Deskbridge.Tests/

### Follow-up: Mocking library

| Option | Description | Selected |
|--------|-------------|----------|
| Yes, NSubstitute | Clean syntax, works well with DI-heavy codebases | ✓ |
| No mocking lib | Use hand-written fakes/stubs | |

**User's choice:** Yes, NSubstitute

### Follow-up: Test scope

| Option | Description | Selected |
|--------|-------------|----------|
| Event bus pub/sub | Publish event, verify subscriber receives it. Weak reference cleanup. | ✓ |
| Pipeline stage ordering | Register stages with different Order values, verify execution order. | ✓ |
| Connection query fuzzy search | Test fuzzy matching against in-memory dataset. | ✓ |
| DI composition | Verify all services resolve from the container without throwing. | ✓ |

**User's choice:** All four selected

---

## Disconnect Pipeline

| Option | Description | Selected |
|--------|-------------|----------|
| Yes, add now | Define IDisconnectPipeline with stages. Symmetry with connect pipeline. Future-proof. | ✓ |
| No, keep simple | Disconnect stays a sequential method as REFERENCE.md specifies. | |
| You decide | Claude's discretion based on implementation feel. | |

**User's choice:** Yes, add now
**Notes:** Research flagged this gap. Adding now avoids retrofitting later.

---

## Solution Conventions

### Code style

| Option | Description | Selected |
|--------|-------------|----------|
| Full setup | .editorconfig with C# conventions, nullable warnings as errors, code analysis | ✓ |
| Minimal | .editorconfig with basics only. Skip code analysis rules. | |
| You decide | Claude picks what makes sense | |

**User's choice:** Full setup

### Global usings

| Option | Description | Selected |
|--------|-------------|----------|
| Yes, shared global usings | Common namespaces in GlobalUsings.cs per project | ✓ |
| No, implicit usings only | Rely on .NET's built-in implicit usings | |

**User's choice:** Yes, shared global usings

---

## NuGet Strategy

### Package management

| Option | Description | Selected |
|--------|-------------|----------|
| Central Package Management | Directory.Packages.props at repo root. All versions in one file. | ✓ |
| Per-project versions | Each .csproj specifies its own package versions. | |

**User's choice:** Central Package Management

### Version pinning

| Option | Description | Selected |
|--------|-------------|----------|
| Pin exact versions | e.g. Version="4.2.0" — fully reproducible builds | |
| Pin major.minor | e.g. Version="4.2.*" — auto-picks patch updates | ✓ |
| Pin to minimum | e.g. Version="4.2.0" with no upper bound | |

**User's choice:** Pin major.minor

### Licensing (user-initiated)

| Option | Description | Selected |
|--------|-------------|----------|
| Yes, MIT | Standard permissive open-source license | ✓ |
| Different license | User specifies a different license | |

**User's choice:** Yes, MIT
**Notes:** User proactively raised licensing during NuGet discussion.

---

## Claude's Discretion

- Disconnect pipeline stage names, ordering, and interface shape
- GlobalUsings.cs contents per project
- .editorconfig rule severity levels

## Deferred Ideas

None — discussion stayed within phase scope
