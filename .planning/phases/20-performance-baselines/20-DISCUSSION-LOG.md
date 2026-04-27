# Phase 20: Performance Baselines - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-04-27
**Phase:** 20-performance-baselines
**Areas discussed:** Test Data Shape, Benchmark Scope, Baseline Storage, Project Structure

---

## Test Data Shape

### Data Structure

| Option | Description | Selected |
|--------|-------------|----------|
| Enterprise-realistic | 3-level nesting, realistic hostnames, uneven distribution | ✓ |
| Flat stress test | 1-2 levels, sequential hostnames, even distribution | |
| Both modes | Generator supports shape parameter for both | |

**User's choice:** Enterprise-realistic
**Notes:** None

### Determinism

| Option | Description | Selected |
|--------|-------------|----------|
| Fixed seed | Same input count always produces identical dataset | ✓ |
| Configurable seed | Default fixed seed, can pass custom | |

**User's choice:** Fixed seed
**Notes:** None

---

## Benchmark Scope

### BuildTree Isolation

| Option | Description | Selected |
|--------|-------------|----------|
| Extract core logic | Pull tree-building into pure method in Core, benchmark directly | ✓ |
| Stub dependencies | Benchmark full ViewModel with no-op stubs | |
| Benchmark both | Extract core logic AND benchmark ViewModel path | |

**User's choice:** Extract core logic
**Notes:** None

### Operations Beyond Core Four

| Option | Description | Selected |
|--------|-------------|----------|
| SaveBatch | Batch write path from Phase 19 | ✓ |
| DeleteBatch | Batch delete path for Phase 23 | ✓ |
| GetByFilter | Filter queries for command palette and search | ✓ |
| Just the four listed | Stick to BuildTree, Search, Load, Save only | ✓ |

**User's choice:** SaveBatch, DeleteBatch, GetByFilter (all three extras selected alongside core four)
**Notes:** User selected all options including "Just the four listed" — interpreted as including all extras

---

## Baseline Storage

### Storage Approach

| Option | Description | Selected |
|--------|-------------|----------|
| BenchmarkDotNet exports | Built-in JSON/CSV exporters, results in Artifacts/ | ✓ |
| Custom baseline file | Custom JSON with threshold comparison | |
| No git tracking | Local-only artifacts, manual comparison | |

**User's choice:** BenchmarkDotNet exports
**Notes:** None

### Git Tracking

| Option | Description | Selected |
|--------|-------------|----------|
| Track initial baseline only | Commit first run, .gitignore subsequent | ✓ |
| Always .gitignore | Artifacts are ephemeral | |

**User's choice:** Track initial baseline only
**Notes:** None

---

## Project Structure

### Generator Location

| Option | Description | Selected |
|--------|-------------|----------|
| Shared in Deskbridge.Core | Reusable by Benchmarks and Tests | ✓ |
| Inside Benchmarks only | Generator only in benchmark project | |
| New TestUtils project | Dedicated shared project | |

**User's choice:** Shared in Deskbridge.Core
**Notes:** None

### Project Location

| Option | Description | Selected |
|--------|-------------|----------|
| tests/Deskbridge.Benchmarks | Alongside Deskbridge.Tests | ✓ |
| benchmarks/Deskbridge.Benchmarks | Separate top-level folder | |

**User's choice:** tests/Deskbridge.Benchmarks
**Notes:** None

---

## Claude's Discretion

- BenchmarkDotNet configuration details (warmup, iteration count, exporters)
- Test data generator class name and API
- Benchmark class organization
- Hostname/group naming patterns

## Deferred Ideas

None
