# Current Delivery Wave

This record is the sole positive work-authorization authority. Authorization changes
only when merged into `main-v2`. Issues and pull requests coordinate work; proposed
records do not authorize their own execution. Preparing and reviewing an explicitly
owner-authorized Wave amendment does not perform the work it proposes to add.

## Authorized Advancements

### Windows 0.1.0-alpha Prereleases (2026-10-29)

**Outcome:** Add a GitHub release workflow that builds the `azureauth-unofficial`
portable package from `main-v2` on a GitHub-hosted Windows runner, and publish
`0.1.0-alpha` prereleases from it as GitHub prereleases of this repository.
Packaging is tracked in
[Issue #506](https://github.com/hcoona/microsoft-authentication-cli/issues/506); release
gates stay tracked in
[Issue #482](https://github.com/hcoona/microsoft-authentication-cli/issues/482). The
prereleases claim no support, production readiness, compatibility, clean-machine
deployment or satisfaction of the validation strategy's release gates.

**Owner decisions:** The repository owner approved this entry on 2026-10-29, amended it
on 2026-10-30, and decides:

- Workflow: started manually (`workflow_dispatch`) and refused unless the ref is
  `refs/heads/main-v2`; runs on a GitHub-hosted `windows-latest` runner with the
  checksum-pinned SDK from `mise.lock`; runs the Windows scenario project excluding
  `WindowsHostTiming`, as the `windows-scenarios` CI job does, then
  `packaging/windows/package.ps1` without `-AllowNonPublicRelease` or
  `-DisableNuGetAudit`; creates a GitHub build-provenance attestation for both zips;
  and creates a **draft** prerelease whose target is the built commit, refusing a version
  whose tag or release already exists. Only the job that creates the draft receives
  `contents: write`, and only the job that attests receives `id-token: write` and
  `attestations: write`. Files pass between jobs as workflow artifacts uploaded with
  `retention-days: 1` and left to expire; no job deletes them.
- Native build chain: the hosted runner image floats, so its Visual Studio C++ tools,
  Windows SDK and `link.exe` change over time. The owner accepts this for the
  `0.1.0-alpha` series instead of pinning them: each run records the MSVC, Windows SDK,
  `link.exe` and .NET SDK versions and the runner image version, and the release review
  checks them.
- First release: the primary journey's first-release blocker applies to the first
  supported, non-prerelease release. `0.1.0-alpha` prereleases are exempt from it and
  remain unsupported.
- Release identity: tag `v<version>`, where `<version>` is the computed SemVer 2.0
  version without build metadata (for example `v0.1.0-alpha.42`), on the built commit.
  Assets: the package zip, the symbols zip and their `.sha256` files. Release notes link
  the package README, state the unofficial, unsigned and unsupported status and the
  bundled Visual Studio Profile's limits, and link the release review.
- Asset contents (2026-10-30): the symbols zip also carries the package's `LICENSE.txt`
  and `THIRD-PARTY-NOTICES.txt`, and the text files of both zips use LF line endings
  regardless of the build host's Git configuration. The draft of `v0.1.0-alpha.31`,
  built before this change, is not published and is deleted.
- Publication: the repository owner reviews each draft and publishes it manually as a
  prerelease; publication is the owner's release decision. The owner may instead
  instruct the authoring agent to publish a draft through the owner's GitHub session.
  The instruction names the version and is an owner comment, or an owner-attributed
  disposition, on Issue #506 posted before publication; the agent publishes only when
  that version's release review and independent triage record no unresolved finding
  and no finding that needs an owner decision. Such a publication is the owner's
  publication decision, carried out through the owner's session. No local verification
  of a downloaded draft is required; Mark-of-the-Web, SmartScreen and Smart App Control
  behavior is not evaluated or claimed.
- Scope: this entry covers every `0.1.0-alpha` prerelease. A version other than
  `0.1.0-alpha.{height}`, a non-prerelease, or a change to the release workflow's
  inputs, permissions or assets needs a new entry.
- Risk: the owner accepts publishing unsigned executables, the bundled Visual Studio
  Profile under decision 0003's prerelease exception, and the native and managed symbol
  files of each build; that each run's unreviewed outputs are downloadable by signed-in
  GitHub users as workflow artifacts until they expire after one day; and that completed
  downloads cannot be recalled.

**Delegated work:**

1. Add `.github/workflows/release.yml` as above, with actions pinned by commit SHA as in
   the existing workflow, and full Git history for version computation.
2. Records: update the support and release posture in `docs/governance/project.md`; the
   `update-channel` and `package-name` selection evidence in
   `operational-identities.yaml` (tag scheme, symbols zip); the Windows design's
   package section and its Native AOT build-chain pinning sentence (recorded versions for
   hosted `0.1.0-alpha` builds); the validation strategy's CI section, release-readiness
   note and Primary Journey Gate, and `docs/product/user-stories.md`, for the
   first-release decision; the threat model's build and CI-token row for the release
   workflow's permissions and attestation (the `.tm7` stays unchanged); a
   `controls.yaml` entry for the release workflow's mechanical gates at the `release`
   execution point, subject to its governance review; the package README and root
   README where they say no package has been published; `CONTRIBUTING.md`; and the
   completed host test: convert `docs/research/experiments/portable-package-host-test.md`
   into a completed evidence record, with its record family and `docs/README.md` row,
   modeled on `real-host-validation-evidence`.
3. Release review for each prerelease, before the owner publishes the draft: an
   independent research-evidence review at the `release` execution point that
   evaluates the whole recheck registry, checks that the draft's assets match the
   workflow run's outputs and attestation and that its target is the built commit,
   checks the recorded build-chain versions, and checks that the symbols and executable
   embed no user-identifying build path; independent triage of any finding; recorded as
   a comment on Issue #506 that the release notes link.
4. Correct defects in the workflow, the packaging script or the records that a run
   exposes, with tests where applicable. Implement the asset-contents decision in
   `package.ps1`, `.gitattributes` and the Windows design, and delete the
   `v0.1.0-alpha.31` draft.

**Exclusions:** No real-account authentication, WAM UI or experiment on any host. No
signing, installer, MSIX, WinGet, Scoop, automatic update, PATH or registry integration,
non-`win-x64` artifact, or upload outside this repository's GitHub Releases. No
authentication behavior, result contract, request protocol or Profile change. No SDK,
runtime or dependency change. No tag or release outside the `v0.1.0-alpha.*` series, and
no release from a ref other than `main-v2`.

**External effects:** GitHub Actions runs on GitHub-hosted runners with public package
restore and tool downloads verified by `mise.lock`; build-provenance attestations
recorded in this repository and, for this public repository, in the public Sigstore
transparency log; workflow artifacts of each run, downloadable by signed-in GitHub users
for at most one day; public tags and GitHub prereleases with their assets (the owner can
delete them, but completed downloads and transparency-log entries cannot be recalled).

**Completion:** Remove this entry through a reviewed Wave change when the owner ends the
`0.1.0-alpha` series or replaces it with a new entry.
