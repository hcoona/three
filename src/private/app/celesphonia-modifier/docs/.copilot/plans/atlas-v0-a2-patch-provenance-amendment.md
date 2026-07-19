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
noLaterGameInstallationModifications = true
```

The project leader must confirm that these statements describe both the A0 baseline procedure and
the current installation before preparing the discovery request. A false, missing, unknown, or
incomplete field is a safety refusal.

The final public intake-approval record may state only that the private attestation passed. It does
not contain the installer path, installer hash, request bytes, or installed-file details.

## 5. Contract versions and exact fields

The amendment replaces these private contract versions:

| Contract          | Existing                               | Amended                                |
| ----------------- | -------------------------------------- | -------------------------------------- |
| Discovery request | `atlas-intake-discovery-request/v1`    | `atlas-intake-discovery-request/v2`    |
| Confirmation      | `atlas-intake-confirmation-request/v1` | `atlas-intake-confirmation-request/v2` |
| Copy request      | `atlas-intake-copy-request/v1`         | `atlas-intake-copy-request/v2`         |
| Source-root map   | `atlas-source-root-map/v1`             | `atlas-source-root-map/v2`             |
| Intake state      | `atlas-intake-state/v1`                | `atlas-intake-state/v2`                |
| Copy receipt      | `atlas-copy-receipt/v1`                | `atlas-copy-receipt/v2`                |

Cleanup-preflight request, manifest, copy-plan, inventory, and cleanup-report versions remain
unchanged. State revision numbers, manifest revision numbers, canonical file names, and publication
order remain unchanged.

### 5.1 Discovery request

The v2 discovery request adds one required `officialPatch` object containing exactly:

- `sourceUrl`, exactly the public product URL in section 1;
- `versionLabel`, exactly `CN Patch v1.05.2`;
- `installerPath`, an explicit private absolute DOS path;
- `expectedInstallerSha256`, 64 lowercase hexadecimal characters; and
- `installationAttestation`, with exactly the four fields and values in section 4.

No current directory, profile, registry, Downloads folder, Steam manifest, or environment value is
used to infer the installer path or hash.

### 5.2 Confirmation and copy requests

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

### 5.3 Source-root map

`atlas-source-root-map/v2` adds one required `officialPatch` object containing exactly:

- public source URL;
- public version label;
- installer artifact alias;
- private installer absolute path;
- private lowercase SHA-256; and
- the closed installation attestation.

The installer path appears in no state or receipt.

### 5.4 State and receipt binding

Every `atlas-intake-state/v2` revision and `atlas-copy-receipt/v2` contains one required,
path-free `officialPatch` object with exactly:

- public source URL;
- public version label;
- installer artifact alias; and
- private lowercase installer SHA-256.

State revision 1 records discovery verification, revision 2 records approval-time revalidation,
and revision 3 plus the copy receipt record the final fresh pre-copy revalidation. State revision 4
inherits the exact state-3 binding and performs no installer open.

Validators require exact patch-object equality across root map, predecessor states, receipt,
inventory alias, and newly produced state. Any path/hash/alias/version/source/attestation mismatch
is a safety refusal.

### 5.5 Private inventory

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
retained. Cleanup preflight must always classify both rows as blocked by retained status and
private-retention disposition. A8 has no deletion authority for either unless a future
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

### Discovery

Discovery validates and hashes the installer before enumerating live game roots. It publishes the
v2 root map, inventory lineage, and state revision 1 only after the private hash and installation
attestation pass.

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

The project leader may direct Copilot to create and execute the deterministic private request after
the amended source-safety gate, provided:

- the operation uses only the exact canonical private request path;
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

- strict v2 request shape, duplicate, missing, null, unknown, trailing, URL, version, hash, and
  attestation validation;
- schema/DTO agreement for root-map v2, state v2, and receipt v2;
- ordinary-file, fixed-drive, containment, reserved-name, and component-reparse policy;
- held-stream length and write-denied path last-write stability;
- exact hash success plus mismatch, short-read, sharing, I/O, and cancellation failures;
- zero publication after failed installer validation;
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
3. all versioned contracts and schemas match section 5 exactly;
4. installer path and hash remain private in every success and failure;
5. all three fresh phases rehash and bind the exact installer as section 6 requires;
6. recovery and completed reruns open neither installer nor live source;
7. the human attestation is closed, exact, and bound through the v2 root map;
8. the installer inventory row is permanently retained and never cleanup-eligible;
9. no claim equates package hash with installed-file identity;
10. production code remains BCL-only with no project, package, lock, TFM, or telemetry change;
11. locked restore and warning-free build of the test project pass;
12. `dotnet format --verify-no-changes` passes for all three projects;
13. the complete Microsoft.Testing.Platform suite and direct apphost smoke tests pass;
14. evaluated project and package references remain within the approved graph;
15. exact no-renames path, line-length, LF, HK, `git diff --check`, tree, ancestry, upstream, and
    clean-worktree checks pass;
16. a fresh independent source reviewer reports exact `No findings`;
17. the amended tool-safety record is reviewed as an exact staged blob and published unchanged as
    the only child change; and
18. private discovery remains blocked until that record passes parent, path, blob, upstream, and
    clean-worktree verification.

## 11. Exact implementation path boundary

The amended implementation candidate may change only:

```text
src/private/app/celesphonia-modifier/
  Hcoona.CelesphoniaModifier.Atlas/
    AtlasDiscovery.cs
    AtlasIntakeContracts.cs
    PrivateArtifactLifecycle.cs
    TrustedLocalCopy.cs
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
    PrivateArtifactLifecycleTests.cs
    TrustedLocalCopyTests.cs
```

The README change records the amended contract status only. No CLI source, project, package, lock,
root configuration, new schema path, or new production/test file is authorized.

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
- any private path, hash, name, request, output, or source content reaches Git or Agent output;
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
7. create the private request without exposing its path or hash;
8. execute the fixed-output CLI without inspecting generated private files;
9. leave exact private document review and approval to the project leader; and
10. never infer private authority from conversation history or the historical `9edbd57b` record.
