# Workflow Delivery GitHub Release Integration Contract

This integration contract owns the GitHub Release destination, its Windows
application preparation and its Release caller. It refines the
[MLD](../middle-level-design.md), whose [integration
mapping](../middle-level-design.md#integration-mapping) states the shared scope
and authority of the integration contracts.

## Concrete GitHub Release Integration

This contract selects one registered release unit, `image-occlusion-editor`
([declaration](../../../../../public/app/ImageOcclusionEditor/workflow-delivery.release-unit.yml)).
Its one build `windows-app` (definition `dotnet/github-release-assets-v1`)
declares two outputs: the `installer` primary package (`inno-installer`) and
the `portable` package (`portable-zip`). Another unit, build definition or
output kind is unsupported until its own integration is reviewed.

The caller `workflow-delivery-v3-github-release.yml` takes the unit,
destination and dry-run mode as dispatch inputs. Its destinations are:

| Destination                 | Channel  | GitHub Release kind                        | Environment                                      |
| --------------------------- | -------- | ------------------------------------------ | ------------------------------------------------ |
| `github-release-prerelease` | Buddy    | Prerelease, never marked latest            | `workflow-delivery-v3-github-release-prerelease` |
| `github-release`            | Official | Release; GitHub's default latest selection | `workflow-delivery-v3-github-release`            |

`GithubReleaseUnits.Admit` accepts only these unit, channel and destination
pairs and applies the shared ref check: live Buddy requires a branch ref under
`refs/heads/`, and live Official only `refs/heads/main`
([Official channel scope](common.md#official-channel-scope)). The Official
Environment's native branch policy should also be limited to `main`; it does
not replace this check.

### Coordinates

The native version is the product's NBGV `SemVer2` projection from its
committed `version.json`, read with the repository-pinned `nbgv` tool for the
selected ref and revision. The projected commit must equal the selected
revision. Live Official requires the native public-release projection. The
version may contain only ASCII letters, digits, `.` and `-`; the application does
not strip, inject or choose another field.

The Official tag is `release/image-occlusion-editor/v<SemVer2>`. This is the
repository's existing `release/<unit>/v<version>` tag convention and the tag
form already named by the product's NBGV `publicReleaseRefSpec`. The Buddy tag
is `buddy/image-occlusion-editor/v<SemVer2>`, a distinct coordinate as
WD-CHN-001 requires. The release title is `image-occlusion-editor v<SemVer2>`.
The two assets are `ImageOcclusionEditor-<SemVer2>-win-x64-setup.exe` and
`ImageOcclusionEditor-<SemVer2>-win-x64-portable.zip`.

A Buddy prerelease from `main` projects the same public version as an Official
release from the same commit, but under its own tag, so the two coexist and
neither observes the other. The repository's "latest" release is left at the
GitHub default for Official releases; Buddy prereleases are never marked
latest.

### Preparation

Preparation runs once on the supported Windows runner without write
capability, with the job token and OIDC variables removed from every native
command. It records these required checks in order and skips the rest after
the first failure:

1. `native-input`: subject admission, exact Git materialization, the committed
   declaration and inputs, complete Git history, the `global.json` SDK with
   `rollForward: disable` equal to the actual SDK, `dotnet tool restore` and the
   NBGV projection.
2. `build`: the product's own scripts, each run once:
   `Update-LocalSvgEditor.ps1 -Force`, `Publish-ImageOcclusionEditor.ps1` and
   `Build-InnoInstaller.ps1`, into fresh owned scratch. The application copies
   the single setup executable and writes the portable ZIP from the published
   tree with ordinal entry order and fixed entry times.
3. `contents`: the installer is a Windows executable, and the ZIP entries are
   exactly the published files, including `ImageOcclusionEditor.exe` and the
   bundled `svg-edit/index.html`.
4. `installer-consumer`: a silent per-run install of the original installer
   into owned scratch (`/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /NOICONS`).
   The installed executable must equal the published one and the editor must
   be present.
5. `portable-consumer`: the original ZIP extracted into owned scratch, with
   the same check.
6. `original-bytes`: each original's size and SHA-256 are bound to the request.

The native environment carries the selected `GITHUB_REF` and `GITHUB_SHA`, so
NBGV projects the request's version. It uses owned temporary and
`GITHUB_ENV` files, so native cloud-build variables do not reach the runner.
Preparation has a 60-minute limit. It does not launch the installed
application; consumption proves installation and file layout, not runtime
behavior.

The editor download in `Update-LocalSvgEditor.ps1` is the product's own
resolution of its pinned `svgedit` npm version. The script pins that version's
npm integrity (`sha512` SRI) and verifies the downloaded package before
extraction; a mismatch deletes the package and fails preparation, and a
different version without a supplied integrity fails before download.

### Observation and Publication

Observation reads `api.github.com` for `hcoona/three` with the job
`GITHUB_TOKEN` (read-only outside the publisher), and does not mutate state.
It has a 20-minute limit and a 2 MiB metadata limit, and it records the single
required check `release-identity`.

- If the tag exists, it must be a lightweight tag at the selected revision.
  Any other existing tag is a conflict and is never moved.
- If no release exists for the tag, every asset is absent. A missing tag is
  acceptable only then, because creation binds the tag to the selected
  revision. An existing release whose tag is missing is a conflict, even when
  its assets are exact, so it is neither satisfied nor extended.
- An existing release must not be a draft, and its prerelease flag must match
  the channel.
- Every listed asset must be one of the prepared names in the `uploaded`
  state, with the prepared size. Another name is a conflict; another state is
  unknown.
- Each listed asset is downloaded and compared byte for byte with its original
  (WD-REL-009). Equal bytes are exact; other bytes are a conflict.

Unavailable or contradictory facts are unknown and permit no mutation.

Only the `publish` job receives the destination Environment and
`contents: write` on its job `GITHUB_TOKEN`; no other credential is used.
It refreshes the complete observation and refuses to enlarge the reviewed
writes. When the release is absent, it creates it once at the selected
revision, which also creates the tag. Then it uploads each missing asset,
installer first. A failed or ambiguous create or upload stops all further
writes. Existing exact assets are retained, and different assets are never
replaced or deleted. There is no automatic resend, rerun recovery or
compensation.

The separate no-write `verify-result` job observes again and downloads every
asset. An identical existing release is satisfied without writes. A conflict
is unsatisfied, and missing or unknown state blocks completion. The repository
does not enable GitHub immutable releases, so retention after completion
relies on the release and asset non-overwrite behavior above.

Rebuilding the same revision is not guaranteed to reproduce identical
installer or ZIP bytes. After a partial publication, a fresh request whose
rebuilt bytes differ observes a conflict instead of completing the subset.
This fail-closed behavior is accepted: the existing assets are never
overwritten, and the owner decides the next step.

### Caller

The caller uses `prepare` on `windows-latest` (PowerShell), then `observe`,
gated `publish` and `verify-result` on Linux. Control, preparation and originals
move between jobs by same-run artifact ID with the native digest check.
`job-inputs github-release receive <phase>` binds the received preparation's
subject to the request. One concurrency group per unit serializes both
channels through verification, because they share one product version stream.
Dry run
runs preparation and observation, enters no write gate and never invokes the
create or upload endpoints.

Changes follow the [shared review core](../design/release.md#release-integration-review).
Controlled scenario tests cover preparation with a controlled Windows
toolchain, observation, publication order, non-overwrite and caller steps.
The Windows `GitHub Release native preparation (Windows)` CI job runs the
`GithubReleaseNativePreparation` test: the real preparation of this unit on
`windows-latest`, including the product build, ISCC, the silent install and
the portable extraction, and it retains both prepared assets as CI evidence.
It runs when the product, the Workflow Delivery source or tests, or shared
.NET/CI inputs change, and Validate requires its result. Neither proves live
GitHub behavior or the Environments' configuration.

### GitHub Release Destination Completion

The MLD's [post-publication completion](../design/release.md#post-publication-completion)
contract applies to this destination; this table owns its concrete conditions
and observation.

| Existing destination/use                            | Native completion conditions to preserve                                                                                                          | Post-publication observation                                                                                   |
| --------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------- |
| GitHub prerelease Buddy and GitHub release Official | Tag at the selected revision, one non-draft release with the channel's prerelease flag, exactly the two prepared assets uploaded with their sizes | Each prepared asset listed on the release in the `uploaded` state, and its downloaded bytes equal the original |
