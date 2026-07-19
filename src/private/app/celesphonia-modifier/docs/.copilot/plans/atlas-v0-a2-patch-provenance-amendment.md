# Atlas V0 A2 Official-Patch Provenance Amendment

**Status:** Governing only through an exact verified plan-review record; no private-run authority

**Increment:** A2R1 - Official-Patch Provenance

**Implementation language:** C# on the repository-pinned .NET 10 SDK

**Pre-amendment baseline:**
`9edbd57b4f44e76de321e06be81a581ed11b0017`

**Governing plans:** `atlas-v0-a0-research-contract.md` and
`atlas-v0-a2-intake-safety-plan.md`

**Planned review record:**
`../reviews/atlas-v0-a2-patch-provenance-plan-review.md`

## 1. Reason for the amendment

The released A0 corpus was collected from:

- Steam application `1786790`;
- public Steam build `13624401`;
- Steam integrity verification followed by installation of the exact operator-retained Kagura
  Games patch package labeled `CN Patch v1.05.2`; and
- a required private attestation covering installation order and the absence of later installation
  modifications.

The project leader confirmed that the retained installer is the exact file used for A0. The public
source page is:

`https://kaguragamer.com/product/magical-girl-celesphonia-patch/`

The publisher's public installation instructions are:

`https://kaguragamer.com/patch-instructions/`

The existing A2 plan instead asks the project leader to attest that the Steam installation was not
manually altered. That description cannot represent the released A0 source profile. It blocks the
private run and would produce incomplete provenance if ignored.

This amendment corrects provenance only. It does not change the two source roots, save roles,
definition groups, selection rules, terminal decisions, 23/21/2 save counts, 580/496/84 definition
counts, Steam application, public build, or one-shot survey identity. Any difference in those
values still reopens A0.

## 2. Authority and invalidation

Private discovery remains prohibited until:

1. this amendment candidate is committed and pushed;
2. a fresh independent reviewer reports exact `No findings`;
3. its plan-review record is independently reviewed as an exact staged blob;
4. that record is committed unchanged as the only child change, pushed, and verified;
5. the amended C# candidate is implemented, committed, and pushed;
6. a fresh independent source review reports exact `No findings`; and
7. a new amended tool-safety record passes the same record-only verification.

The original A2 plan-review and tool-safety records remain immutable historical evidence.
Tool-safety record `9edbd57b4f44e76de321e06be81a581ed11b0017` does not authorize discovery
after this material source-profile decision.

After verification, this amendment partially supersedes the A2 plan's section 6 v1 request
contracts, section 10 source-profile attestation and strictly human-invoked wording, and sections
17.2-17.3 request-preparation and local-invocation wording. It does not supersede the rule that
Copilot and subagents never receive private document bytes, the project leader personally audits
private outputs, and only the project leader can approve the pending manifest and later phases.

The verified amendment plan-review record becomes the implementation diff base. Any implementation
outside the exact path boundary in section 11 creates a new plan candidate. Any later tracked
source, schema, test, dependency, project, SDK, build-procedure, or provenance-policy change
invalidates the amended source-safety gate.

## 3. Exact provenance model

The active source profile is:

`steam-public-build + official-patch-package + human-installation-attestation`

The public repository may record only:

- Steam application `1786790`;
- public build `13624401`;
- patch source and instruction URLs;
- version label `CN Patch v1.05.2`;
- that a private installer SHA-256 was verified;
- that the required installation attestation passed; and
- safe aggregate corpus counts and difference categories already allowed by A0 and A2.

The following remain private:

- installer absolute path;
- installer SHA-256;
- private request and output bytes;
- source-root paths;
- file names beyond approved aliases;
- private manifest, state, receipt, inventory, and provenance digests; and
- copied or installed content.

The installer SHA-256 identifies the retained package bytes. It does not cryptographically prove
which files the installer wrote, that the installed tree is unchanged, or that installed files
derive from those bytes. The human attestation bridges package identity to installation history.
Later copy hashes independently prove only the copied per-file bytes.

## 4. Human installation attestation

The discovery request carries this closed private attestation:

```text
attestedByRole = project-leader
steamIntegrityCheckPassed = true
exactBoundInstallerAppliedAfterIntegrityCheck = true
noLaterSteamVerificationOrUpdate = true
noLaterPatchRerun = true
noLaterManualExecutableOrDefinitionEdit = true
```

The project leader must confirm that these statements describe both the A0 baseline procedure and
the current installation before preparing the discovery request. A false, missing, unknown, or
incomplete field is a safety refusal.

The final three fields apply to Steam- or patch-managed executable and definition content. They do
not prohibit ordinary gameplay writes inside the two approved save roots. A later Steam
verification, Steam update, patch rerun, or manual executable/definition edit invalidates the
attestation even if the installer bytes remain unchanged.

The final public intake-approval record may state only that the private attestation passed. It does
not contain the installer path, installer hash, request bytes, or installed-file details.

## 5. Contract versions and exact fields

The amendment replaces these private contract versions:

| Contract                 | Existing                               | Amended                                    |
| ------------------------ | -------------------------------------- | ------------------------------------------ |
| Discovery request        | `atlas-intake-discovery-request/v1`    | `atlas-intake-discovery-request/v2`        |
| Confirmation             | `atlas-intake-confirmation-request/v1` | `atlas-intake-confirmation-request/v2`     |
| Copy request             | `atlas-intake-copy-request/v1`         | `atlas-intake-copy-request/v2`             |
| Discovery preparation    | None                                   | `atlas-intake-discovery-preparation/v1`    |
| Confirmation preparation | None                                   | `atlas-intake-confirmation-preparation/v1` |
| Copy preparation         | None                                   | `atlas-intake-copy-preparation/v1`         |
| Source-root map          | `atlas-source-root-map/v1`             | `atlas-source-root-map/v2`                 |
| Intake state             | `atlas-intake-state/v1`                | `atlas-intake-state/v2`                    |
| Copy receipt             | `atlas-copy-receipt/v1`                | `atlas-copy-receipt/v2`                    |

Cleanup-preflight request, manifest, copy-plan, inventory, and cleanup-report versions remain
unchanged. State revision numbers, manifest revision numbers, and all existing canonical file names
remain unchanged.

The only publication-order exception is discovery: v2 publishes the source-root map before the
pending manifest so the root map is the phase's patch-bound recovery marker. Section 7 defines the
closed v1/v2 transition.

### 5.1 Reviewed request preparation

The amended C# CLI adds these fixed-output commands:

```text
celesphonia-atlas intake-prepare-discovery <preparation-file>
celesphonia-atlas intake-prepare-confirmation <preparation-file>
celesphonia-atlas intake-prepare-copy <preparation-file>
```

The project leader creates each private preparation file and supplies the human-only facts. The
preparation files occupy fixed, alias-only relative paths under:

```text
src\private\app\celesphonia-modifier\.private\atlas-v0\
  operator-input\<surveyAlias>\discover.json
  operator-input\<surveyAlias>\confirm.json
  operator-input\<surveyAlias>\copy.json
```

That directory is outside the surveyed workspace and its closed census. The relative command paths
contain only public fixed segments and the approved survey alias; they reveal no source locator,
source file name, or digest. Copilot may invoke a reviewed command with that relative path but may
not read or display the preparation file.

Discovery preparation contains every v2 discovery-request field except
`expectedInstallerSha256`. The builder:

1. strictly validates the preparation shape and canonical alias-only paths;
2. validates and hashes only the explicit installer using section 6 steps 1-8 and 10, requiring
   metadata stability but deferring expected-hash equality to human approval and discovery;
3. inserts the calculated digest into the v2 discovery request;
4. serializes canonical UTF-8 bytes;
5. creates the request at its one canonical workspace path, or accepts an exact byte match; and
6. emits only `Intake request prepared.` or the existing classified diagnostic.

The project leader opens the generated request, audits its private path, digest, attestation, and
all existing A2 request fields, and explicitly approves it before discovery. This human review
turns the builder-calculated digest into the expected installer binding that discovery rehashes.

Confirmation preparation contains exactly `schemaVersion`, `surveyAlias`, `projectRoot`,
`workspaceRoot`, `approved`, `decisionCommit`, and `expectedPendingManifestSha256`. `approved` must
be `true`. It reads sealed canonical state/root-map/plan/inventory evidence, requires the
human-supplied manifest digest to match, projects the root-map patch binding, and builds the exact v2
confirmation request at the existing canonical path.

Copy preparation contains exactly `schemaVersion`, `surveyAlias`, `projectRoot`, `workspaceRoot`,
`expectedApprovedStateSha256`, and `decisionCommit`. It reads sealed canonical
state/root-map/plan/inventory evidence, requires the human-supplied state and decision values to
match, projects the root-map patch binding, and builds the exact v2 copy request at the existing
canonical path.

Preparation never opens live game sources. Confirmation/copy preparation never opens the installer.
Every builder is create-new or exact-byte-idempotent, supports cancellation, writes no partial final
request, never overwrites different bytes, and never emits private values or exception details.
Preparation files and generated requests remain private. The tool does not delete them.

Discovery preparation also requires the canonical workspace census to contain no A2 output from a
prior v1 or v2 attempt. Its only permitted existing generated request is an exact byte match to the
new v2 request. It never migrates, deletes, or overwrites an earlier request or output.

### 5.2 Discovery request

The v2 discovery request adds one required `officialPatch` object containing exactly:

- `sourceUrl`, exactly the public product URL in section 1;
- `versionLabel`, exactly `CN Patch v1.05.2`;
- `installerPath`, an explicit private absolute DOS path;
- `expectedInstallerSha256`, 64 lowercase hexadecimal characters; and
- `installationAttestation`, with exactly the six fields and values in section 4.

No current directory, profile, registry, Downloads folder, Steam manifest, or environment value is
used to infer the installer path or hash.

### 5.3 Confirmation and copy requests

The v2 confirmation and copy requests each add required
`expectedOfficialPatchInstallerSha256`. They obtain the installer path from the strictly validated
source-root map bound through the preceding state, then require exact equality among:

- the request's expected hash;
- the source-root map's installer hash;
- the preceding state's patch binding;
- the patch installer inventory alias; and
- the freshly calculated installer hash.

Completed-phase validation and recovery trust sealed private evidence and perform no installer or
live-source open.

### 5.4 Source-root map

`atlas-source-root-map/v2` adds one required `officialPatchProvenance` object containing exactly:

- public source URL;
- public version label;
- installer artifact alias;
- private installer absolute path;
- private lowercase SHA-256; and
- the closed installation attestation.

The installer path appears in no state or receipt.

### 5.5 State and receipt binding

Every `atlas-intake-state/v2` revision and `atlas-copy-receipt/v2` contains one required,
path-free `officialPatchBinding` object with exactly:

- public source URL;
- public version label;
- installer artifact alias; and
- private lowercase installer SHA-256.

State revision 1 records discovery verification, revision 2 records approval-time revalidation,
and revision 3 plus the copy receipt record the final fresh pre-copy revalidation. State revision 4
inherits the exact state-3 binding and performs no installer open.

The implementation uses distinct full-provenance and path-free-binding types. It defines one
`ToBinding()` projection from root-map provenance and permits exact equality only among projected
or path-free binding objects. Root-map attestation is validated in full and remains transitively
bound through the existing root-map document digest in every state.

Inventory validation separately requires one installer row with the exact alias, class, purpose,
retention tuple, and verification method from section 5.6, plus source-root-map direct lineage to
that alias. Any projection/hash/alias/version/source/attestation/inventory mismatch is a safety
refusal.

### 5.6 Private inventory

Discovery allocates the official-patch installer artifact before the source-root-map artifact and
adds one `atlas-private-inventory/v1` entry:

```text
artifactClass = private-provenance
purpose = official-patch-installer
custodianRole = project-leader
lineageAliases = []
lastUseMilestone = post-A8-appeal
expiryCondition = never
plannedDisposition = retain-private
status = retained
verificationMethod = sha256-held-read-handle
qualification = null
```

The source-root-map inventory entry names the installer artifact alias as direct lineage. Alias
allocation remains consecutive and uses the existing monotonic cursor.

The source-root-map inventory entry is also changed to `lastUseMilestone = post-A8-appeal`,
`expiryCondition = never`, `plannedDisposition = retain-private`, and `status = retained`.

The installer package and the source-root map holding its private hash evidence are permanently
retained. The existing cleanup report remains single-result: it returns `blocked-status` for both
rows because status is evaluated first, while separately preserving and validating
`plannedDisposition = retain-private`. A8 has no deletion authority for either unless a future
independently approved plan explicitly reverses the project-leader decision.

## 6. Installer hashing and path safety

Fresh discovery, fresh confirmation, and fresh copy each:

1. validate the installer path as an absolute canonical fixed-drive DOS path;
2. reject device, UNC, relative, outside-policy, and reserved-name paths;
3. reject every visible reparse or device component before opening the file;
4. require an ordinary file outside the game root, project root, and private workspace;
5. open with `FileMode.Open`, `FileAccess.Read`, and `FileShare.Read`;
6. capture initial stream length and path last-write metadata while write/delete sharing is denied;
7. calculate SHA-256 incrementally with bounded buffers and caller cancellation;
8. recapture stream length and path last-write metadata before releasing the handle;
9. require stable metadata and exact expected hash equality; and
10. close the handle before publication.

The process never executes, copies, writes, renames, deletes, changes attributes on, semantically
parses, or logs the installer. The fixed CLI output and diagnostics never contain its path, hash,
length, timestamp, file name, or exception details.

Cancellation before phase publication produces no new state. Malformed request values are exit 2;
caller cancellation is exit 3; access, sharing, and read failures are exit 4; missing, reparse,
unstable, outside-policy, or hash-mismatched installer evidence is exit 5.

## 7. Phase and recovery behavior

### Closed v1 transition

No private discovery was run under v1. The amended implementation performs no v1 migration,
upgrade, deletion, or best-effort recovery.

The final `atlas-source-root-map/v2` file is the sole discovery-phase patch-verification marker.
Fresh discovery hashes the installer before publication and publishes that v2 root map before any
pending manifest or later discovery output. A rerun may enter no-rehash recovery only after it
strictly validates the v2 root map, its exact request-derived provenance, and its deterministic
canonical location.

Any pending manifest, inventory transition, copy plan, state, backup, staging file, or later-phase
artifact without that valid v2 root map is a safety refusal. A v1 root map or state is a safety
refusal. The tool neither deletes nor repurposes such bytes; the project leader must archive the
workspace and authorize a new empty survey identity before retrying. Confirmation, copy, and
preflight require state v2, so v1 state cannot enter their recovery paths.

### Discovery

Discovery validates and hashes the installer before enumerating live game roots. It publishes the
v2 root map first, then the pending manifest and later outputs. Inventory lineage and state revision
1 are published only after the private hash and installation attestation pass.

### Confirmation

Fresh confirmation validates state revision 1 and every bound private document, rehashes the
installer, and publishes state revision 2 only after exact equality. A completed valid state-2-or-
later rerun returns from sealed evidence without opening the installer or live sources.

### Copy

Fresh copy validates state revision 2 and every bound document, rehashes the installer before
opening any live game source, and carries the exact path-free binding into the receipt and state
revision 3.

A missing, changed, unstable, reparse-backed, or mismatched installer before fresh state revision 3
stops A2. It does not authorize recopying or replacement with another package.

### Recovery and preflight

Recovery after any output-publication seam validates sealed bytes only. It never rehashes the
installer and never opens live game sources. A complete staged receipt and finalization recovery
uses the patch binding already sealed before copying. Cleanup preflight validates state revision 3
and inherits its patch binding into state revision 4 without opening the installer.

## 8. Privacy and operator boundary

The project leader may direct Copilot to invoke the deterministic C# request builder and execute the
resulting private request after the amended source-safety gate, provided:

- the human-created preparation file uses only the exact canonical alias-only path in section 5.1;
- the project leader audits and approves the generated request before its operation;
- the operation uses only the exact canonical generated-request path;
- no command output, retained transcript, or Agent message contains the private request, installer
  path, installer hash, source paths, manifest bytes, or generated private records;
- Copilot does not open, parse, display, search, summarize, or attach generated private files;
- the CLI emits only its fixed success or classified terminal diagnostic;
- the project leader alone opens and reviews the generated private documents; and
- only approved aggregate counts, public identifiers, and difference categories return to Git or
  an Agent transcript.

Executing the reviewed CLI is not authority to inspect its inputs or outputs. Any unexpected
process output, exception detail, or private disclosure is a stop condition.

## 9. Synthetic test requirements

All tests use synthetic installer bytes and paths. They cover:

- strict preparation shapes, canonical alias-only paths, builder fixed output, create-new behavior,
  exact-byte idempotence, cancellation, and no-private-output failures;
- discovery preparation hash generation followed by explicit human-approval simulation and fresh
  discovery rehash comparison;
- confirmation/copy preparation from sealed evidence without installer or live-source opens;
- strict v2 request shape, duplicate, missing, null, unknown, trailing, URL, version, hash, and
  attestation validation;
- schema/DTO agreement for root-map v2, state v2, and receipt v2;
- ordinary-file, fixed-drive, containment, reserved-name, and component-reparse policy;
- held-stream length and write-denied path last-write stability;
- exact hash success plus mismatch, short-read, sharing, I/O, and cancellation failures;
- zero publication after failed installer validation;
- source-root-map-v2-first publication and no-rehash recovery only after that marker validates;
- fail-closed handling for every synthetic partial-v1/v2 artifact combination;
- fresh discovery hashing before live-root enumeration;
- fresh confirmation and copy detecting installer replacement;
- exact root-map/state/receipt/inventory alias and hash continuity;
- permanent inventory retention and cleanup-preflight blocking;
- completed discovery/confirmation/copy/preflight performing zero installer and source opens;
- every existing output-publication and recovery seam preserving prior no-reopen behavior;
- synthetic private path/hash sentinels absent from stdout, stderr, exceptions, and canonical
  repository-safe outputs;
- all existing A0 corpus counts, roots, rules, aliases, and decisions unchanged; and
- every pre-amendment A2 regression test.

## 10. Acceptance criteria

The implementation increment passes only when:

1. the exact plan candidate and plan-review record satisfy section 2;
2. only the paths in section 11 change from the verified amendment plan-review record;
3. the C# builders prepare auditable requests without exposing private values to Copilot;
4. all versioned contracts and schemas match section 5 exactly;
5. installer path and hash remain private in every success and failure;
6. all three fresh phases rehash and bind the exact installer as section 6 requires;
7. recovery and completed reruns open neither installer nor live source;
8. no-rehash discovery recovery requires the source-root-map-v2 marker;
9. every v1 or ambiguous partial workspace fails closed without deletion;
10. the human attestation is closed, exact, and excludes ordinary gameplay save writes;
11. the installer and hash-evidence inventory rows are permanently retained and `blocked-status`;
12. no claim equates package hash with installed-file identity;
13. production code remains BCL-only with no project, package, lock, TFM, or telemetry change;
14. locked restore and warning-free build of the test project pass;
15. `dotnet format --verify-no-changes` passes for all three projects;
16. the complete Microsoft.Testing.Platform suite and direct apphost smoke tests pass;
17. evaluated project and package references remain within the approved graph;
18. exact no-renames path, line-length, LF, HK, `git diff --check`, tree, ancestry, upstream, and
    clean-worktree checks pass;
19. a fresh independent source reviewer reports exact `No findings`;
20. the amended tool-safety record is reviewed as an exact staged blob and published unchanged as
    the only child change; and
21. private discovery remains blocked until that record passes parent, path, blob, upstream, and
    clean-worktree verification.

## 11. Exact implementation path boundary

The amended implementation candidate may change only:

```text
src/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas/
    AtlasDiscovery.cs
    AtlasIntakeContracts.cs
    PrivateArtifactLifecycle.cs
    AtlasRequestPreparation.cs
    TrustedLocalCopy.cs
  Hcoona.CelesphoniaModifier.Atlas.Cli/
    AtlasCliApplication.cs
    AtlasCliOperations.cs
  docs/.copilot/
    README.md
    schemas/atlas-v0/
      copy-receipt.schema.json
      intake-state.schema.json
      source-root-map.schema.json
tests/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas.Tests/
    AtlasCliApplicationTests.cs
    AtlasDiscoveryTests.cs
    AtlasIntakeContractTests.cs
    AtlasProcessSmokeTests.cs
    PrivateArtifactLifecycleTests.cs
    AtlasRequestPreparationTests.cs
    ProjectBoundaryTests.cs
    TrustedLocalCopyTests.cs
```

The README change records the amended contract status only. The two listed CLI files authorize only
the three fixed-output preparation commands. The two listed new C# files authorize the BCL-only
request builder and its synthetic tests. No project, package, lock, root configuration, new schema
path, or other production/test file is authorized.

The plan candidate itself contains exactly:

```text
src/private/app/celesphonia-modifier/docs/.copilot/
  README.md
  plans/
    atlas-v0-a0-research-contract.md
    atlas-v0-a2-intake-safety-plan.md
    atlas-v0-a2-patch-provenance-amendment.md
```

The plan-review record child adds only:

```text
src/private/app/celesphonia-modifier/docs/.copilot/reviews/
  atlas-v0-a2-patch-provenance-plan-review.md
```

The amended tool-safety record path is:

```text
src/private/app/celesphonia-modifier/docs/.copilot/reviews/
  atlas-v0-a2-patch-provenance-tool-safety-review.md
```

Future intake-approval and release records retain their previously planned paths but must cite this
amendment and the amended source-safety record.

## 12. Validation procedure

Run from the repository root:

```powershell
$projectRoot = "src\private\app\celesphonia-modifier"
$testRoot = "tests\private\app\celesphonia-modifier"
$library = "$projectRoot\Hcoona.CelesphoniaModifier.Atlas\" +
  "Hcoona.CelesphoniaModifier.Atlas.csproj"
$cli = "$projectRoot\Hcoona.CelesphoniaModifier.Atlas.Cli\" +
  "Hcoona.CelesphoniaModifier.Atlas.Cli.csproj"
$tests = "$testRoot\Hcoona.CelesphoniaModifier.Atlas.Tests\" +
  "Hcoona.CelesphoniaModifier.Atlas.Tests.csproj"

mise exec -- dotnet restore $tests --locked-mode -v:minimal
mise exec -- dotnet build $tests --no-restore /m:1 -warnaserror -nologo -v:minimal
mise exec -- dotnet format $library --no-restore --verify-no-changes --verbosity minimal
mise exec -- dotnet format $cli --no-restore --verify-no-changes --verbosity minimal
mise exec -- dotnet format $tests --no-restore --verify-no-changes --verbosity minimal
mise exec -- dotnet test --project $tests --no-build --no-restore --verbosity minimal
mise exec -- dotnet test --project $tests --no-build --no-restore --verbosity minimal `
  --filter-class '*AtlasProcessSmokeTests'
mise exec -- dotnet msbuild $library `
  '-getItem:ProjectReference,PackageReference' -nologo
mise exec -- dotnet msbuild $cli `
  '-getItem:ProjectReference,PackageReference' -nologo
mise exec -- dotnet msbuild $tests `
  '-getItem:ProjectReference,PackageReference' -nologo
```

Also run ref-bound HK checks, `git diff --check`, committed-file LF and line-length checks, exact
no-renames path comparisons, and candidate tree, ancestry, upstream, and clean-worktree checks.

## 13. Stop conditions

Stop and return to planning if:

- the exact retained installer is unavailable;
- installer bytes or private SHA-256 change;
- the public source URL or selected version label changes;
- the Steam public build changes;
- integrity verification, install ordering, or no-later-modification attestation is false;
- any A0 root, count, rule, decision, alias identity, or denominator changes;
- any design claims installer-package identity proves installed-file identity;
- any private locator-bearing path, hash, source name, request bytes, output bytes, or source content
  beyond the permitted alias-only control paths reaches Git or Agent output;
- any implementation path falls outside section 11;
- any dependency, project, SDK, telemetry, or target-framework change is proposed;
- validation requires an unplanned source open during recovery;
- any record-only parent, path, blob, upstream, or clean-worktree check fails; or
- any independent finding remains unresolved.

## 14. Handoff

To resume:

1. verify the pre-amendment baseline and clean upstream;
2. review this amendment and applicable `AGENTS.md` files;
3. verify the exact plan-review record before implementation;
4. implement only section 11 with synthetic data;
5. publish and independently review the amended source candidate;
6. verify the amended tool-safety record before any private operation;
7. invoke the reviewed builder without opening its private preparation file or generated request;
8. have the project leader audit and approve the generated request;
9. execute the fixed-output CLI without inspecting generated private files;
10. leave exact private document review and approval to the project leader; and
11. never infer private authority from conversation history or the historical `9edbd57b` record.
