# Phase 1: Foundation - Context

**Gathered:** 2026-04-11
**Status:** Ready for planning

<domain>
## Phase Boundary

Solution scaffold with all three projects (Deskbridge, Deskbridge.Core, Deskbridge.Protocols.Rdp) building on net10.0-windows, plus the architectural backbone (DI container, event bus, connection pipeline, disconnect pipeline, notification service, query interface) as working infrastructure with tests. Also includes a test project and solution-wide conventions.

</domain>

<decisions>
## Implementation Decisions

### Test Infrastructure
- **D-01:** Include a test project at `tests/Deskbridge.Tests/` using xUnit, FluentAssertions, and NSubstitute
- **D-02:** Phase 1 tests must cover: event bus pub/sub (publish, subscribe, unsubscribe, weak reference cleanup), pipeline stage ordering (correct order, failure aborts), connection query fuzzy search (name, hostname, tags against in-memory data), and DI composition (all services resolve without throwing)

### Disconnect Pipeline
- **D-03:** Define a disconnect pipeline (`IDisconnectPipeline`) symmetric with the connect pipeline. Include stages for state save, disconnect, dispose, audit, and publish event. This future-proofs for session recording and state persistence without retrofitting later.

### Solution Conventions
- **D-04:** Full code style enforcement from day one: `.editorconfig` with C# conventions, nullable warnings as errors, code analysis (CA rules)
- **D-05:** Include `GlobalUsings.cs` per project with common namespaces (System.Collections.Generic, CommunityToolkit.Mvvm, etc.) to reduce using noise

### NuGet Strategy
- **D-06:** Use Central Package Management with `Directory.Packages.props` at repo root. All version pins in one file. Projects use `<PackageReference>` without Version attribute.
- **D-07:** Pin major.minor versions (e.g., `Version="4.2.*"`) — auto-picks patch updates, less maintenance burden

### Licensing
- **D-08:** MIT license. Add LICENSE file to repo root. Set `PackageLicenseExpression` in Directory.Build.props.

### Claude's Discretion
- Disconnect pipeline stage names, ordering, and interface shape — follow the connect pipeline pattern from REFERENCE.md but adapted for teardown
- GlobalUsings.cs contents per project — include what makes sense for each project's domain
- .editorconfig rule severity levels — use standard .NET conventions, make nullable warnings errors

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Architecture & Constraints
- `REFERENCE.md` — Full project specification: project structure, DI registrations, event bus interface, pipeline interface, query interface, disposal sequence, all compatibility constraints
- `REFERENCE.md` §Required Configuration Files — Directory.Build.props, app.manifest, Velopack entry point, .gitignore templates

### UI Patterns
- `DESIGN.md` — WPF-UI bootstrap (App.xaml dark theme setup, ApplicationThemeManager.Apply), FluentWindow pattern, namespace conventions. Needed for the App.xaml/Program.cs scaffold even though Phase 1 only shows an empty window.

### Research
- `.planning/research/STACK.md` — Package versions validated for .NET 10 compatibility, pre-scaffold verification checklist
- `.planning/research/PITFALLS.md` — CommunityToolkit.Mvvm 8.4.0 compilation issue (must use 8.4.2+), .NET built-in fluent theme conflict with WPF-UI
- `.planning/research/ARCHITECTURE.md` — Architecture validation, disconnect pipeline recommendation, thread affinity concerns for COM pipeline stages

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `MSTSCLib.dll` and `AxMSTSCLib.dll` in repo root — must be moved to `src/Deskbridge.Protocols.Rdp/Interop/` during scaffold

### Established Patterns
- None — greenfield project. This phase establishes all patterns.

### Integration Points
- None — this is the foundation everything else builds on.

</code_context>

<specifics>
## Specific Ideas

- The connect pipeline interface is fully specified in REFERENCE.md (IConnectionPipelineStage, ConnectionContext, PipelineResult, IConnectionPipeline). The disconnect pipeline should mirror this pattern.
- REFERENCE.md specifies exact DI registrations in App.xaml.cs — follow these as the composition root template.
- WeakReferenceMessenger from CommunityToolkit.Mvvm is the event bus implementation — wrap it behind IEventBus interface as specified.

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope

</deferred>

---

*Phase: 01-foundation*
*Context gathered: 2026-04-11*
