# Build & Release Runbook

Maintainer-facing. How to produce Deskbridge's distributable artifacts locally, and
how the automated release actually works. (End-user install instructions live in
`deployment-guide.md`.)

## What Deskbridge ships

There is **no MSI**. Deskbridge produces three kinds of artifact:

| Artifact | What it is | Produced by |
| --- | --- | --- |
| `Deskbridge.exe` (portable) | Self-contained single-file exe. Bundles the .NET 10 runtime — runs with no install. ~175 MB. | `dotnet publish` |
| `Deskbridge-<channel>-Setup.exe` | **Velopack** installer. Installs per-user to `%LocalAppData%\Deskbridge`, wires auto-update, creates shortcuts. This is what `deployment-guide.md` tells users to run. ~230 MB. | `vpk pack` |
| `Deskbridge-<version>.msix` | MSIX Store package. | CI `msix` job |

The Velopack `Setup.exe` and its companion `*-full.nupkg` / `RELEASES` files are the
auto-update payload: the app checks GitHub Releases on startup and pulls new versions
from these packages.

## Versioning

- **No `<Version>` is set in any csproj.** The version is injected at build time from the
  git tag: `-p:Version=${TAG#v}` (see `.github/workflows/build.yml`).
- `CHANGELOG.md` tracks the target version. As of this writing that is **1.0.0
  (Unreleased)** — nothing has been published yet.
- SemVer2 (required by Velopack). A tag **without** a prerelease suffix (`v1.0.0`) ships to
  the **stable** channel; a tag **with** a suffix (`v1.1.0-beta.1`) ships to **beta** as a
  GitHub pre-release. The channel split is driven purely by the presence of a `-` in the
  version string.

## The automated release (the normal path)

Releases are **tag-triggered, not push-triggered.**

- **Push to `main`** → runs the `build` job only: build, test (`Category!=UAT&Category!=Slow`),
  and upload a `win-x64` exe as a 30-day CI artifact. **No release, no version, no installer.**
- **Push a tag `vX.Y.Z`** → runs `build`, then:
  - `release` job: `vpk download github` (previous release, for deltas) → `vpk pack` →
    `vpk upload github --publish` to GitHub Releases on the matching channel.
  - `msix` job: builds and uploads the `.msix`.

So to cut a real release:

```bash
# 1. make sure main is green and CHANGELOG.md is updated
git tag v1.0.0            # or v1.1.0-beta.1 for a beta
git push origin v1.0.0
# 2. GitHub Actions builds + publishes the Release automatically
```

## Building locally (what you run by hand)

### Portable exe

```bash
dotnet publish src/Deskbridge/Deskbridge.csproj \
  --configuration Release --runtime win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:Version=1.0.0 --output publish
# -> publish/Deskbridge.exe
```

### Velopack installer

```bash
dotnet tool install -g vpk           # first time only
vpk pack -u Deskbridge -v 1.0.0 -p publish -e Deskbridge.exe \
  --packTitle "Deskbridge" --channel stable
# -> Releases/Deskbridge-stable-Setup.exe  (+ *-full.nupkg, Portable.zip, RELEASES-stable)
```

Both `publish/` and `Releases/` are gitignored.

### Caveats for local builds

- **Unsigned.** No code-signing certificate is configured, so `vpk pack` warns and leaves
  every file unsigned. Fine for local testing; production releases should sign (SmartScreen
  will otherwise flag the installer). Signing params would be added to the `vpk pack`/CI step.
- **vpk vs Velopack library version.** `vpk` (currently 1.2.0) is newer than the `Velopack`
  library the app references (`0.0.1298`, in `Directory.Packages.props`). `vpk pack` warns
  ("Velopack library version is lower than vpk version … can occasionally cause compatibility
  issues"). A locally-packed installer **installs and runs** fine; the risk is only in the
  auto-update handshake between mismatched versions. The CI installs `vpk` unpinned too, so it
  hits the same warning. **Follow-up worth doing before the first real release:** either pin
  `vpk` to a 0.0.x that matches, or bump the `Velopack` library to 1.x and re-verify the update
  flow. Track this before tagging `v1.0.0`.

## Related docs

- `deployment-guide.md` — end-user install/config, data locations, network requirements.
- `.github/workflows/build.yml` — the actual CI definition (source of truth).
- `CHANGELOG.md` — version history.
