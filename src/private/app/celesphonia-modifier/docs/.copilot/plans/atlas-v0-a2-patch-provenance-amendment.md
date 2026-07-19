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
contracts, section 8 CLI contract, section 10 source-profile attestation and strictly human-invoked
wording, section 16 comparison chain, and sections 17.2-17.4 request-preparation, invocation, and
release wording. It does not supersede the rule that Copilot and subagents never receive private
document bytes, the project leader personally audits private outputs, and only the project leader
can approve the pending manifest and later phases.

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
retainedInstallerObtainedFromDeclaredOfficialSource = true
retainedInstallerMatchesDeclaredVersionLabel = true
steamIntegrityCheckPassed = true
exactBoundInstallerAppliedAfterIntegrityCheck = true
noLaterSteamVerificationOrUpdate = true
noLaterPatchInstallationExecutionOrManagedContentChange = true
noLaterManualExecutableOrDefinitionEdit = true
```

The project leader must confirm that these statements describe both the A0 baseline procedure and
the current installation before preparing the discovery request. A false, missing, unknown, or
incomplete field is a safety refusal.

The first two booleans attest that the retained file came from the section 1 product URL and was
presented to the project leader as `CN Patch v1.05.2`. A false or uncertain origin/version claim
stops A2; the private package hash does not independently prove either claim.

The final three fields apply to Steam- or patch-managed executable and definition content. They do
not prohibit ordinary gameplay writes inside the two approved save roots. A later Steam
verification, Steam update, patch installation/execution, patch-managed content change, or manual
executable/definition edit invalidates the attestation even if the retained installer bytes remain
unchanged.

The final public intake-approval record may state only that the private attestation passed. It does
not contain the installer path, installer hash, request bytes, or installed-file details.

## 5. Contract versions and exact fields

The amendment replaces these private contract versions:

- Discovery request: `atlas-intake-discovery-request/v1` to
  `atlas-intake-discovery-request/v2`.
- Confirmation request: `atlas-intake-confirmation-request/v1` to
  `atlas-intake-confirmation-request/v2`.
- Copy request: `atlas-intake-copy-request/v1` to `atlas-intake-copy-request/v2`.
- Discovery preparation: none to `atlas-intake-discovery-preparation/v1`.
- Confirmation preparation: none to `atlas-intake-confirmation-preparation/v1`.
- Copy preparation: none to `atlas-intake-copy-preparation/v1`.
- Cleanup preparation: none to `atlas-cleanup-preflight-preparation/v1`.
- Preparation receipt: none to `atlas-request-preparation-receipt/v1`.
- Request review: none to `atlas-private-request-review/v1`.
- Source-root map: `atlas-source-root-map/v1` to `atlas-source-root-map/v2`.
- Intake state: `atlas-intake-state/v1` to `atlas-intake-state/v2`.
- Copy receipt: `atlas-copy-receipt/v1` to `atlas-copy-receipt/v2`.

Cleanup-preflight request, manifest, copy-plan, inventory, and cleanup-report versions remain
unchanged. State revision numbers, manifest revision numbers, and all existing canonical file names
remain unchanged.

The only publication-order exception is discovery: v2 publishes the source-root map before the
pending manifest so the root map is the phase's patch-bound recovery marker. Section 7 defines the
closed v1/v2 transition.

### 5.1 Reviewed request preparation

The amended C# CLI adds these fixed-output commands:

```text
celesphonia-atlas intake-prepare-discovery <repository-root> <survey-alias>
celesphonia-atlas intake-prepare-confirmation <repository-root> <survey-alias>
celesphonia-atlas intake-prepare-copy <repository-root> <survey-alias>
celesphonia-atlas intake-prepare-preflight <repository-root> <survey-alias>
```

The project leader creates each private preparation file and supplies the human-only facts. The
preparation files occupy fixed, alias-only repository-relative suffixes:

```text
src\private\app\celesphonia-modifier\.private\atlas-v0\
  operator-input\<surveyAlias>\discover.json
  operator-input\<surveyAlias>\confirm.json
  operator-input\<surveyAlias>\copy.json
  operator-input\<surveyAlias>\cleanup-preflight.json
  operator-input\<surveyAlias>\<phase>.receipt.json
  operator-input\<surveyAlias>\<phase>.review.json
```

That directory is outside the surveyed workspace and its closed census. The C# CLI itself is the
only driver. Every private-phase command accepts exactly an explicit absolute canonical
`repository-root` and the exact public `survey-000001` alias. It rejects relative/device/UNC roots,
the current directory as an implicit root, another survey alias, and every visible reparse
component.

The CLI derives the one fixed preparation or request path from those two arguments, then requires
the parsed private document's `projectRoot`, `workspaceRoot`, and `surveyAlias` to match. The
arguments reveal no game/save/installer locator, source file name, or digest. Copilot may pass them
to a reviewed command but may not read or display the derived file.

Discovery preparation contains its preparation `schemaVersion` and every other v2
discovery-request field except `officialPatch.expectedInstallerSha256`, `preparationPath`,
`expectedPreparationSha256`, and `preparationArtifactAlias`. The builder:

1. strictly validates the preparation shape and canonical alias-only paths;
2. validates and hashes only the explicit installer using section 6 steps 1-8 and 10, requiring
   metadata stability but deferring expected-hash equality to human approval and discovery;
3. calculates the canonical preparation-file digest and reserves four consecutive aliases;
4. inserts both digests, the canonical preparation path, and the reserved aliases into the request;
5. serializes canonical UTF-8 bytes;
6. creates the request at its one canonical workspace path, or accepts an exact byte match;
7. publishes the preparation receipt last; and
8. emits only `Intake request prepared.` or the existing classified diagnostic.

The project leader opens the generated request, audits its private path, digest, attestation, and
all existing A2 request fields, then creates the approved request-review document before discovery.
This human review turns the builder-calculated digest into the expected installer binding that
discovery rehashes.

Confirmation preparation contains exactly `schemaVersion`, `surveyAlias`, `projectRoot`,
`workspaceRoot`, `approved`, `decisionCommit`, `expectedPendingManifestSha256`, and
`discoveryOutputsHumanAudited = true`. `approved` must be `true`. It reads sealed canonical
state/root-map/plan/inventory evidence, requires the human-supplied manifest digest to match,
projects the root-map patch binding, and builds the exact v2 confirmation request at the existing
canonical path.

Copy preparation contains exactly `schemaVersion`, `surveyAlias`, `projectRoot`, `workspaceRoot`,
`expectedApprovedStateSha256`, `decisionCommit`, and
`confirmationOutputsHumanAudited = true`. It reads sealed canonical state/root-map/plan/inventory
evidence, requires the human-supplied state and decision values to match, projects the root-map
patch binding, and builds the exact v2 copy request at the existing canonical path.

Cleanup-preflight preparation contains exactly `schemaVersion`, `surveyAlias`, `projectRoot`,
`workspaceRoot`, `expectedQualifiedStateSha256`, `proposedMilestone`, and
`copyOutputsHumanAudited = true`. It reads sealed state-3/inventory evidence and builds the existing
cleanup-preflight request at its canonical path.

Preparation never opens live game sources. Confirmation, copy, and preflight preparation never open
the installer. Every builder is create-new or exact-byte-idempotent, supports cancellation, writes
no partial final artifact, never overwrites different bytes, and never emits private values or
exception details. Preparation, request, receipt, and review files remain private. The tool does not
delete them.

Fresh discovery preparation also requires the canonical workspace census to contain no A2 operation
output from a prior v1 or v2 attempt. Its only permitted existing generated request is an exact byte
match to the new v2 request. It never migrates, deletes, or overwrites an earlier request or output.

Before any operation output exists, a builder rerun may revalidate its input and accept only the
exact existing request and receipt bytes. After a valid successor state exists, the builder returns
success from sealed state evidence without opening the custody bundle, installer, or live source.
Any intermediate operation output causes the builder to refuse; only the corresponding operation
may enter its section 7 recovery path.

A request without its final receipt is a preparation-publication seam, not a reviewable request.
Operations refuse it. Only the same builder may validate the exact request and finish the receipt;
abandoning that seam stops A2 and retains both files for separately reviewed remediation.

The phase tokens are exactly `discover`, `confirm`, `copy`, and `cleanup-preflight`. Each
`atlas-request-preparation-receipt/v1` contains exactly:

- `schemaVersion`, `surveyAlias`, `phase`, and `sourceInventorySha256`;
- `preparationArtifact`, with alias, canonical path, digest, purpose, schema version, and lifecycle;
- `requestArtifact`, with alias, canonical path, digest, purpose, schema version, and lifecycle;
- `receiptArtifactAlias` and `reviewArtifactAlias`; and
- `reviewPath`, the fixed `<phase>.review.json` path.

The receipt is the pre-execution custody record. It reserves four consecutive aliases in this order:
preparation, request, receipt, review. It binds exact bytes and lifecycle even when the project
leader later rejects or abandons the request.

After auditing the request, the project leader creates `atlas-private-request-review/v1` at the
fixed review path. It contains exactly:

- `schemaVersion`, `surveyAlias`, `phase`, and `reviewerRole = project-leader`;
- `decision`, exactly `approved` or `rejected`;
- `preparationReceiptSha256` and `requestSha256`;
- `requestFieldsReviewed = true` and `privateValuesRemainPrivate = true`; and
- `priorOutputAuditRequired` and `priorOutputAuditPassed`.

Discovery review requires both prior-output booleans to be `false`. Confirmation, copy, and
preflight require both to be `true` and require exact agreement with their preparation input. A
missing review returns approval-required. A rejected review returns approval-required and stops A2.
The request, receipt, preparation, and review then remain under private project-leader custody; this
plan authorizes no deletion, migration, reuse, or alias release. An abandoned bundle means the
receipt exists without a review and has the same stop-and-retain outcome.

Every v2 discovery, confirmation, and copy request also contains exactly:

- `preparationPath`, the canonical absolute path of its fixed alias-only preparation file;
- `expectedPreparationSha256`, the builder-calculated lowercase digest; and
- `preparationArtifactAlias`, the next inventory alias at builder time.

A fresh operation reopens only that preparation file with read access and write/delete sharing
denied, strictly revalidates its schema, digest, canonical path, reserved alias, and exact
field-to-request projection. It also validates the receipt, request, and review digests, paths,
aliases, decisions, and audit booleans. It imports all four rows into the private inventory before
adding phase outputs. Discovery separately performs its fresh installer hash comparison.

The exact imported lineage is: preparation `[]`; request `[preparation]`; receipt
`[preparation, request]`; and review `[request, receipt]`. Successor state binds the request,
receipt, and review digests. Completed-state and recovery paths trust those sealed bindings and
never reopen the custody bundle.

### 5.2 Exact CLI grammar and help

The global help bytes become exactly:

```text
Usage:
  celesphonia-atlas empty-survey
  celesphonia-atlas intake-prepare-discovery <repository-root> <survey-alias>
  celesphonia-atlas intake-prepare-confirmation <repository-root> <survey-alias>
  celesphonia-atlas intake-prepare-copy <repository-root> <survey-alias>
  celesphonia-atlas intake-prepare-preflight <repository-root> <survey-alias>
  celesphonia-atlas intake-discover <repository-root> <survey-alias>
  celesphonia-atlas intake-confirm <repository-root> <survey-alias>
  celesphonia-atlas intake-copy <repository-root> <survey-alias>
  celesphonia-atlas cleanup-preflight <repository-root> <survey-alias>

Commands:
  empty-survey                 Write a deterministic empty Atlas survey.
  intake-prepare-discovery     Prepare an intake discovery request.
  intake-prepare-confirmation  Prepare an intake confirmation request.
  intake-prepare-copy          Prepare an intake copy request.
  intake-prepare-preflight     Prepare a cleanup preflight request.
  intake-discover              Discover the approved Atlas intake scope.
  intake-confirm               Confirm an approved Atlas intake manifest.
  intake-copy                  Create qualified Atlas research snapshots.
  cleanup-preflight            Report private-artifact cleanup eligibility.

Options:
  -h, --help  Show help.
```

Every private-phase command uses this exact command-help text:

```text
Usage: celesphonia-atlas <command> <repository-root> <survey-alias>

Options:
  -h, --help  Show help.
```

Empty-survey help bytes remain unchanged. The parser precedence is:

1. one global help token;
2. exact two-token empty-survey or recognized private-phase command help;
3. exact one-token `empty-survey`;
4. exact three-token recognized preparation command with root and alias;
5. exact three-token recognized request command with root and alias; then
6. fixed `Invalid arguments.` with exit 2.

Unknown commands, empty/whitespace arguments, extra arguments, mixed help, a help token used as an
operation argument, or any legacy preparation/request-file operand never invoke an operation.
Preparation success is exactly
`Intake request prepared.` on stdout. All existing error bytes, exit classifications, cancellation
precedence, stdout/stderr write precedence, and no-exception-detail rules remain byte-for-byte.
Every help and diagnostic line uses LF, and each complete help or success payload ends in exactly
one LF.

### 5.3 Discovery request

The v2 discovery request adds one required `officialPatch` object containing exactly:

- `sourceUrl`, exactly the public product URL in section 1;
- `versionLabel`, exactly `CN Patch v1.05.2`;
- `installerPath`, an explicit private absolute DOS path;
- `expectedInstallerSha256`, 64 lowercase hexadecimal characters; and
- `installationAttestation`, with exactly the eight fields and values in section 4.

No current directory, profile, registry, Downloads folder, Steam manifest, or environment value is
used to infer the installer path or hash.

### 5.4 Confirmation and copy requests

The v2 confirmation and copy requests each add required
`expectedOfficialPatchInstallerSha256`. They obtain the installer path from the strictly validated
source-root map bound through the preceding state.

Hash equality is one closed chain:

- request `expectedOfficialPatchInstallerSha256`;
- root-map provenance `installerSha256`;
- predecessor-state binding `installerSha256`; and
- freshly calculated installer SHA-256.

Alias equality is a separate closed chain:

- root-map provenance `installerArtifactAlias`;
- predecessor-state binding `installerArtifactAlias`;
- the one inventory row's `artifactAlias`; and
- the successor state or receipt binding `installerArtifactAlias`.

The inventory row must also match the exact class, purpose, custody, retention, status, and
verification tuple in section 5.7. Source URL and version label compare separately against their
public constants and every path-free binding.

Completed-phase validation and recovery trust sealed private evidence and perform no installer or
live-source open.

### 5.5 Source-root map

`atlas-source-root-map/v2` adds one required `officialPatchProvenance` object containing exactly:

- `sourceUrl`, the public source URL;
- `versionLabel`, the public version label;
- `installerArtifactAlias`, the inventory alias;
- `installerPath`, the private absolute path;
- `installerSha256`, the private lowercase digest; and
- `installationAttestation`, the closed attestation.

The installer path appears in no state or receipt.

### 5.6 State and receipt binding

Every `atlas-intake-state/v2` revision and `atlas-copy-receipt/v2` contains one required,
path-free `officialPatchBinding` object with exactly:

- `sourceUrl`, the public source URL;
- `versionLabel`, the public version label;
- `installerArtifactAlias`, the inventory alias; and
- `installerSha256`, the private lowercase digest.

State revision 1 records discovery verification, revision 2 records approval-time revalidation,
and revision 3 plus the copy receipt record the final fresh pre-copy revalidation. State revision 4
inherits the exact state-3 binding and performs no installer open.

The implementation uses distinct full-provenance and path-free-binding types. It defines one
`ToBinding()` projection from root-map provenance and permits exact equality only among projected
or path-free binding objects. Root-map attestation is validated in full and remains transitively
bound through the existing root-map document digest in every state.

Inventory validation separately requires one installer row with the exact alias, class, purpose,
retention tuple, and verification method from section 5.7, plus source-root-map direct lineage to
that alias. Any projection/hash/alias/version/source/attestation/inventory mismatch is a safety
refusal.

### 5.7 Private inventory

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

The source-root-map inventory entry supplements, rather than replaces, its existing lineage. Its
exact ordered `lineageAliases` value is `[pendingManifestAlias, installerArtifactAlias]`. Alias
allocation remains consecutive and uses the existing monotonic cursor.

The source-root-map inventory entry is also changed to `lastUseMilestone = post-A8-appeal`,
`expiryCondition = never`, `plannedDisposition = retain-private`, and `status = retained`.

The installer package and the source-root map holding its private hash evidence are permanently
retained. The existing cleanup report remains single-result: it returns `blocked-status` for both
rows because status is evaluated first, while separately preserving and validating
`plannedDisposition = retain-private`. A8 has no deletion authority for either unless a future
independently approved plan explicitly reverses the project-leader decision.

Each preparation file receives one inventory row immediately before its generated-request row:

```text
artifactClass = private-evidence
purpose = <phase-specific value below>
custodianRole = project-leader
lineageAliases = []
lastUseMilestone = A2
expiryCondition = after:A2
plannedDisposition = delete
status = present
verificationMethod = <exact preparation schema version>
qualification = null
```

The exact preparation purpose values are `request-preparation:discover`,
`request-preparation:confirmation`, `request-preparation:copy`, and
`request-preparation:cleanup-preflight`. The corresponding verification method is that phase's
preparation schema version from section 5.

The other imported rows use the same custody, last-use, expiry, disposition, status, and
qualification values. Their distinct fields are:

- request: `private-evidence`, existing `request:<phase>` purpose, existing
  `atlas-cli:<command>` verification;
- receipt: `private-provenance`, `request-preparation-receipt:<phase>` purpose, receipt schema
  verification; and
- review: `private-evidence`, `request-review:<phase>` purpose, review schema verification.

Before an approved operation, the receipt governs the private custody bundle outside the survey
census. The operation imports all four rows into the private-artifact inventory and A8 lifecycle.
A2 performs no deletion. Any later cleanup requires the existing reviewed status-transition and
human-approval process; builders and intake operations have no cleanup authority.

Alias allocation replaces each pre-amendment request position with the consecutive
`[preparation, request, receipt, review]` quartet, then preserves every later relative order. It
inserts the discovery installer alias immediately before the source-root-map alias. Recovery
validates those exact consecutive ordinals and lineages.

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
refusal. A request-only-v1 workspace means a canonical v1 discovery request exists with no other A2
operation output; it receives the same safety refusal.

The tool and this plan authorize no deletion, archive, migration, reuse, replacement, or second
survey identity for any v1 or ambiguous bytes. A2 stops for a separately persisted and independently
reviewed A0/A2 remediation decision. The one-shot survey identity remains unchanged. Confirmation,
copy, and preflight require state v2, so v1 state cannot enter their recovery paths.

### Discovery

Fresh discovery requires the approved discovery request-review document. It validates and hashes
the installer before enumerating live game roots. It publishes the v2 root map first, then the
pending manifest and later outputs. Inventory lineage and state revision 1 are published only after
the private hash and installation attestation pass.

### Confirmation

Fresh confirmation requires public decision commit `A`, the preparation's discovery-output audit,
and the approved confirmation request-review document. It validates state revision 1 and every
bound private document, rehashes the installer, and publishes state revision 2 only after exact
equality. A completed valid state-2-or-later rerun returns from sealed evidence without opening the
installer or live sources.

### Copy

Fresh copy requires the preparation's confirmation-output audit and the approved copy
request-review document. It validates state revision 2 and every bound document, rehashes the
installer before opening any live game source, and carries the exact path-free binding into the
receipt and state revision 3.

A missing, changed, unstable, reparse-backed, or mismatched installer before fresh state revision 3
stops A2. It does not authorize recopying or replacement with another package.

### Recovery and preflight

Recovery after any output-publication seam validates sealed bytes only. It never rehashes the
installer and never opens live game sources. A complete staged receipt and finalization recovery
uses the patch binding already sealed before copying. Fresh cleanup preflight requires the
preparation's copy-output audit and approved preflight request review. It validates state revision 3
and inherits its patch binding into state revision 4 without opening the installer.

## 8. Privacy and operator boundary

The project leader may direct Copilot to invoke the deterministic C# request builder and execute the
resulting private request after the amended source-safety gate, provided:

- the CLI receives only the explicit verified repository root and `survey-000001`;
- the project leader audits and approves the generated request before its operation;
- the C# CLI derives and validates the exact preparation or request path;
- no command output, retained transcript, or Agent message contains the private request, installer
  path, installer hash, source paths, manifest bytes, or generated private records;
- Copilot does not open, parse, display, search, summarize, or attach generated private files;
- the CLI emits only its fixed success or classified terminal diagnostic;
- the project leader alone opens and reviews the generated private documents; and
- only approved aggregate counts, public identifiers, and difference categories return to Git or
  an Agent transcript.

Executing the reviewed CLI is not authority to inspect its inputs or outputs. Any unexpected
process output, exception detail, or private disclosure is a stop condition.

The four builders prepare discovery, confirmation, copy, and cleanup-preflight requests. Copilot may
invoke an operation only after the project leader creates the matching approved review document and
authorizes the exact root/alias command.

### 8.1 Exact direct-apphost invocation

The amended tool-safety record binds this procedure and its relative apphost path. The only runtime
substitution is the current environment's already-known absolute repository root. It may appear
only as the local command argument, never in Git or CLI output. No other substitution is allowed:

```powershell
$repositoryRoot = "<verified absolute repository root>"
$surveyAlias = "survey-000001"
$atlasRoot = Join-Path $repositoryRoot "src\private\app\celesphonia-modifier"
$apphost = Join-Path $atlasRoot `
  "Hcoona.CelesphoniaModifier.Atlas.Cli\bin\Debug\net10.0\celesphonia-atlas.exe"
```

After `T` is the clean upstream-equal `HEAD`, Copilot may run exactly:

```powershell
& $apphost intake-prepare-discovery $repositoryRoot $surveyAlias
```

The project leader audits the generated request and creates its approved request-review document
before Copilot may run:

```powershell
& $apphost intake-discover $repositoryRoot $surveyAlias
```

After discovery audit and publication of `A`, the same prepare/audit/operate sequence uses:

```powershell
& $apphost intake-prepare-confirmation $repositoryRoot $surveyAlias
& $apphost intake-confirm $repositoryRoot $surveyAlias
& $apphost intake-prepare-copy $repositoryRoot $surveyAlias
& $apphost intake-copy $repositoryRoot $surveyAlias
```

Those four lines are separate invocations; each operation line requires project-leader audit and an
approved request-review document first. After copy-output audit, Copilot may prepare preflight:

```powershell
& $apphost intake-prepare-preflight $repositoryRoot $surveyAlias
```

After the project leader audits that request and creates its approved review document, Copilot may
run:

```powershell
& $apphost cleanup-preflight $repositoryRoot $surveyAlias
```

Each invocation runs without transcription, verbose/debug output, piping, redirection, or argument
logging. Exit 0 must carry exactly the command's one fixed success line. A nonzero exit must carry
exactly one classified fixed diagnostic line. Any additional/different output or a Git-gate
mismatch stops execution before another command. Copilot reports only the fixed line and exit code;
it never lists or opens a derived control file.

## 9. Synthetic test requirements

All tests use synthetic installer bytes and paths. They cover:

- strict preparation shapes, canonical alias-only paths, builder fixed output, create-new behavior,
  exact-byte idempotence, cancellation, and no-private-output failures;
- absolute repository-root and exact-survey argument validation, derived control paths, and
  relative/current-directory/device/UNC/outside-root/wrong-name/wrong-survey rejection;
- exact global/private-phase help bytes, parser precedence, extra/mixed argument rejection, stream
  failure precedence, and zero-operation help behavior;
- discovery preparation hash generation followed by explicit human-approval simulation and fresh
  discovery rehash comparison;
- confirmation/copy preparation from sealed evidence without installer or live-source opens;
- receipt-last publication, four-alias custody order, strict private review decisions, exact
  lineage, approved inventory import, and completed/recovery zero custody-bundle opens;
- missing/rejected/abandoned review behavior, durable pre-execution custody, zero deletion, and
  mandatory replanning;
- discovery-to-confirmation, confirmation-to-copy, and copy-to-preflight output-audit gates;
- strict v2 request shape, duplicate, missing, null, unknown, trailing, URL, version, hash, and
  attestation validation;
- schema/DTO agreement for preparation receipt v1, root-map v2, state v2, and copy receipt v2;
- ordinary-file, fixed-drive, containment, reserved-name, and component-reparse policy;
- held-stream length and write-denied path last-write stability;
- exact hash success plus mismatch, short-read, sharing, I/O, and cancellation failures;
- zero publication after failed installer validation;
- source-root-map-v2-first publication and no-rehash recovery only after that marker validates;
- fail-closed handling for every synthetic partial-v1/v2 artifact combination;
- fresh discovery hashing before live-root enumeration;
- fresh confirmation and copy detecting installer replacement;
- exact root-map/state/receipt/inventory alias and hash continuity;
- exact source-root-map ordered lineage `[pendingManifestAlias, installerArtifactAlias]`;
- permanent inventory retention and cleanup-preflight blocking;
- completed discovery/confirmation/copy/preflight performing zero installer and source opens;
- every existing output-publication and recovery seam preserving prior no-reopen behavior;
- synthetic private path/hash sentinels absent from stdout, stderr, exceptions, and canonical
  repository-safe outputs;
- all existing A0 corpus counts, roots, rules, aliases, and decisions unchanged; and
- every pre-amendment A2 regression test.

## 10. Acceptance criteria

The amended implementation gate passes only when:

1. the exact plan candidate and plan-review record satisfy section 2;
2. only the paths in section 11 change from the verified amendment plan-review record;
3. the four C# builders publish receipt-bound requests, and every operation requires the exact
   approved private review without exposing private values to Copilot;
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

Full A2 release additionally requires:

1. the intake-approval record contains every safe discovery fact in section 11.2;
2. confirmation, copy, and preflight complete under the approved decision commit;
3. the private-run-acceptance record contains every safe final fact in section 11.2;
4. a fresh reviewer reports exact `No findings` on the complete cumulative candidate;
5. the release-gate record is reviewed and published as the exact record-only child; and
6. the release record is the clean shared branch tip with the exact chain in section 11.1.

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
      request-preparation-receipt.schema.json
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

The README change records the amended contract status only. The two listed CLI files authorize the
four fixed-output preparation commands and explicit root/alias grammar for every private phase. The
two listed new C# files authorize the BCL-only request builder and its synthetic tests. No project,
package, lock, root configuration, new schema path, or other production/test file is authorized.

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

The remaining repository-safe record paths are:

```text
src/private/app/celesphonia-modifier/docs/.copilot/reviews/
  atlas-v0-a2-intake-approval.md
  atlas-v0-a2-private-run-acceptance.md
  atlas-v0-a2-release-gate.md
```

### 11.1 Superseding commit and record chain

The following roles define exact commits:

- `P` is the final amendment-plan candidate. Relative to `9edbd57b`, its cumulative no-renames diff
  contains exactly the four plan-candidate paths in section 11.
- `B` is the immediate child of `P`. It adds only
  `atlas-v0-a2-patch-provenance-plan-review.md`, with the independently reviewed staged blob
  unchanged. `B` is the amended implementation and all later cumulative-review diff base.
- `S` is the final amended source candidate descended from `B`. The cumulative no-renames diff
  `B..S` contains exactly the implementation paths in section 11 and no record path.
- `T` is the immediate child of `S`. It adds only
  `atlas-v0-a2-patch-provenance-tool-safety-review.md`, unchanged from its independently reviewed
  staged blob.
- `A` is the immediate child of `T`, created only after private discovery and project-leader review.
  It adds only `atlas-v0-a2-intake-approval.md`, unchanged from its independently reviewed staged
  blob. `A` is the public `decisionCommit` used by confirmation and copy.
- `R` is the immediate child of `A`, created only after successful confirmation, copy, and cleanup
  preflight. It adds only `atlas-v0-a2-private-run-acceptance.md`, unchanged from its independently
  reviewed staged blob.
- `G` is the immediate child of `R`. It adds only `atlas-v0-a2-release-gate.md`, unchanged from its
  independently reviewed staged blob, and becomes the A2 shared-branch tip.

The source-safety review examines exact `B..S`. The final fresh independent review examines exact
`B..R`; that cumulative no-renames diff may contain only the implementation paths plus the tool-
safety, intake-approval, and private-run-acceptance record paths. The plan-review record is the base
and is not repeated in either diff. The release record reviews and cites the exact `B..R` candidate,
then verifies `G` as its one-path record-only child.

Every role must be a pushed commit with verified parent, tree, allowed no-renames paths, staged blob
identity where applicable, upstream equality, and clean worktree. A code, schema, test,
documentation, dependency, build, request-contract, or private-procedure change after `S`
invalidates `T` and all descendants; the chain restarts from `B` with a new source candidate. No old
A2 plan/tool record substitutes for `B` or `T`.

This chain supersedes the original A2 plan's section 16 comparison base and section 17.4 candidate
sequence. All other record-review mechanics remain governing.

### 11.2 Mandatory repository-safe private-run evidence

`atlas-v0-a2-intake-approval.md` records exactly these safe facts after the project leader audits
the generated discovery request, pending manifest, v2 root map, state revision 1, and inventory:

- survey alias, pending-manifest revision, Steam application/build, public patch URL/version, and
  the existing approved A0 aggregate counts;
- `discoveryRequestPrepared = true`;
- `discoveryRequestReviewApproved = true`;
- `discoveryRequestHumanAudited = true`;
- `discoveryOutputsHumanAudited = true`;
- `installationAttestationPassed = true`;
- `installerHashRevalidatedAtDiscovery = true`;
- `sourceRootMapV2PublishedFirst = true`;
- `v1OrAmbiguousArtifactsObserved = false`;
- `discoveryCustodyBundleInventoryBindingPassed = true`;
- `permanentInstallerCustodyRowPassed = true`;
- `permanentHashEvidenceCustodyRowPassed = true`;
- `privateDisclosureObserved = false`; and
- `projectLeaderDecision = approved`.

The record contains no installer hash/path/name/metadata, preparation/request bytes, private
document digest, source path/name, per-file result, or installed-file identity claim.

`atlas-v0-a2-private-run-acceptance.md` records exactly these safe facts after the project leader
audits state revisions 2-4, copy receipt v2, final inventory, and cleanup report:

- the approved survey alias, public decision commit `A`, public patch URL/version, Steam
  application/build, and approved aggregate copy counts;
- `installerHashRevalidatedAtConfirmation = true`;
- `installerHashRevalidatedAtCopy = true`;
- `patchBindingContinuityPassed = true`;
- `stateV2RevisionsOneThroughFourPassed = true`;
- `copyReceiptV2Passed = true`;
- `confirmationRequestReviewApproved = true`;
- `confirmationOutputsHumanAudited = true`;
- `copyRequestReviewApproved = true`;
- `copyOutputsHumanAudited = true`;
- `preflightRequestReviewApproved = true`;
- `preflightOutputsHumanAudited = true`;
- `requestReviewBindingsPassed = 4`;
- `confirmationCustodyBundleInventoryBindingPassed = true`;
- `copyCustodyBundleInventoryBindingPassed = true`;
- `preflightCustodyBundleInventoryBindingPassed = true`;
- `permanentCustodyRowsPresent = 2`;
- `permanentCustodyCleanupResult = blocked-status`;
- `cleanupInvalidRows = 0`;
- `cleanupDeletionsPerformed = 0`;
- `unexpectedRecoveryObserved = false`;
- `privateDisclosureObserved = false`; and
- `packageHashClaimLimitedToPackageIdentity = true`.

These are project-leader attestations based on private document review and fixed CLI outcomes, not
public reproduction of private evidence. A false, unknown, missing, or non-applicable value stops
release. The final cumulative reviewer checks record completeness, chain consistency, public-value
agreement, and absence of private fields. `atlas-v0-a2-release-gate.md` cites `B`, `S`, `T`, `A`,
`R`, the exact final-review result, and all parent/path/blob/upstream/clean-worktree checks.

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
The line-length gate requires every line in this new amendment and every added/modified line in the
four-path plan diff to be at most 100 characters. Unchanged baseline lines outside the diff do not
belong to this candidate gate.

## 13. Stop conditions

Stop and return to planning if:

- the exact retained installer is unavailable;
- installer bytes or private SHA-256 change;
- the public source URL or selected version label changes;
- the Steam public build changes;
- integrity verification, install ordering, or no-later-modification attestation is false;
- any A0 root, count, rule, decision, alias identity, or denominator changes;
- any v1 or ambiguous request, output, staging, or recovery artifact is present;
- any custody bundle is missing, inconsistent, rejected, or abandoned after preparation succeeds;
- any required request review or prior-output audit is absent or not approved;
- any design claims installer-package identity proves installed-file identity;
- any private locator-bearing path, hash, source name, request bytes, output bytes, or source
  content beyond the permitted public root/alias arguments reaches Git or Agent output;
- any implementation path falls outside section 11;
- any dependency, project, SDK, telemetry, or target-framework change is proposed;
- validation requires an unplanned source open during recovery;
- any required section 11.2 safe fact is false, unknown, missing, or non-applicable;
- any tracked change occurs between `S` and `G` outside the exact record-only chain;
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
