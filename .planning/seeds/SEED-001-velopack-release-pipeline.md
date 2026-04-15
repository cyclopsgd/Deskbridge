---
id: SEED-001
status: dormant
planted: 2026-04-15
planted_during: v1.0 / Phase 6 planning
trigger_when: Phase 7 (Update & Migration) kickoff — after Phase 6 complete
scope: medium
---

# SEED-001: Convert basic CI workflow into full Velopack release pipeline

## Why This Matters

Phase 6 ships with a minimal `.github/workflows/build.yml` that produces a self-contained single-file `Deskbridge.exe` as a GitHub Actions artifact on every push. This unblocks "download the build at work" immediately without waiting for Phase 7.

When Phase 7 lands (auto-update + installer), this basic workflow should be upgraded to publish proper installers and delta updates to GitHub Releases — not replaced from scratch. Doing this as a refactor (rather than greenfield) preserves the tested-and-known-good build + test + publish steps.

Without this seed, the Phase 7 executor may redesign the workflow from scratch and lose the already-validated single-file publish configuration (RID, PublishSingleFile, IncludeNativeLibrariesForSelfExtract).

## When to Surface

**Trigger:** Phase 7 (Update & Migration) kickoff.

This seed should be presented during `/gsd-new-milestone` or `/gsd-plan-phase 7` when the phase scope matches any of these conditions:
- Velopack packaging work (`vpk pack`)
- GitHub Releases publishing
- Auto-update infrastructure (`GithubSource`, `UpdateManager`)
- Any task mentioning "installer", "Setup.exe", or "release pipeline"

## Scope Estimate

**Medium** — one plan within Phase 7 (probably `07-0X-release-pipeline.md`).

### Concrete work
1. Add tag-triggered job (`on: push: tags: ['v*']`) alongside the existing push/PR build.
2. Install Velopack CLI tool (`dotnet tool install -g vpk` or via `Velopack.Build`).
3. Add `vpk pack` step producing Setup.exe + portable bundle + RELEASES manifest.
4. Publish artifacts to GitHub Releases via `gh release create` (release-notes from commit range).
5. Wire `GithubSource` into `Program.cs` / Velopack custom Main per CLAUDE.md + `.planning/phases/01-foundation` summaries.
6. Consider splitting into `build.yml` (push/PR — fast feedback) + `release.yml` (tag — full packaging) if the combined job slows PR feedback.

### Keep from v1 CI
- `dotnet restore` / `build` / `test` matrix (already works)
- `--filter "Category!=UAT&Category!=Slow"` test scoping
- `setup-dotnet@v4` with `10.0.x`
- Self-contained single-file publish (Velopack wraps this, doesn't replace it)

## Breadcrumbs

### Current v1 CI (upgrade target)
- `.github/workflows/build.yml` — basic build + test + publish + artifact upload

### Velopack already referenced
- `src/Deskbridge/Deskbridge.csproj` line 20 — `<PackageReference Include="Velopack" />`
- CLAUDE.md — "Velopack 0.0.1298 via GithubSource; custom Main method required (App.xaml as Page, not ApplicationDefinition)"

### Phase 1 foundation that Velopack depends on
- `src/Deskbridge/Program.cs` (if exists) — Velopack custom Main entry
- `src/Deskbridge/App.xaml` — must be Page, not ApplicationDefinition (pre-existing, verified)

### Roadmap
- `.planning/ROADMAP.md` § Phase 7: Update & Migration — "The application silently checks for updates and offers one-click upgrade"
- `.planning/REQUIREMENTS.md` — Auto-Update requirements (to be added during Phase 7 discuss)

## Notes

- User explicitly asked for exe (not MSI). If the "MSI for IT distribution" requirement resurfaces later, that's a separate WiX add-on and not this seed.
- Pre-release build signing with an Authenticode cert (Velopack supports this) is an open question for Phase 7 discuss — may become a sub-seed if deferred.
- Auto-update channel strategy (stable / beta / nightly) is also a Phase 7 discuss item — the current CI only produces a single unsigned channel.
