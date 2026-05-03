---
phase: 22
plan: 03
subsystem: large-import-handling
tags: [mremoteng, serializer, reverse-mapper, round-trip, stress-fixtures, pathological]
dependency_graph:
  requires:
    - "src/Deskbridge.Core/Services/MRemoteNGImporter.cs (parser — schema authority)"
    - "src/Deskbridge.Core/Services/TestDataGenerator.cs (Phase 20 deterministic input)"
    - "tests/Deskbridge.Tests/Fixtures/sample-confcons.xml (schema authority for root attrs)"
  provides:
    - "MRemoteNGXmlSerializer (D-08 reverse-mapper) — produces parseable mRemoteNG XML at scale"
    - "4 committed pathological fixtures (D-10) — deep-nest, unicode-mixed, malformed-single-row, large-empty-groups"
    - "tests/Deskbridge.Tests/Fixtures/large/ + .csproj Content glob → fixtures copy to bin"
  affects:
    - "Plan 22-04 (stress + pathological tests) — round-trip generator + 4 inputs are now in place"
tech-stack:
  added: []
  patterns:
    - "Static reverse-mapper (Pattern H) — copy of ConnectionExporter shape: GroupBy/ToDictionary parent-id lookup, recursive WriteGroupNode/WriteConnectionNode"
    - "XmlWriter + UTF8Encoding(emitBom: false) for BOM-less UTF-8 (RESEARCH P10)"
    - "xUnit v3 [Theory] + [InlineData] size sweep at 500/1000/5000 (Pattern F)"
    - "Fixture-stream loader via Path.Combine(AppContext.BaseDirectory, ...) (Pattern G)"
key-files:
  created:
    - "src/Deskbridge.Core/Services/MRemoteNGXmlSerializer.cs (157 lines)"
    - "tests/Deskbridge.Tests/Services/MRemoteNGXmlSerializerRoundTripTests.cs (319 lines, 17 tests)"
    - "tests/Deskbridge.Tests/Fixtures/large/deep-nest-7-levels.xml (2.4 KB)"
    - "tests/Deskbridge.Tests/Fixtures/large/unicode-mixed.xml (4.5 KB)"
    - "tests/Deskbridge.Tests/Fixtures/large/malformed-single-row.xml (24.1 KB)"
    - "tests/Deskbridge.Tests/Fixtures/large/large-empty-groups.xml (10.6 KB)"
  modified:
    - "tests/Deskbridge.Tests/Deskbridge.Tests.csproj (Content glob extended with Fixtures\\large\\*.xml)"
decisions:
  - "malformed-single-row.xml uses Port=\"abc\" (semantically-invalid but structurally-valid XML) per PATTERNS.md correction #3 — XDocument.Load must succeed; the parser tolerates the bad port via int.TryParse fallback to 3389; 22-04 will assert the executor's continue-and-collect path on top of parser tolerance."
  - "Password attribute emitted as empty string (NOT omitted) for fixture realism — round-trip test asserts empty-or-omitted, both branches acceptable per MIG-03."
  - "Port written via InvariantCulture so en-US runner locales and de-DE locales emit identical bytes."
  - "Fixtures stored under tests/Deskbridge.Tests/Fixtures/large/ subdirectory; .csproj uses an explicit Fixtures\\large\\*.xml glob (existing sample-confcons.xml entry is non-glob, kept as-is)."
  - "Group + Connection ordering by SortOrder preserved during reverse-mapping so deterministic round-trips remain byte-stable across runs."
metrics:
  duration_minutes: 6
  completed_date: "2026-05-03"
  tasks: 2
  commits: 2
  files_created: 6
  files_modified: 1
  tests_added: 17
  total_tests_after: 723
  total_tests_before: 706
---

# Phase 22 Plan 03: MRemoteNGXmlSerializer Reverse-Mapper + Pathological Fixtures Summary

**One-liner:** Reverse-mapper (`MRemoteNGXmlSerializer`) plus four committed pathological fixtures and 17 round-trip + smoke tests, validating that `TestDataGenerator(5000) → Serialize → MRemoteNGImporter.ParseAsync` preserves connection count, group count, and 3-level hierarchy in under 5 seconds.

## What Shipped

### Reverse-mapper (D-08)

`src/Deskbridge.Core/Services/MRemoteNGXmlSerializer.cs` — static class, ~157 lines. Mirrors the existing `ConnectionExporter` shape: `GroupBy → ToDictionary` parent-id lookup, recursive `WriteGroupNode` / `WriteConnectionNode` helpers, no DI, callable from any thread.

Critical correctness invariants encoded in the writer:

| Invariant | Why | Encoded as |
|---|---|---|
| Root `FullFileEncryption="false"` (lowercase) | Parser at `MRemoteNGImporter.cs:40` throws `ImportException` on any uppercase value | Hardcoded `WriteAttributeString("FullFileEncryption", "false")` with comment |
| UTF-8 no BOM | Match `sample-confcons.xml`'s encoding declaration; `XmlReader` strips BOMs but byte-shape readability matters | `Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)` |
| `Type` is case-sensitive ("Container"/"Connection") | Parser at `MRemoteNGImporter.cs:65` does exact-string match | Hardcoded literals |
| `Password` always empty | MIG-03 — never emit a real value, even though parser ignores | Hardcoded empty string with MIG-03 comment |
| `Port` invariant-culture | Locale-stable bytes across en-US/de-DE/etc. | `c.Port.ToString(CultureInfo.InvariantCulture)` |
| `Protocol` mapped to wire | Rdp→RDP / Ssh→SSH2 / Vnc→VNC | `ProtocolToWire` switch |

### Pathological fixtures (D-10) — 4 committed XML files under `tests/Deskbridge.Tests/Fixtures/large/`

| Fixture | Size | Structure |
|---|---:|---|
| `deep-nest-7-levels.xml` | 2.4 KB | 7 levels of `<Node Type="Container">` nesting + 1 terminal `<Node Type="Connection">` at the bottom |
| `unicode-mixed.xml` | 4.5 KB | 1 Cyrillic Container + 15 Connections covering Cyrillic, CJK, RTL Arabic, emoji, combining marks, Greek polytonic, Latin diacritics, plus 5 plain-ASCII control rows |
| `malformed-single-row.xml` | 24.1 KB | 1 wrapper Container + 100 Connections; row 100 is `BadRow` with `Port="abc"`. **Structurally valid XML**, semantically invalid row only — PATTERNS.md correction #3 |
| `large-empty-groups.xml` | 10.6 KB | 30 top-level empty Containers + 1 wrapper with 19 nested empty Containers = 50 Containers, 0 Connections |

All four fixtures: UTF-8 no BOM (verified `head -c 3 → 3c 3f 78` not `ef bb bf`), `FullFileEncryption="false"` on root.

### Round-trip + smoke tests (17 tests)

`tests/Deskbridge.Tests/Services/MRemoteNGXmlSerializerRoundTripTests.cs` — 319 lines, `[Trait("Category", "MRemoteNGXmlSerializerRoundTrip")]`, `sealed class`, xUnit v3 idioms.

| # | Test | What it asserts |
|---:|---|---|
| 1 | `Serialize_Produces_Utf8_NoBom` | First 3 bytes are not `EF BB BF` |
| 2 | `Serialize_Root_FullFileEncryption_IsFalse` | Root attribute is the literal lowercase `"false"` |
| 3 | `RoundTrip_500_PreservesConnectionCount` | Generate(500) → Serialize → ParseAsync ⇒ TotalConnections == 500 |
| 4 | `RoundTrip_1000_PreservesConnectionCount` | Same at 1000 |
| 5 | `RoundTrip_5000_PreservesConnectionCount` | Same at 5000 (~3 MB output, no OOM) |
| 6 | `RoundTrip_PreservesGroupCount` | TotalFolders == groups.Count after round-trip |
| 7 | `RoundTrip_PreservesGroupHierarchy` | Maximum container depth ≥ 3 (TestDataGenerator emits 3 levels) |
| 8 | `RoundTrip_PreservesConnectionAttributes` | Hostname/Port/Username/Domain match exactly for first connection |
| 9 | `Serialize_OmitsPasswordAttribute_OrEmitsEmpty` | All `Password` attributes are empty (MIG-03) |
| 10-12 | `Serialize_Protocol_MapsToWireValue([Theory])` | Rdp→RDP, Ssh→SSH2, Vnc→VNC |
| 13 | `Pathological_DeepNest_LoadsWithoutStackOverflow` | 7 Containers + 1 Connection; no SO |
| 14 | `Pathological_UnicodeMixed_PreservesUnicodeNames` | "Тест-сервер" survives `SanitizeName` and lookup-by-name works |
| 15 | `Pathological_MalformedSingleRow_LoadsAsValidXml` | `XDocument.Load` does NOT throw |
| 16 | `Pathological_MalformedSingleRow_ParserToleratesBadPort` | TotalConnections == 100; `BadRow.Port == 3389` (parser falls back via `int.TryParse`) |
| 17 | `Pathological_LargeEmptyGroups_ProducesZeroConnections` | TotalConnections == 0, TotalFolders == 50 |

### .csproj wiring

`tests/Deskbridge.Tests/Deskbridge.Tests.csproj` — extended the existing single-file `Content` rule with a glob over `Fixtures\large\*.xml` so all four committed fixtures copy to `bin/Debug/net10.0-windows/Fixtures/large/` for test runs. Existing `Fixtures\sample-confcons.xml` rule untouched.

## Verification

| Gate | Result |
|---|---|
| Task 1 RED build | `dotnet build tests/Deskbridge.Tests` — 8 errors, all `CS0103` for missing `MRemoteNGXmlSerializer` (expected — tests reference type that doesn't exist yet) |
| Task 2 Core build | `dotnet build src/Deskbridge.Core` — 0 errors, 0 warnings |
| Task 2 GREEN test run | `dotnet test tests/Deskbridge.Tests` — **723 passed, 3 skipped, 0 failed** (was 706/706 pre-22-03 → +17 new tests) |
| Full-suite duration | 5.0 seconds (5000-row round-trip included) — well within budget; D-12 explicitly defers hard timing gates |
| Schema fidelity | All 13 pre-existing `MRemoteNGImporterTests` against `sample-confcons.xml` still pass |
| BOM byte-check | `head -c 3` on all four fixtures returns `3c 3f 78` (`<?x`), not `ef bb bf` |
| FullFileEncryption check | `grep` confirms `FullFileEncryption="false"` on the root of all 4 fixtures |
| Container/Connection counts | deep-nest 7 Containers / 1 Connection ✓ ; malformed 100 Connections / 1 `Port="abc"` ✓ ; large-empty 50 Containers / 0 Connections ✓ |

## Commits

- `3d1302c` — `test(22-03): add round-trip + pathological fixtures and failing serializer tests`
- `7e6e732` — `feat(22-03): land MRemoteNGXmlSerializer reverse-mapper (D-08)`

## Deviations from Plan

None. The plan was executed exactly as written:

- Task 1 produced 4 fixtures + 17-test file + .csproj edit in a single `test(22-03):` commit.
- Task 2 produced the static reverse-mapper in a single `feat(22-03):` commit; all 17 tests green; full suite 723/723 (excluding 3 pre-existing skips).
- `malformed-single-row.xml` uses `Port="abc"` per PATTERNS.md correction #3 — the file IS structurally valid XML; the bad row exercises the parser's `int.TryParse → 3389 fallback` (verified by `Pathological_MalformedSingleRow_ParserToleratesBadPort`). Plan 22-04 will layer the executor's continue-and-collect failure semantics on top of this parser tolerance.

## Open Items / Carry-forward to 22-04

- **Executor failure-collection on `Port="abc"`:** Plan 22-04's `[Theory]` over `malformed-single-row.xml` should assert that the parser produces 100 ImportedNodes (one with `Port=3389` defaulted), not that it produces 99 + 1 `FailedImport`. The current executor (22-01) does NOT add stricter post-parse validation to convert the defaulted port into a `FailedImport`. Plan 22-04 may either (a) accept this as documented continue-tolerance OR (b) propose stricter validation as a new follow-up phase — the choice is plan-22-04's, not 22-03's.

## Self-Check

- [x] `src/Deskbridge.Core/Services/MRemoteNGXmlSerializer.cs` — FOUND (157 lines)
- [x] `tests/Deskbridge.Tests/Services/MRemoteNGXmlSerializerRoundTripTests.cs` — FOUND (319 lines)
- [x] `tests/Deskbridge.Tests/Fixtures/large/deep-nest-7-levels.xml` — FOUND (2.4 KB, 7 Containers, 1 Connection)
- [x] `tests/Deskbridge.Tests/Fixtures/large/unicode-mixed.xml` — FOUND (4.5 KB, 1 Container, 15 Connections, Cyrillic preserved)
- [x] `tests/Deskbridge.Tests/Fixtures/large/malformed-single-row.xml` — FOUND (24.1 KB, 1 Container, 100 Connections, 1 `Port="abc"`)
- [x] `tests/Deskbridge.Tests/Fixtures/large/large-empty-groups.xml` — FOUND (10.6 KB, 50 Containers, 0 Connections)
- [x] `tests/Deskbridge.Tests/Deskbridge.Tests.csproj` — MODIFIED (Content glob extended)
- [x] Commit `3d1302c` — FOUND in `git log` (test(22-03): RED + fixtures + csproj)
- [x] Commit `7e6e732` — FOUND in `git log` (feat(22-03): GREEN serializer)
- [x] `dotnet test` — 723 passed, 3 skipped, 0 failed
- [x] No accidental file deletions (`git diff --diff-filter=D HEAD~2..HEAD` returns empty)

## Self-Check: PASSED
