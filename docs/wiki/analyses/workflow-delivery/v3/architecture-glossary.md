# Workflow Delivery v3 Architecture Glossary

## Status

Architecture version: **v3**.

This is the normative glossary for the clean v3 implementation line. It records
terms and principles confirmed during architecture review before comparison
with v1 or v2 implementation details.

Confirmed entries should remain stable. Open terms are explicitly identified and
must not be treated as settled architecture.

## System Framing

The delivery architecture contains two peer business systems:

- **CI Qualification**
- **Release Delivery**

They share mechanism-level capabilities but retain separate policies, plans,
evidence, and decisions.

An external governance boundary controls authority across both systems.

## System Boundaries

### CI Qualification

The business system that determines which quality obligations apply to a change,
executes or delegates those obligations, and produces an explainable qualification
decision.

CI Qualification is change-centric. It does not own release authorization or
publication side effects.

### Release Delivery

The business system that accepts an explicit release intent for an immutable
revision, rebuilds the release outputs, performs its own required quality checks,
obtains authorization, publishes outputs, and records the resulting external state.

Release Delivery does not reuse artifacts produced by pull request builds and does
not consume CI check results as release evidence.

### Shared Foundation

The mechanism-level foundation shared by CI Qualification and Release Delivery.

It may define stable identity, revision, digest, fact, artifact, provenance,
contract, Repository Model compilation, and execution-capability primitives.
It does not own an aggregate root, scheduler, universal record envelope, CI
scope policy, release-channel policy, approval policy, or final business
decisions.

### Delivery Governance

The authority boundary that determines who may define policy, approve protected
actions, grant execution capabilities, and accept decisions.

Delivery Governance is not a third business system. It is a higher-authority
boundary implemented through facilities such as repository rules, protected
environments, identity federation, credential scopes, review policy, and audit
configuration.

CI Qualification and Release Delivery may request decisions or capabilities, but
they must not grant final authority to themselves.

### Platform-Orphan Exception

A protected, case-specific Governance authority for cleanup probe run
`32809578776`, which remains externally nonterminal after the reviewed
terminalization attempts failed. It is a singleton current-source document, not
a reusable exception category or registry.

A Governance-only read-only reconciliation invocation may propose excluding
only that run from its terminalization blocker set after same-invocation
current-source, platform, and package admission. The exception grants no
reconciliation authority and permits only one protected consumption. It does
not rewrite platform state, hide the run, establish acceptance success,
authorize mutation, or satisfy Live activation. Only the protected merge of a
change that atomically retains the unchanged candidate result and converts
active authority to cross-bound inert audit facts consumes the exception and
makes the as-observed historical result authoritative. Any later use or
different run requires separate human approval and normative design change.

### Decision Zone

The trusted runtime zone that executes authoritative policy, planning, evidence
admission, and final decision logic.

It executes same-revision control code but not target-defined project/build
hooks and does not directly hold publication credentials. Official control code
is owner-reviewed; first-slice Buddy control code may be branch-controlled under
the accepted exception.

### Build and Qualification Zone

The unprivileged runtime zone that may execute code from a pull request or
release target.

It performs build, test, lint, package, and other quality work. It produces
immutable artifacts and evidence but does not hold publication credentials or
grant authority to itself.

### Side-Effect Zone

The narrowly privileged runtime zone that receives a scoped publication
capability only after qualification and governance approval.

It consumes verified immutable artifacts and a fully materialized publish
request. It normally must not check out or execute code from the release target.
The First-Slice Buddy Risk Exception instead runs target-revision publisher code
after dedicated Environment approval. Its output is a publication receipt for
reconciliation.

### Buddy

A Release Delivery policy channel for distributable, non-authoritative preview
releases.

Buddy may produce externally installable outputs. Normally its target
eligibility is channel policy. For the first `hcoona-release-smoke-npm` live
GitHub Packages slice, any same-repository ref selected by
`workflow_dispatch` is eligible without protected-ref or CODEOWNERS approval.
It uses the same Release planning, build, qualification, evidence, and
side-effect lifecycle as Official, but publishes only through isolated channel,
destination, package-coordinate, and Capability boundaries.

Buddy does not occupy an Official canonical destination or create an
authoritative production release record.
Buddy artifacts and evidence are not promoted to Official.

Buddy uses the frozen native NBGV product version unchanged. Buddy and Official
may therefore have the same version string while retaining distinct complete
publication coordinates and authority boundaries.

Buddy is not a separate release implementation and is not merely another name
for dry-run.

### First-Slice Buddy Risk Exception

The explicitly accepted bounded trust exception for live publication of the
dedicated disposable `hcoona-release-smoke-npm` package to GitHub Packages.
It was reopened and reconfirmed before LLD on 2026-08-06.

The exact selected target supplies workflow, control, Planner, Finalizer, and
publisher code. After the exact Publication Snapshot is sealed, a dedicated
protected Buddy Environment provides human trust elevation. Only after approval
does the target-revision side-effect job receive short-lived `GITHUB_TOKEN` with
minimum `packages: write`; it receives no PAT and no `id-token: write`.

Approval is not cryptographic or independent semantic validation. A protected
independent publisher does not constrain malicious target code after approval.
Environment approval is mandatory against mistakes and ordinary process
violations, but is not a non-bypassable `GITHUB_TOKEN` permission ceiling
against a malicious repository writer.

Every repository actor with Write, Maintain, or Admin access is inside the Buddy
trusted publisher TCB. External/fork contributors and actors without repository
write are outside it and cannot manually dispatch the live path under normal
permissions. A trusted writer can create alternate workflow YAML or jobs with
`packages: write`. Optional workflow-execution protections are defense in depth,
not a required dependency or permission ceiling. Reviewer context,
package/destination isolation, minimum normal-flow permissions, no-consumer
policy, forbidden ordinary admin actions, and Break-Glass handling bound rather
than eliminate the risk. Activation records actual token permissions and
package/repository grants, verifies Official and known production isolation,
and uses safe denial probes only for enumerated unrelated assets. It does not
claim universal negative reach proof; other reachable package operations under
the smallest configured grants are accepted writer-TCB risk. If any writer is
no longer trusted to publish, the slice blocks until that actor's repository
access is reduced below
Write/Maintain/Admin or package-write Capability and destination access are
placed behind an independently enforced publisher boundary unavailable to
writer-authored workflows. Ref narrowing, Environment branch restrictions,
CODEOWNERS, and workflow-execution protections are insufficient remediation by
themselves while an untrusted writer can author alternate workflows with
`packages: write`. Official and future Buddy destinations or production
packages do not inherit the exception.

The no-consumer policy is permanently enforced through repository-wide HK
scanning of dependency manifests, lockfiles, workflows, install scripts, and
dependency configuration on dependency-surface changes and every
`slice-validation`. Human Governance re-attests writer-TCB membership and
package/repository/Manage Actions access at least every 90 days and after
relevant role, team, or permission changes. An authorized human promptly
commits `live_enabled: false` to the policy-fixed protected attestation pending
reacceptance, while attestation expiry independently bounds stale normal flows.
Protected review, merge, and fresh-read latency make this bounded operational
response rather than instantaneous platform disablement.

Activation also requires direct repository-wide legacy Buddy retirement. The
implementation PR lands with `live_enabled: false`, removes both legacy Buddy
workflow files, and preserves no compatibility route. Governance freezes Buddy
dispatch, disables both `buddy.yml` and `release-buddy.yml`, drains or cancels
queued, waiting, approval-pending, and running executions, and verifies disabled
state, removal, and old-ref dispatch rejection before destination acceptance.
New-YAML-only rejection cannot close old selected refs. Former Buddy projects
are unsupported until migrated. v1 Official and CI assets remain unchanged;
legacy Buddy workflows, Buddy-specific tests and matrices, and Buddy
documentation are excluded from that preservation and are retired or rewritten.
Failed destination acceptance leaves all Buddy publication disabled and does
not restore legacy Buddy without a separate user-approved rollback PR, so an
intentional brief Buddy outage is expected.

### First-Slice Destination Acceptance Bootstrap

A temporary protected Governance workflow used once to validate GitHub Packages
semantics while normal v3 live publication remains disabled. Its purpose is
separate from live Release. It is hard-bound to an approved target SHA, fixed
acceptance-only coordinates in the same disposable package, explicit operator
confirmation, and a dedicated reviewer-protected acceptance Environment.
Package-write permission exists only in probe jobs.

The bootstrap accepts no normal Release inputs and creates no Product,
Execution, Attempt, Authorization, Receipt, or live Release history. After
legacy cutover, every probe independently requires `github.run_attempt == 1`.
The terminal evidence-capture job runs with
`always() && github.run_attempt == 1` or an exact equivalent so first-attempt
probe failures, skipped or canceled dependencies, and ambiguous mutation
evidence are persisted and incomplete or unknown state enters reconciliation.
The evidence job still rejects non-first attempts. A partial rerun cannot reuse
an earlier Environment review or coordinate, and retry requires a new reviewed
workflow invocation and new fixed disposable coordinate/version. Governance
captures its evidence, removes the workflow, bypass, and Environment, verifies
removal, and only then may an authorized protected commit set
`live_enabled: true`. Failure leaves `live_enabled: false` and all Buddy
publication disabled, removes the temporary path, and sends any probe state to
reconciliation. The bootstrap is never retained as a reusable bypass.

### Official

A Release Delivery policy channel for authoritative production publication.

A live Official release must:

- target a revision reachable from a Governance-configured authoritative branch;
- use the Release Planner and Finalizer contained in that target revision;
- rebuild every publishable artifact variant declared by the Release Unit;
- complete its own Release Qualification Target;
- complete remote-state observation and freeze the exact Publication Snapshot;
  and
- bind authorization to that immutable Publication Snapshot digest.

After authorization, the revision, version, artifacts, and destinations must not
change. Official business identity uses the canonical NBGV version. Ecosystem
publication and dry-run use the exact frozen native NBGV projection, such as
`npmPackageVersion`, unchanged. Live Official creates the authoritative release
record.

A non-authoritative branch may exercise Official dry-run behavior but cannot
receive live Official publication capability.

Official is not a separate release implementation.

## Core Domain Terms

### Project Node

A normalized technical fact discovered from an ecosystem manifest or workspace
at an immutable repository revision.

Examples include a .NET project, Python project, or JavaScript package. A
Project Node records ecosystem identity, source location, dependency
relationships, build capabilities, and relevant manifest facts.

A Project Node is not an authored ownership, qualification, versioning, or
publication boundary. The ecosystem build system remains authoritative for its
internal dependency and output semantics.

### Source-Tree Conformance

The repository-local assertion that an immutable checkout satisfies its
formatting, linting, static source, lock, generated-file, configuration, and
path-triggered scenario rules.

The repository-root HK gate owns this assertion as one opaque composite Quality
Definition. CI binds the candidate and definition identity but does not inspect
HK profiles, steps, file applicability, batching, or internal planning.

The first-slice root HK implementation includes a path-triggered v3 control
package pytest step and runs it unconditionally for manual `slice-validation`.
That step remains internal to Source-Tree Conformance and does not create a
separate CI obligation, Evidence record, or job.

### Affected-System Qualification

The model-driven assertion that systems affected by a candidate remain correct
across Project Node dependency closure, provider-native quality targets,
execution dimensions, supporting tests, and affected Release Unit variants.

The CI Planner owns this scope. HK does not replace it.

### Quality Capability

An ecosystem-resolvable quality operation available to a Project Node or
provider-native aggregate target, such as build, type checking, analysis, or
test execution.

Providers discover standard capabilities from native manifests and metadata.
Project custom policy supplies only definitions that native ecosystem facts
cannot express.

### Quality Preset

An ecosystem-qualified, semantically versioned quality contract selected by a
project.

A preset states which capabilities are required, required when present, or
advisory. It does not copy native commands or imply cross-ecosystem equivalence.
Adding or strengthening required semantics creates a new preset identity that
projects adopt explicitly.

### Effective Quality Policy

The preset or custom policy that applies to one Project Node after
directory-scoped authoring is resolved.

For one ecosystem, the effective selection is the nearest tracked ancestor
descriptor entry matching that ecosystem. Directory scope is an authoring
mechanism, not a domain object. The compiled Plan records the resolved policy
rather than directory inheritance.

### Quality Definition

The executable semantic contract for one kind of quality proof.

A Quality Definition identifies its Provider or Quality Adapter, supported
target kind, inputs, coverage dimensions, runner constraints, prerequisites,
raw result interpretation, and mechanical Quality Result contract.

### Release Unit

A logical product unit that is versioned, qualified, authorized, and delivered as
one release concern.

A Release Unit may produce multiple artifacts or platform variants. It selects
Build Definitions and entry points without reproducing the internal dependency
closure already owned by the ecosystem build system. Repository project
boundaries do not automatically define Release Unit boundaries.

### Qualification Target

The exact object covered by one quality decision. A Qualification Target must
bind to an immutable repository revision and identify the relevant Project
Nodes, Release Units, variants, inputs, and obligations.

In CI Qualification, the target is derived from changed paths, affected Project
Nodes and dependency relationships, effective project quality policy,
provider-native quality targets and dimensions, affected Release Units, global
inputs, and repository obligations.

In Release Delivery, the target is derived from the selected Release Unit and the
complete quality scope required by release policy.

A Release Qualification Target contains:

- the complete Project Node and declared-input closure required by the selected
  Build Definitions;
- every artifact variant selected for publication;
- every quality check declared by the Release Unit, without changed-path
  pruning; and
- explicit compatibility obligations or contract tests for designated
  consumers.

It does not include every reverse-dependent Project Node or every repository
check by default. Release Policy may require a broader consumer closure or a
repository-wide qualification profile for a specific class of Release Unit.

A Qualification Target must be closed before execution. Planning may explicitly
fail to close the target, but it must not emit a partial target that leaves
unknown Project Nodes, Release Units, variants, obligations, or destinations
for an executor to interpret.

For CI, unclassified changed paths, unresolved workspace relationships, and
changes to control-plane policy or global build configuration produce explicit
blocking obligations or diagnostics.

For Release, completeness is split across two gates:

- Pre-admission request-local Repository Model compilation closes descriptors,
  Project Nodes and dependency graph, Build Definitions, modeled variants and
  outputs, canonical and native NBGV facts, and build and artifact scope.
  Failure stops before Execution lookup, coalescing, or admission and creates no
  Attempt.
- Post-admission Attempt planning validates channel policy, policy-selected
  obligations and variants, compatibility obligations, destination projections
  and coordinates, Adapter and version bindings, logical operations, potential
  action and dependency schemas, capability policy, and deterministic complete
  mutable-resource-key derivation and enforceability basis.

### CI Candidate Identity

The immutable source identity evaluated by CI for a specific GitHub event.

For a pull request, CI evaluates the current GitHub-generated merge commit and
records the base, head, and tested merge commit SHAs. A change to the base or
head invalidates the previous decision.

For a merge queue, CI evaluates the merge-group commit SHA. For a push, CI
evaluates the pushed commit SHA.

Branch names, pull request numbers, workflow run IDs, and check-run IDs are
indexes rather than source identities.

### Manual Slice Validation

The non-authoritative first-slice CI purpose `slice-validation`.

It validates root HK and the complete `hcoona-release-smoke-npm` Project Node,
project build, project tests, Release Unit, and npm artifact build/pack without
changed-path pruning. It is not canonical repository-wide full validation, is
not a Ruleset required check, and does not replace v1 required CI. Canonical
explicit or scheduled full validation remains deferred until every active
Project Node, Release Unit, and repository obligation is modeled.

### Official Product Identity

The destination-independent business product identity for Official: channel,
Release Unit, and canonical NBGV version.

Different immutable targets may have the same Official Product Identity. The
architecture does not require a permanent global Product Identity-to-target
mapping.

### Release Execution Identity

The identity used for Release Execution lookup, request coalescing, and Attempt
lineage.

- Official Release Execution Identity is Official Product Identity plus
  immutable target.
- Buddy Release Execution Identity is channel, Release Unit, and immutable
  target.

Different targets always create different Release Executions, including when
they share one Official Product Identity or derive the same destination
coordinate. Overlapping live actions serialize on complete Adapter-declared
mutable-resource keys. Durable destination state determines absent, exact, or
conflict; failed pre-mutation Attempts reserve nothing.

Plan digests, artifact digests, and workflow attempts are Attempt bindings
rather than Product or Execution Identity fields. Successful approval binds and
retains an Authorization Record. A completed rejection whose explicit platform
contract is proven may bind and retain Approval Outcome Evidence instead; the
first-slice GitHub path has no such contract. GitHub
cancellation or platform expiry while approval remains pending may end the run
before a separate record or Finalizer outcome exists. When no capability group
started, the platform conclusion is sufficient no-side-effect terminal evidence
and the Attempt is incomplete and replayable. If a capability job may have
started, the Attempt is incomplete and possibly mutated and replay must
reobserve. Tags, branches, and workflow run IDs are indexes.

Before live eligibility, identity lookup, coalescing, or admission, each
candidate run attempt branches to live release or release simulation. Each
branch compiles exactly one same-revision, request-local Repository Model
Snapshot for its own purpose and reuses it throughout the resulting live
Attempt or simulation pass. The Snapshot and every admitted Fact Bundle bind
purpose, request identity, `github.run_id`, `github.run_attempt`, target,
producer, and control identity. Prior-run-attempt and cross-purpose artifacts
are rejected. A replay or other new run attempt compiles a new Snapshot even
when request identity, `github.run_id`, and target remain unchanged.

For the live branch, Official Product and Execution identities use the
Snapshot's target-bound canonical NBGV facts. Buddy Execution Identity needs
only channel, Release Unit, and target. The Snapshot already contains
authoritative native facts, including `npmPackageVersion`; Buddy identity
ignores them.

### Execution History Admission Snapshot

The immutable history-only snapshot created during live `admit`, under the
whole-Execution concurrency slot and before the current Attempt binding. Release
uses read-only Actions APIs with complete pagination to discover retained runs
and run attempts, artifacts, Attempt bindings, outcomes, and platform
conclusions for the same Release Execution Identity.

Each admitted historical record authoritatively retains only artifact
ID/digest, source workflow run ID, head SHA, payload integrity, and platform
metadata exposed by the Artifact/Run APIs. Jobs/Run APIs separately supply
attempt/job/phase facts. Payload producer, exact attempt, reusable-workflow,
purpose, and control claims are diagnostic self-assertions. The Snapshot binds
the admitted platform identities/digests and separately queried facts for
current finalization and explanation. Historical records cannot satisfy
current-Attempt Evidence, authorization, eligibility, artifacts, Receipts, or
outcomes. API denial, incomplete pagination, malformed records, or duplicate or
conflicting platform bindings block admission.
Retention expiry is not a ledger failure: current provably absent or exact
destination state may proceed, while partial, conflicting, unknown, or
unprovable state requires reconciliation.

Artifact and record admission has two caller-selected modes. The payload cannot
select or influence the mode. `current-authority` requires exact current
purpose, request, run, run attempt, Attempt, target, producer, control,
artifact, and digest bindings and rejects prior attempts.
`execution-history` is allowed only during pre-Attempt admission; its source may
be a different workflow run or an earlier run attempt of the current run. It
binds the limited platform facts above and separately queried phase facts,
including existence of the earlier attempt for same-run history; payload
lineage claims do not become artifact-to-attempt or artifact-to-job provenance.
The resulting Snapshot binds the current request/run/attempt,
exhaustive query basis, and sorted admitted platform IDs/digests with an
explicit history-only marker. It cannot satisfy current Evidence,
authorization, eligibility, artifacts, Receipts, or outcomes. Strict historical
workflow/attempt provenance is unsupported until Artifact Attestations or OIDC
are separately approved; the first slice enables no `id-token`.

### Live Eligibility Decision

The immutable Release-owned pre-Attempt decision for the named live Buddy
slice. It is produced after exact target pinning and request-local Repository
Model compilation and before Execution lookup, concurrency, history admission,
or Attempt creation.

It binds live purpose, request, current run and attempt, selected ref and target
SHA, Repository Model digest, producer/control, consumer policy and catalog
digests, exact scanned dependency surfaces and exceptions, the attestation's
required boolean `live_enabled` value, and Governance attestation repository,
fixed protected ref, resolved commit, path, Git blob OID, canonical content
SHA-256, and result. For the first slice, the immutable source contract is
repository `hcoona/three`, ref
`refs/heads/main`, and path
`.github/workflow-delivery/governance/hcoona-release-smoke-npm.json`. The
attestation is a non-executable human-inspection snapshot with explicit accepted
writer and package/repository/Manage Actions access inventory or evidence
digest, policy/package bindings, issuer, inspection time, acknowledged
limitations, expiry of at most 90 days, and `live_enabled`. Using
`contents: read`, the evaluator freshly verifies ref protection, resolves the
ref, reads the document at the resolved commit, and validates its provenance
and content. The evaluator receives no `actions: read`, package-read, or
package-write permission; effective Actions-history read is confined to
history admission and explicit package read to destination observation. The
payload does not self-reference Git provenance; the Decision
binds that provenance externally. Runtime does not enumerate current writers or
GitHub Packages grants. Current-attempt success is required; CI HK, history, or
an older decision cannot substitute. Missing, unreadable, malformed, expired,
provenance-mismatched, `live_enabled: false`, or consumer-positive state blocks
without creating an Attempt. Changes require a prompt authorized protected
commit setting `live_enabled: false`, followed by update and re-attestation
before re-enablement; expiry bounds normal-flow staleness.

### Governance Freshness Revalidation

The immediate pre-Capability re-read that prevents an approval wait from
outliving or bypassing live Governance state.

Using `contents: read`, it freshly resolves the policy-fixed protected ref and
reads the attestation document again, verifies ref protection, schema,
canonical content, bindings, current expiry, and `live_enabled: true`, and
requires repository/ref/path plus commit/blob/content provenance and content
identity to match the current Attempt's Live Eligibility Decision. A false
`live_enabled` value, expiry, changed or invalidated source/content, or binding
mismatch blocks publication and requires a new Attempt after Governance is
restored. The publisher may repeat the same `contents: read` check immediately
before mutation only as defense in depth; the repeat adds no credential or
service and is not a malicious-writer boundary.

### Package Target Witness

Canonical `workflow-delivery/provenance.json` embedded inside the npm tarball
and therefore covered by its bytes and digest. It binds target commit, Release
Unit, canonical and native NBGV facts, Build Definition, catalog and control
digests, purpose, and schema. It excludes run and Attempt identities so builds
of one target remain reproducible across attempts. Exact remote package state
requires matching coordinate, ownership, witness, and bytes; a sidecar is not a
substitute.

## Build and Quality Terms

### Pull Request Build

A build used to provide early qualification feedback for an unmerged change.

Its artifacts are not admissible as Release artifacts.

### Release Build

A fresh build performed by Release Delivery for the final immutable release
revision.

Release Build outputs are subject to Release-owned quality, provenance,
authorization, and publication decisions.

### Quality Check Definition

A reusable definition of how to perform a quality check. CI Qualification and
Release Delivery may share the same check definitions and execution capabilities
without sharing plans, evidence, or decisions.

### Build Definition

The authoritative, reusable definition of how one artifact variant is built.

It freezes the build entry point, toolchain constraints, parameter model,
variant dimensions, expected output structure, and provenance requirements.
CI Qualification and Release Delivery use the same Build Definition but
materialize separate Build Requests for different revisions and purposes.

When a change affects a Release Unit, CI builds every publishable artifact
variant defined for that Release Unit. Release later rebuilds the complete
publishable variant set for the final release revision.

### Build Request

A system-owned, immutable invocation of a Build Definition for a specific
revision, purpose, and artifact variant.

CI and Release Build Requests are separate. They may differ in revision,
version identity, and authorization context, but they must preserve the shared
binary-production definition. Each request binds one exact target-bound native
NBGV projection selected from the Repository Model Snapshot. The Build Adapter
applies and verifies that value and must not recompute NBGV, derive another
version, or use fallback version fields.

### Reproducible Release Build

A Release Unit business contract requiring the same target commit, Build
Definition, toolchain, and declared inputs to produce bit-for-bit identical
release artifacts.

The delivery system does not certify reproducibility by performing duplicate
builds. Remote observation records artifact digests and refuses to continue
when destination state conflicts with the current Attempt's snapshot-bound
desired projection state. That integrity check is not a general
reproducible-build certification.

### Semantic Plan Finality

The rule that an executor may resolve mechanical execution details but may not
change the business meaning of an accepted Plan.

An executor may restore locked dependencies, enumerate tests within a selected
test target, locate declared outputs, inspect remote state for idempotency, and
adapt paths to the assigned runner.

An opaque composite definition may internally select implementation steps when
those steps are not domain obligations. The root HK gate uses this rule for
source-tree conformance.

It may not add, remove, substitute, or downgrade Project Nodes, Release Units,
variants, obligations, versions, artifacts, destinations, or authorization
requirements. A runtime discovery that conflicts with the Plan or Build
Definition causes failure rather than replanning.

### Planner

The bounded-context-owned decision service that converts immutable inputs,
repository facts, and applicable policy into closed inputs for each execution
boundary before that boundary runs. Planning is staged; it does not claim that
facts produced by an earlier boundary are known before that boundary executes.

For Release, the Qualification Snapshot closes build and qualification scope
and the deterministic pre-observation publication basis before build execution.
After qualification and observation, the Publication Snapshot closes exact
artifacts, desired and observed state, materialized actions and inputs, complete
mutation key sets, capability groups and requirements, Receipt contracts, and
the authorization basis before side effects.

CI and Release have separate Planners because they select different scope,
obligations, identities, and side effects.

### Finalizer

The bounded-context-owned decision service that admits execution records and
produces the immutable Decision or outcome after execution.

CI and Release have separate Finalizers. Shared Foundation may provide strict
record validation, canonicalization, digest, and Evidence-binding functions,
but it does not select the verdict.

The CI Finalizer considers required obligations only. Advisory obligations use
a separate non-authoritative Reporter.

### Advisory Reporter

The CI-owned projection component that validates and summarizes advisory
Evidence without producing an authoritative qualification verdict.

It may finish after the required Final Decision. Its failure is visible but does
not change the required check.

### Official Product Identity Semantics

Official Product Identity uses the canonical NBGV version for one Release Unit.

It is not a substitute for ecosystem-native publication projection. Official
publication and dry-run freeze and use the exact required native NBGV
projection unchanged.

Each immutable target has a distinct Official Release Execution Identity and
append-only Attempt history. No permanent global Product Identity-to-target
ledger is required. Replay recompiles the request-local Repository Model and
creates new Attempt-specific Qualification and Publication Snapshots. Existing
destination state is accepted only when it matches snapshot-bound desired
projection state; a differing coordinate, ownership, target binding, or artifact
digest requires reconciliation.

### Buddy Execution Semantics

Buddy Release Execution Identity is channel, Release Unit, and immutable target.

Pre-admission Repository Model compilation computes the authoritative canonical
and native NBGV facts. Attempt planning selects and freezes the required native
projection from that Snapshot, then derives the complete deterministic
destination projection set. No native version, External Package Coordinate, or
projection-set digest is part of Buddy Release Execution Identity.

Distinct admitted Buddy requests for the same channel, Release Unit, and target
address one Release Execution Identity and one Release Execution. A different
target creates a different Execution Identity and Execution even when every
derived coordinate is the same.

A registry coordinate is an external resource address, not a pre-publication
reservation. No Intent reserves an absent coordinate. Successful durable
destination creation establishes its observable package binding.

### External Package Coordinate

The address of one package-version resource: channel, destination, package, and
version.

It excludes Release Unit and target. Every live action that may establish or
mutate the package resource includes this exact coordinate in its
mutable-resource key set; an Adapter may also require additional keys.

### Package Routing Tag

A destination routing projection distinct from package provenance. The
first-slice npm Adapter uses exactly
`buddy-sha-<40-lowercase-target-sha>` and maps it to the frozen native
`npmPackageVersion`. It never uses implicit `latest` or a shared moving Buddy
tag. Exact state includes this mapping, but the package-internal target witness
remains the target-provenance authority.

### Mutable-Resource Key

A deterministic destination-defined identity for external mutable state touched
by one Publication Action.

Every mutating action declares its complete key set through its Destination
Adapter. Package actions include the exact External Package Coordinate.
First-slice npm compound publication additionally includes the
destination/package/routing-tag resource and permits no separate normal tag
mutation. Package actions may also require additional Adapter-declared keys. Non-package
destinations define their exact keys through Adapter contracts. Publication
Snapshots and action manifests bind the keys. Overlapping live actions serialize
on them; missing, unknown, incomplete, or conflicting required keys block
publication. Remediation reuses exactly the complete frozen key set from the
original action and never derives it from Product or Execution Identity.

A platform serialization group is an enforcement projection, not a
Mutable-Resource Key. When a platform exposes only equality groups, an Adapter
may map several nonidentical complete key sets to one conservative group only if
all overlapping sets are guaranteed to share that group. The first-slice GitHub
Packages projection uses physical destination plus normalized npm package name,
so different versions and target-derived tags for the same package serialize.
The complete coordinate-plus-tag key set remains frozen in the Publication
Snapshot, action binding, Receipt, and validation.

### Qualification Evidence

Evidence produced by executing quality obligations against a specific
Qualification Target.

CI evidence and Release evidence have separate ownership. Release Delivery reruns
its required quality checks rather than adopting CI results as release evidence.

CI required and advisory obligations both produce Plan-bound Evidence. Only
required Evidence enters the authoritative CI Final Decision.

### Evidence Admission

The lightweight process that proves an execution result belongs to the exact
Plan obligation being decided.

Admission verifies strict record shape, candidate or target commit, Plan digest,
obligation identity, definition and request digests, producer job, attempt,
runner family, and relevant artifact content digests.

Admission does not rerun the command, reinterpret test results, or duplicate the
executor's quality logic. High-risk side-effect boundaries may additionally
recompute artifact digests and verify provenance before publication.

Target code may produce raw results, but a CI- or Release-owned Evidence writer
creates the final Evidence envelope from the execution result and GitHub job
context.

### Plan Readiness

The structural state that determines whether a closed Qualification Target can
be executed toward a successful decision.

A Plan is either `ready` or `blocked`. Unclassified paths, unresolved
dependencies, undefined variants, missing definitions, and conflicting policy
make the Plan `blocked`.

Diagnostics explain readiness but do not determine it.

### Obligation Disposition

The policy-assigned effect of one quality obligation:

- `required`: successful admitted Evidence is necessary for success; or
- `advisory`: the result must be reported but does not block the final decision.

Checks that do not apply are excluded explicitly during planning rather than
being silently skipped during execution.

`required-when-present` is a preset capability-resolution mode rather than a
terminal obligation disposition. When the capability exists, the resulting
obligation is required. Its absence is valid.

### Obligation Outcome

The terminal qualification state of one obligation:

- `satisfied`;
- `failed`;
- `incomplete`, including skipped, cancelled, timed out, missing, or lost
  execution; or
- `conflicted`, including inconsistent Evidence or artifact identity.

A successful qualification decision requires a ready Plan and every required
obligation to be satisfied.

### Final Decision

An immutable record produced after required aggregation completes. It binds the
candidate or target identity, Plan digest, required Evidence Set digest,
required obligation outcomes, verdict, and completion time.

Required workflow reruns produce a new Final Decision rather than modifying an
existing record. Advisory Evidence is reported separately and does not create a
new authoritative Decision.

A GitHub required-check context may project the latest authoritative Final
Decision for the current candidate. That mutable user-interface projection does
not replace the append-only Decision history.

Release authorization does not bind this generic Final Decision or a generic
Plan digest. After Release qualification and remote-state observation,
authorization binds the exact Publication Snapshot digest, including the
Qualification Decision, qualified artifacts, observations, and materialized
actions, keys, capabilities, and Receipt contracts as applicable. An in-progress
side-effect execution cannot switch automatically to a later Snapshot or
Decision.

### Runtime-Decoupled Delivery Systems

CI Qualification and Release Delivery do not depend on each other's runtime
plans, evidence, artifacts, status checks, or decisions.

They align through shared domain identities, quality definitions, build
specifications, ecosystem capabilities, and provenance primitives. Each system
materializes and executes its own plan against its own Qualification Target.

Release target eligibility is a Delivery Governance concern rather than a
runtime dependency on CI status.

### Release Intent

The request to deliver exactly one Release Unit through exactly one channel.

Manual initiation uses the GitHub-selected workflow ref as the target. An
independent target SHA or ref input is forbidden. The workflow pins the resolved
`github.sha`, and every Plan, artifact, Evidence object, action, and record binds
that same revision.

Intent is request identity, not Product, Execution, or package identity.
Multiple admitted Intents may address the same Release Execution Identity.

### Release Execution

The channel-specific business execution for one immutable Release Execution
Identity. It contains append-only whole-release Attempts and may be initiated by
multiple separate Release Intents that address that identity.

Its state is one of `in-progress`, `replayable`,
`reconciliation-required`, or `completed`. Platform retention limits how long
its operational records remain replayable.

### Release Simulation

A dry-run execution that performs planning, build, qualification, observation,
and publication simulation without obtaining live publication Capability or
entering the Buddy or Official Release lineage.

Simulation branches before live eligibility, Product or Execution Identity
lookup, coalescing, admission, or Attempt creation. It first compiles and
validates one simulation-purpose Repository Model Snapshot binding request
identity, `github.run_id`, `github.run_attempt`, target, channel, Release Unit,
canonical and native version facts, producer, and control identity.

Only after that validation does the Planner derive the separately namespaced,
request-scoped Simulation Identity from the validated bindings. Later simulation
planning records bind both the Simulation Identity and Repository Model Snapshot
digest. A Buddy simulation uses the same target-derived native NBGV version
projection and hypothetical destination coordinate as live Buddy planning. It
never contains or acquires a live Product, Release Execution, or Attempt
identity, Authorization Record, Capability, Receipt, or mutation. It may emit
hypothetical requirements and actions and a Simulation Outcome.

Shared schemas may be reused only with an explicit purpose discriminator.
Cross-purpose admission always rejects the record.

### Release Plan Lineage

The single logical Plan history for one Release Attempt. It contains two
immutable sealed snapshots rather than one mutable document or two unrelated
Plans.

### Qualification Snapshot

The first sealed snapshot in a Release Plan Lineage. It freezes the Release
Execution Identity, request-local Repository Model Snapshot identity and digest,
Release Unit, target commit, channel, version, Project Node and declared-input
closure, build dependencies, the complete Release Unit artifact variant set,
Build Definitions, selected Release quality policy, required quality
obligations, complete destination projections and coordinates, Adapter and
version bindings, logical operations, potential action and dependency schema,
capability policy, and deterministic complete mutable-resource-key derivation
and enforceability basis.

It authorizes only unprivileged build and qualification work. It does not
freeze actual mutation actions or actual action key sets before artifacts and
remote disposition are known.

### Publication Snapshot

The second sealed snapshot in a Release Plan Lineage. It references the
Qualification Snapshot digest, preserves every frozen semantic field, and adds
actual artifact identities, content digests, provenance, snapshot-bound desired
projection state, destination observations, exact materialized action DAG and
inputs, complete Adapter-declared key set for each actual mutation, capability
groups and requirements, Qualification Decision, and Receipt contracts.

After successful approval, an Authorization Record binds Governance approval to
the Publication Snapshot digest and immutable reviewer-summary artifact ID and
digest. A finalizer verifies that no Qualification Snapshot field changed and
that the summary artifact matches the approved Snapshot.

GitHub transports the snapshots as separate attempt-specific artifacts even
though they share one logical Release Plan lineage.

### Publication Preparation Interruption

The terminal live-Attempt condition after an exact successful Qualification
Decision but before a durable Publication Snapshot exists.

Observation, Snapshot materialization, Snapshot upload, or platform
cancellation may produce this condition only when direct platform facts prove
that the capability-bearing path did not start and no Authorization, Capability
Admission Decision, mutation marker, capability-group result bundle, or Receipt
exists. Missing Snapshot transport alone is not proof.

The sole Release Finalizer records terminal phase `publication-preparation`,
result `incomplete`, uncertainty about unfinished planning,
`possibly_mutated: false`, and next action `new-attempt`. It does not fabricate a
Publication Snapshot or create a domain Evidence record that merely copies
GitHub job results. A durably persisted Publication Snapshot ends this phase;
later reviewer or approval-input failure retains that Snapshot and uses the
Snapshot-bound lifecycle. Raw job results remain retained human diagnostics
rather than canonical Attempt Outcome fields.

### Release Attempt

One coherent plan, build, qualification, authorization, publication, and
reporting pass within a Release Execution.

An Attempt is identified by Release Execution Identity, `github.run_id`, and
`github.run_attempt`. It also binds the originating Release Intent and request
identity. Those are required immutable bindings, not additional Attempt Identity
components. Its Qualification Snapshot, Publication Snapshot, artifact digests,
and conditional approval record are immutable bindings created within that
Attempt when the corresponding job completes: Authorization Record after
success, or Approval Outcome Evidence after contract-proven rejection on a
supporting platform. The first-slice GitHub rejection creates neither.
Platform cancellation or expiry may terminate the Attempt without either
context-owned record.

Every admitted, non-coalesced manual request for an existing Release Execution
Identity creates a new Attempt, but is not a replay of the earlier request. A
pending dispatch replaced or coalesced before execution is not admitted and
creates no Attempt. A GitHub `Re-run all jobs` is a replay of its existing
request. Both admitted paths first compile a new request-local Repository Model
Snapshot, then independently replan, rebuild, qualify, observe, obtain approval,
and finalize. For `Re-run all jobs`, the Snapshot binds the new
`github.run_attempt` even though request identity, `github.run_id`, and target
remain unchanged. Within one admitted request, later planning reuses that
request's Snapshot rather than recomputing it.

An Attempt does not combine successful jobs, artifacts, approvals, or evidence
from multiple GitHub run attempts as if they formed one atomic pass.

An Attempt Outcome records qualification, observation, authorization, action,
Receipt, and reporting results without replacing the Release Execution state.

### Whole-Release Replay

The supported retry model for a failed Release Attempt.

Every replay reruns planning, the complete Release build, Release
qualification, observation, authorization, and reporting. The planner observes
every projection again: exact satisfied state skips its side effect, absent
state may publish, and unknown, projection-internal partial, or conflicting
state requires reconciliation.

GitHub `Re-run failed jobs` is not a supported Release recovery protocol because
it produces a mixed-attempt job graph. A normal transient retry uses `Re-run all
jobs`. A workflow or control-code fix creates a new target revision; ordinary
replay of an older target continues to use that target's original code.

Each live side-effect job obtains a new attempt-scoped Publication Capability.

### Remote-State Observation

The mandatory read-only pre-authorization planning step in every Release
Attempt, including the first attempt.

Each logical publication projection is classified atomically against its
snapshot-bound desired projection state, not Product or Execution Identity.
Desired state is
derived from the Qualification Snapshot and admitted qualified artifacts and
includes exact destination coordinate, expected ownership, target binding, and
artifact bytes or digest. Each Observation Record binds its Release Attempt,
logical projection, immutable desired-state basis, and canonical remote response
and observed facts. It cannot bind a future Publication Snapshot. The later
Publication Snapshot seals admitted Observation Records with the resulting
desired state and materialized actions:

- absent state may produce a publish action;
- exact satisfied state produces no side effect;
- partial, unknown, conflicting, or unprovable state fails closed and requires
  reconciliation.

An absent coordinate remains a legitimate initial-publication state when no
operational Release lineage is retained. It does not require a tag witness,
binding index, or permanent ledger. After authorization, registry publication
uses the destination's atomic non-overwriting create contract. Pre-observed
exact state produces no action. Atomic create-or-exact may accept a concurrently
created exact state without mutation, but differing state fails without
mutation. It is never read-then-upsert, overwrite, or delete-and-recreate. A
pure create-only conflict is reobserved on whole-release replay.

Cancellation does not create a separate reconciliation workflow. A later
whole-release replay performs the same normal Remote-State Observation before
any new write.

The initial architecture assumes Delivery Governance is the only normal writer.
It does not require a second observation after authorization and accepts
out-of-band mutation between observation and publication as a residual risk.

### Publication Projection

A channel-selected logical remote product that must become exact as one unit,
such as a registry package version or a GitHub Release with its required assets.

The Release Unit selects projections, the Destination Adapter defines their
mechanics and action expansion, and Delivery Governance grants the necessary
Capability. A projection may expand into multiple ordered actions, but
projection-internal partial state is not ordinary replayable state.

### Capability Group

An execution group whose actions share one destination-specific authorization
and credential boundary.

Independent groups may run in parallel after channel approval. Actions within a
group run in order and stop after the first failure. Every action retains its
own identity and Receipt.

### Capability Admission Gate

The credential-free decision boundary between successful approval and a
credential-bearing capability group. It validates the Authorization Record,
Publication Snapshot, reviewer-summary artifact, planned actions, artifacts,
complete resource keys, group manifest, and Governance Freshness Revalidation.
Only its successful admission may schedule or start the publisher. The
publisher may repeat those checks as defense-in-depth rather than serving as
the first admission boundary.

### Authorization Record

The append-only record that binds one channel-level approval to one exact
Publication Snapshot digest and immutable reviewer-summary artifact ID and
digest.

That Snapshot includes the Qualification Decision, qualified artifacts and
digests, remote-state observations, and exact materialized actions, inputs,
complete mutation key sets, capability requirements, groups, and Receipt
contracts as applicable.

It authorizes capability-group execution but does not itself contain or replace
destination credentials. Each capability group independently obtains its
short-lived Capability through its configured Governance boundary.

It exists only after successful approval. Capability groups require a valid
Authorization Record and cannot treat approval failure Evidence as authority.

### Approval Outcome Evidence

The platform-derived terminal record for a denial only when the platform
supplies documented exact current-attempt, approval-job, Snapshot, and terminal
result bindings. Workflow Delivery does not manufacture an approval timeout or
watchdog outcome.

GitHub Environment `DeploymentReview` does not provide authoritative
`run_attempt` or approval-job binding and has no documented append-only
consistency contract that makes review-ID delta inference safe. Therefore the
first-slice GitHub workflow admits no Approval Outcome Evidence for rejection or
denial. Observable review data may appear only as a non-authoritative human
diagnostic. Rejection is unknown approval-contract failure, leaves a replayable
incomplete Attempt, and starts no Capability.

It proves governed failure and grants no Capability. GitHub cancellation or
platform expiry while approval is pending may instead terminate the run without
this record or a Finalizer outcome. If no capability group started, the platform
run/job conclusion itself proves no side effect and leaves a replayable,
incomplete Attempt. If a capability job may have started, cancellation does not
prove absence of mutation; replay must reobserve. When finalization runs and has
neither valid authorization nor applicable admissible terminal evidence,
approval state is unknown and the outcome is approval-contract failure rather
than governed denial.

### Release Reconciliation

The read-only exceptional process used when Remote-State Observation cannot
classify a projection as safely absent or exactly satisfied.

Reconciliation resolves partial, unknown, conflicting, or unprovable state.
Existing successful publication is not automatically rolled back.

### Release Execution Serialization

The best-effort GitHub boundaries that serialize Release lineage and access to
externally visible mutable resources.

- Release Execution lineage and pending-request coalescing use the complete
  Release Execution Identity.
- Every live mutating action binds the complete deterministic resource-key set
  declared by its Destination Adapter.
- Package action keys include the exact External Package Coordinate: channel,
  destination, package, and version, intentionally excluding Release Unit and
  target, plus any additional Adapter-required keys.
- Non-package Destination Adapters define their exact mutable-resource keys.
- Platform concurrency may use an Adapter-declared conservative equality-group
  projection only when it guarantees that every pair of overlapping complete
  key sets receives the same group. The first-slice GitHub Packages projection
  is physical destination plus normalized npm package name and intentionally
  over-serializes different versions and target-derived tags.
- Distinct Releases remain distinct business identities, but overlapping action
  key sets serialize access to shared external resources.
- Break-Glass Remediation reuses exactly the complete frozen
  Adapter-declared resource-key set from the original action and never derives
  it from Product or Execution Identity.

Serialization does not grant authorization or replace Remote-State Observation.
Live actions may run concurrently only when their complete resource-key sets do
not overlap. Missing, unknown, incomplete, or conflicting required keys block
live publication.

Release concurrency never cancels an in-progress execution. Pending request
coalescing may retain only the newest duplicate request for one complete Release
Execution Identity; resource-key serialization does not redefine distinct
Executions as duplicates.

Request-local Repository Model compilation occurs before this execution
concurrency boundary. The surviving concurrency-scoped caller invokes one
same-revision reusable live-Attempt workflow and holds the Release Execution
identity slot continuously from admission through finalization. A superseded
pending caller never enters admission and creates no Attempt.

GitHub concurrency is not a distributed lock, durable queue, or protection
against external writers. Correctness still depends on observation,
Destination Adapter behavior, and the governed single-writer assumption.

Release Execution and pending-request coalescing keys derive from Release
Execution Identity. Live-action resource keys derive independently from
complete Adapter-declared mutable-resource keys; a platform serialization group
may be a conservative Adapter-declared projection of the destination resource
shape, but it is not a projection of business Product or Execution Identity and
does not replace the complete key set.

## Quality Attribute Terms

### Non-Authoritative Cache

A cache is a performance mechanism rather than a correctness dependency.

Package managers, setup actions, and build tools may use cache entries whenever
they become available during an execution. The delivery system does not require
an explicit cache-disabled mode.

Continuous cache unavailability must not change the Plan, skip an obligation,
alter Evidence semantics, or prevent execution when authoritative dependency
sources remain available. Cache writes may fail without changing the result.

Release artifacts and durable release records are not caches.

### Just-in-Time Publication Capability

A Publication Capability is requested by the Side-Effect Zone when the planned
action actually requires it.

The system does not add a separate OIDC or credential-availability probe.
Failure to obtain the required OIDC token or other destination Capability blocks
that publication action and prevents a completed Release result.

Build and qualification may complete without publication authority. The system
must not fall back to a long-lived token, personal access token, alternate
environment, or alternate workflow identity.

### Authoritative Delivery Record

A small structured object required to establish delivery correctness, including
a frozen Plan, admitted Evidence, artifact identity and provenance, Final
Decision, Publication Receipt, or Remediation Record.

Authoritative Delivery Records must be persisted before a later stage relies on
them. If a publication succeeds but its Receipt cannot be persisted, the
Attempt stops before additional destination side effects and a later replay
observes remote state.

Performance metrics, optional diagnostic logs, dashboards, and notifications
are telemetry rather than Authoritative Delivery Records. Their failure may
reduce observability without changing the decision.

### Platform-Native Record Retention

The retention model that uses each existing platform for the facts it can
actually preserve.

GitHub Actions Plans, Evidence, Receipts, reports, and build artifacts are
operational records available only within the configured Actions retention
window. In this public repository, GitHub supports at most 90 days. The
first-slice LLD uses 45 days for Release control and artifacts so retention
exceeds the platform Environment gate-expiry window, currently up to 30 days;
activation blocks if repository policy cannot provide that margin. Retention or
pending approval does not freeze the protected document's `live_enabled` value
or extend the at-most-90-day Governance attestation; capability admission
freshly verifies the field and source provenance/content.

Longer-lived release identity and provenance rely on Git tags, registry
package/version records, GitHub Release objects when selected, and GitHub
Artifact Attestations with public Sigstore transparency-log publication.

After Actions records expire, a replay may use only facts still provable from
those platforms. Unprovable exact state fails closed.

The first architecture does not add an external Durable Release Ledger or
require every Release Unit to create a GitHub Release audit anchor. A future
compliance requirement may introduce such a capability explicitly.

### Ordinary-Change CI Latency SLO

The product objective that the required CI Final Decision for an ordinary pull
request completes within 12 minutes at the 95th percentile.

Measurement begins when GitHub creates the workflow run and includes runner
queue time. Superseded candidates are excluded.

Broad changes to workflow authority, policy, global toolchains, or more than one
Release Unit are measured separately. Exceeding the SLO is performance debt
rather than a correctness failure.

Ordinary candidates remain in the cohort when quality fails or planning blocks.
Only supersession and explicit broad-change classification exclude them.

The SLO may drive parallelism, batching, early failure presentation, and cache
optimization. It must not reduce required obligations, publishable variant
coverage, or Evidence Admission.

Release active compute, runner queueing, and human approval wait are measured
separately and do not share the CI 12-minute objective.

### Repository Model Provider

A mechanism that converts ecosystem manifests, workspace configuration, global
configuration, project relationships, and build capabilities into normalized
Project Node, dependency, path-impact, global-input, dimension, and capability
facts.

It does not infer Release Unit identity or own CI or Release policy.

A pure Provider parses immutable inputs without target execution. A
target-evaluating Provider runs in an unprivileged discovery job and emits a
target-bound Fact Bundle for strict Repository Model admission.

### Fact Bundle

The immutable transport wrapper emitted by a target-evaluating Provider around
one Provider Result.

It binds the Provider Result digest, producer job, request identity, explicit
purpose, `github.run_id` and `github.run_attempt`, target, control identity,
request artifact, immutable transport identity, and Bundle digest. Admission
requires the current purpose and run attempt and rejects a cross-purpose or
prior-attempt Bundle. It contains no CI or Release policy.

### Provider Request Manifest

The closed authoritative list of Provider requests required for one Repository
Model compilation.

It binds request identity, purpose, `github.run_id`, `github.run_attempt`, exact
target, producer and control identities, static catalog, Provider implementation
and execution mode, request digests, and expected terminal result identities.
Compilation requires exactly one terminal Provider Result per entry.

### Build Adapter

An adapter that executes a Build Definition through one ecosystem toolchain and
maps declared artifacts to produced outputs.

CI and Release share the adapter. It returns a mechanical Build Result and does
not decide whether a build is required or whether the output is admissible.

### Quality Adapter

An adapter that executes one closed Quality Invocation and emits a mechanical
Quality Result.

It does not decide whether the resulting obligation is required or advisory and
does not directly emit authoritative CI or Release Evidence.

### Destination Adapter

A Release Delivery infrastructure adapter that implements projection
observation, publication, Receipt payload, mutability, digest visibility, and
remediation semantics for one destination family. It defines complete
deterministic mutable-resource keys for every mutating action.

It does not decide whether Buddy or Official policy selects the destination and
is not owned by Shared Foundation. Shared Foundation may provide generic GitHub,
registry, transport, artifact, and digest client primitives used by the adapter.

### Artifact Reference

A context-neutral mechanical reference that separates logical artifact
identity, content digest and size, immutable transport identity, producer
invocation, target, and purpose.

CI and Release may share the structure while applying independent admission.
A CI Artifact Reference cannot satisfy Release solely because its bytes or
logical output role match.

Actions artifact names are deterministic non-authoritative indexes unique
across the complete workflow run with overwrite disabled. Every physical name
includes the run attempt directly or through its deterministic hash preimage.
Producers capture immutable artifact ID, digest, and URL. Consumers fetch only
by ID and verify name metadata, producer, run ID, run attempt, and digest.
Prior-attempt IDs, name fallback, and latest-artifact selection are invalid.

### Mechanical Result

A family-specific Provider or Adapter output that reports normalized facts,
outputs, outcomes, and diagnostics without expressing a business verdict.

CI or Release binds the result to its own Plan or Attempt and forms the
authoritative Evidence, Observation Record, or Receipt.

### Execution Class

A Foundation declaration describing the trust and capability shape required by
one invocation.

Initial classes distinguish authoritative pure control code, unprivileged
target evaluation, unprivileged target execution, read-only remote observation,
privileged side effect, and privileged remediation. Foundation declares the
class; context workflows and Delivery Governance create the actual runtime and
grant.

### Cross-Revision Exchange Contract

A machine contract intentionally produced by one control-code revision and
consumed by another.

It carries stable kind, explicit contract version, producer repository,
workflow, ref, run, attempt, job and revision, original domain lineage, payload
digest, and compatibility rules. Same-revision, same-attempt internal records
do not require a universal API version.

### Independent Aggregate Roots

The rule that CI Qualification and Release Delivery own separate Plans,
Evidence Sets, Decisions, and state machines while consuming shared normalized
foundation interfaces.

New ecosystems normally add Providers or Build and Quality Adapters. New
destinations add Release-owned Destination Adapters that may reuse Foundation
client primitives. Neither extension path modifies CI or Release decision
semantics. Shared Foundation remains a mechanism layer rather than a universal
business Planner or Finalizer.

### Decision Explanation

The structured reason chain emitted as part of a CI or Release Final Decision.

CI explanations connect changed paths to Project Nodes, dependency
relationships, Release Units, selected obligations, variants, Evidence,
outcomes, verdict, and corrective actions.

Release explanations connect the Release Unit, target commit, version, channel,
artifacts, destination observations, planned actions, Receipts, outcomes,
authority, authorization, and allowed operator actions.

GitHub Job Summary is the human projection and the structured Decision or report
is the machine projection. Both are generated from the same model.

### Break-Glass Remediation

A separately authorized operational process that corrects an external release
projection without rewriting release history.

Break-Glass Remediation is not a `force` option on a normal Buddy or Official
Release Intent. It requires:

- immutable references to the original Release Execution, Attempt, Publication
  Snapshot, and Publication Action;
- the original Publication Action identity and exact complete frozen
  Adapter-declared mutable-resource key set;
- an expected current remote state;
- an explicit remediation action and destination;
- a reason and incident or work-item reference;
- a dry-run or equivalent precondition check;
- a short-lived, narrowly scoped remediation capability; and
- an append-only Remediation Record containing before and after state.

The remediation workflow reuses those keys exactly and never derives or
recomputes them from Product or Execution Identity, current destination state,
or current Adapter defaults.

Destination capability remains authoritative. An immutable registry may support
yank, deprecate, or a new version but not physical replacement. A mutable
destination may support replacement or retargeting when policy permits it.

Official remediation requires stronger governance than normal Official
publication. Buddy may use a lower approval tier, but it follows the same
append-only audit model.

### Publication Capability

A short-lived, externally granted credential or platform permission used to
perform a planned side effect.

Delivery Governance scopes the capability to the narrowest channel,
destination, repository, workflow, Environment, identity, and validity boundary
that the platform supports. The architecture does not assume that GitHub OIDC
or a destination token cryptographically carries the Publication Snapshot
digest, artifact digests, or exact action set.

Independent trusted side-effect executors generally validate the Authorization
Record, Publication Snapshot, artifact digests, action IDs, and Attempt before
using capability. Receipts record both the authorized semantic action and the
actual platform identity used.

For the First-Slice Buddy Risk Exception, that executor is target-revision code
and is not an independent adversarial enforcement boundary. It validates exact
bindings by contract. Its dedicated Environment and minimum normal-flow
`GITHUB_TOKEN` scope govern ordinary execution but do not constrain a trusted
malicious repository writer from authoring alternate write-capable workflow
jobs.

Qualification declares Capability requirements but cannot request, approve, or
create a live Capability. Only an authorized side-effect capability group may
request destination Capability after a credential-free Capability Admission
Gate validates Authorization Record, Snapshot, summary, action, artifact,
resource-key, and group bindings. Delivery Governance grants it through
platform controls such as protected environments, job permissions, OIDC trust,
and registry trusted-publishing policy.

Capabilities are destination-specific to the extent supported by the platform.
Buddy cannot reach Official destinations, dry-run receives no live Capability,
and Break-Glass Remediation uses a separate remediation Capability.

A Plan, artifact, attempt, or approval change invalidates the executor's
authorization to use a previously obtained capability even when the external
credential format cannot encode every such binding.

## Confirmed Architecture Principles

1. CI Qualification and Release Delivery are peer systems over a shared
   mechanism-level foundation.
2. Delivery Governance remains independent from both business systems and from the
   Shared Foundation.
3. Shared concepts must not collapse CI and Release into one universal plan or
   evidence model.
4. Pull request artifacts must not be reused or promoted by Release Delivery.
5. Release Delivery independently reruns every obligation in its selected
   channel-specific quality policy, and every such obligation is required.
6. CI and Release may share quality definitions, build specifications, ecosystem
   adapters, and execution capabilities.
7. Release Qualification covers the complete Project Node and declared-input
   closure required by the Release Unit Build Definitions, plus explicit
   compatibility obligations.
8. CI uses Planner and Finalizer code from the tested candidate revision. Live
   Release uses code from the exact selected target revision. Official requires
   a protected authoritative target. The first live Buddy smoke slice permits
   any same-repository selected ref. Dry-run Release simulation uses code from
   its exact selected simulation revision without approval or live publication
   Capability.
9. Planning, finalization, workflow, record-shape, and minimum-policy changes
   require Governance-configured owner review except for target-revision control
   and publisher code within the explicitly accepted first-slice Buddy
   exception.
10. Control-code changes create a new candidate or Release target; normal replay
    never injects newer control code into an older target.
11. The normal flow separates target execution from publication Capability,
    except for the explicitly accepted first-slice Buddy target-revision
    publisher after dedicated Environment approval and credential-free
    Capability Admission. Environment remains a mandatory process control, not
    a security boundary against a trusted malicious repository writer who
    authors an alternate workflow.
12. CI Qualification and Release Delivery have no runtime evidence, artifact, or
    verdict dependency on each other.
13. CI builds all publishable variants of every affected Release Unit by using
    the same Build Definitions used by Release Delivery. Artifact variants
    belong to the Release Unit rather than to a channel.
14. Buddy is a distributable preview channel whose complete channel,
    destination, package-coordinate, and Capability boundaries are isolated
    from Official; isolation does not require a different product-version
    string.
    The first Buddy smoke slice accepts branch-controlled publication risk only
    for its dedicated disposable GitHub Packages package. Every repository
    writer is inside that slice's trusted publisher TCB; an untrusted writer
    blocks the slice until access is reduced below Write/Maintain/Admin or an
    independently enforced publisher boundary makes package-write Capability
    and destination access unavailable to writer-authored workflows. Ref
    restrictions and workflow governance alone are insufficient remediation.
    Future Buddy destinations do not inherit it.
15. Official publication requires an authoritative target revision and an
    Authorization Record bound to an immutable Publication Snapshot digest.
16. Original Buddy and Official Attempts, Snapshots, Observations, Actions,
    Receipts, and Outcomes remain immutable.
17. Forced correction is modeled as a separately authorized Break-Glass
    Remediation with append-only before-and-after evidence.
18. CI decisions bind the actual GitHub candidate tree and, for pull requests,
    its base and head commits.
19. Release actions bind the target commit, Release Unit, frozen plan, artifact
    digests, and plan-specific authorization.
20. CI and Release Qualification Targets are fully closed before execution;
    unresolved Project Nodes, Release Units, inputs, or obligations are blocking
    rather than implicitly excluded.
21. Executors may perform mechanical discovery but cannot change the semantic
    content of an accepted Plan.
22. Decision aggregation verifies Evidence ownership and integrity without
    repeating the executor's quality check.
23. Success requires a ready Plan and satisfied Evidence for every required
    obligation; skipped, missing, unknown, cancelled, timed out, and conflicting
    states cannot become success.
24. Diagnostics explain structural state but never determine the verdict.
25. Final Decisions are append-only; required reruns create new Decisions while
    GitHub checks project the latest authoritative result. Advisory Evidence
    remains outside the authoritative Decision.
26. Publication credentials are externally granted through short-lived,
    narrowly scoped platform Capabilities. For normal destinations, independent
    trusted side-effect executors enforce exact immutable Plan, artifact,
    Attempt, and action bindings. Under the named First-Slice Buddy Risk
    Exception, the target-revision publisher validates by contract but is not an
    independent adversarial enforcement boundary; trusted repository writers
    may disregard or bypass it.
27. Release retry uses whole-release replay rather than GitHub failed-job
    resumption.
28. Release builds are required to be bit-for-bit reproducible as a Release Unit
    business contract; the delivery system does not certify reproducibility by
    duplicate building.
29. Partial publication is handled as an append-only Saga with
    projection-atomic reconciliation rather than automatic rollback.
30. Every Release Attempt performs read-only projection observation before
    authorization; cancellation adds no separate recovery workflow. An absent
    coordinate with no retained operational lineage is legitimate
    initial-publication state.
31. CI may cancel superseded candidate runs. Release lineage and request
    coalescing use complete Release Execution Identity. Every live mutating
    action binds complete deterministic Destination Adapter resource keys and
    serializes against overlapping keys. Package keys include exact External
    Package Coordinate; non-package keys are Adapter-defined. Release never
    cancels in-progress publication.
32. Cache availability may affect performance but never correctness, scope,
    Evidence, or verdict.
33. Publication Capabilities are requested just in time; their unavailability
    blocks publication without triggering a credential fallback.
34. Authoritative Plans, Evidence, Decisions, artifact identities, and Receipts
    must persist; optional telemetry may fail without changing correctness.
35. Record retention follows actual platform guarantees; Actions artifacts are
    operational rather than permanent release records. Destination absence does
    not require retained lineage or a permanent reservation record.
36. Ordinary pull-request CI has a P95 12-minute Final Decision SLO without
    weakening qualification semantics.
37. Repository project and build facts, target-bound canonical and native NBGV
    projections, quality execution, and destination behavior extend through
    stable adapters while CI and Release keep separate aggregate roots.
38. CI and Release Decisions include a structured, machine-readable explanation
    that also drives the human GitHub summary.
39. Each Release Attempt has one logical Plan lineage containing immutable
    Qualification and Publication snapshots; in-place Plan backfill is forbidden.
40. Architecture review begins from the ideal system direction and boundaries
    before considering the current implementation.
41. CI separates source-tree conformance from affected-system qualification.
    The repository-root HK gate owns the former as one opaque composite
    obligation.
42. The CI Planner does not inspect HK profiles, steps, file applicability, or
    internal planning and does not depend on HK plan serialization.
43. Projects select ecosystem-specific, semantically versioned quality presets
    or custom policy. Required semantic strengthening requires project opt-in.
44. Effective project quality policy uses nearest-ancestor, ecosystem-matching
    authoring without creating a directory domain object or cross-ecosystem
    preset semantics.
45. CI impact includes the full typed reverse Project Node closure and all
    publishable variants of every affected Release Unit.
46. Quality obligation identity is Quality Definition, concrete target, and
    concrete dimensions. Execution batching and mechanical reuse do not change
    that identity.
47. Required and advisory obligations execute in separate lanes. The CI
    Finalizer decides required obligations, while an Advisory Reporter presents
    non-authoritative results.
48. Incremental CI requires an authoritative comparison range. Schedule and
    explicit manual full validation use complete repository qualification; an
    invalid range does not silently fall back to full validation. During
    first-slice coexistence, the v3 pull-request check is shadow-only and manual
    `slice-validation` is non-authoritative and slice-scoped; neither is
    canonical full validation or a Ruleset required check.
49. A blocked CI Plan executes no authoritative partial obligations.
50. A domain abstraction is introduced only when concrete scenarios demonstrate
    independent behavior, identity, lifecycle, or policy responsibility.
51. Manual Release dispatch uses the selected Git ref as the exact target;
    independent target SHA or ref inputs are forbidden.
52. One Release Execution contains append-only whole-release Attempts for one
    Release Execution Identity. Official Product Identity is channel, Release
    Unit, and canonical NBGV version; Official Execution Identity adds target.
    Buddy Execution Identity is channel, Release Unit, and target. Separate
    admitted requests for one Execution Identity retain separate Intent records
    and initiate new Attempts in the same Execution. Every admitted,
    non-coalesced request creates one distinct Attempt; a pending dispatch
    replaced before execution creates none. A different target creates another
    Execution. Dry-run branches before live identity or admission, uses a
    separately namespaced request-scoped Simulation Identity, and never enters
    live Release lineage.
53. Buddy and Official each select a complete Release quality policy. Neither
    implicitly inherits CI project policy or the other channel's policy.
54. The Release Unit selects logical publication projections, Destination
    Adapters define mechanics, Qualification declares Capability requirements,
    and the normal v3 flow requests Governance-granted destination Capability
    only after a credential-free Capability Admission Gate validates exact
    authorization, Snapshot, summary, action, artifact, resource-key, group, and
    current Governance-freshness bindings. The first-slice writer-TCB exception
    does not make Environment a malicious-writer permission ceiling.
55. Projection-internal partial state requires read-only reconciliation and, if
    mutation is necessary, separately authorized Break-Glass Remediation.
56. Successful channel-level approval produces an Authorization Record for the
    exact Publication Snapshot. Terminal denial Evidence is admissible only with
    documented exact attempt-bound proof; the first-slice GitHub Environment
    surface does not provide it, so rejection is unknown, replayable, and
    non-authorizing. Workflow Delivery adds no approval watchdog. Cancellation
    or platform expiry while approval is pending may end the run without a
    Finalizer outcome. With no capability group started, the platform conclusion
    is sufficient no-side-effect evidence; possible capability execution instead
    requires reobservation.
57. Independent capability groups may execute in parallel. Actions within a
    group remain ordered, fail-stop, and individually receipted.
58. Release correctness relies on a documented lower-layer destination contract
    for atomic non-overwriting creation and durable exact-state observation,
    plus complete Adapter-declared resource-key serialization for
    repository-controlled contenders. Package key sets include exact External
    Package Coordinate and any additional Adapter-required keys. An incapable
    destination is unsupported rather than emulated through an application-level
    lock, tag witness, binding index, or permanent ledger.
59. The Finalizer aggregates immutable result bundles and does not query remote
    destinations again.
60. Artifact final bytes are frozen before publication; the initial scope
    excludes signing or notarization that changes artifact bytes.
61. External provenance projections execute only after the Authorization Record
    exists, even when they are prerequisites for other publication groups.
62. Every live Release Attempt requires channel approval of its Publication
    Snapshot, including an exact-satisfied no-op Attempt. A no-op Attempt
    requires no destination Capability.
63. Each action Receipt is persisted before a later mutation begins in the same
    capability group. A group result bundle references those Receipts rather
    than being their sole durable container.
64. Break-Glass Remediation revalidates the complete expected remote state after
    approval and immediately before mutation and reuses exactly the original
    action's complete frozen Adapter-declared mutable-resource keys. It never
    derives them from Product or Execution Identity. Normal Release does not add
    the same second observation under the governed single-writer assumption.
65. Buddy dry-run uses the unchanged target-derived native NBGV version and
    hypothetical Buddy destination coordinate. It has a separately namespaced
    request-scoped Simulation Identity derived only after Repository Model
    Snapshot validation, purpose-discriminated records, and no live Product,
    Execution, Attempt, authorization, Capability, Receipt, or mutation.
66. Shared Foundation is a logical mechanism layer with no aggregate root,
    independent business lifecycle, scheduler, authorization, or Finalizer.
67. Shared Foundation exposes record primitives and binding helpers but no
    universal CI/Release record envelope or Evidence model.
68. Providers resolve normalized facts and capabilities; Adapters execute
    closed family-specific mechanical invocations.
69. CI and Release own scheduling, batching decisions, fail-stop, retries,
    skips, and final aggregation. Foundation may expose compatibility hints only.
70. Providers and Adapters emit Mechanical Results. The calling context forms
    and admits authoritative Evidence, Observation Records, and Receipts.
71. Artifact Reference and internal provenance primitives are shared, while CI
    and Release apply independent purpose- and context-bound admission.
72. Provider, Adapter, Definition, and client implementations use a
    same-revision static catalog; initial scope has no dynamic plugin loading or
    remote plugin ABI.
73. A target-evaluating Provider runs in an unprivileged discovery boundary and
    emits a target-bound Fact Bundle; only a pure Provider may run directly in
    authoritative planning.
74. Foundation declares execution classes and capability requirements but never
    creates, grants, discovers, broadens, or downgrades credentials.
75. Foundation owns mechanism Definition schemas and catalogs; context policy
    selects Definitions and determines scope, requiredness, and projections.
76. Destination projection, action, Receipt, replay, and remediation semantics
    are Release-owned. Foundation provides generic client primitives only.
77. CI and Release may share transparent non-authoritative caches, but each
    rematerializes outputs and creates new context-specific provenance and
    authoritative records.
78. Foundation normalizes mechanical outcomes and diagnostics without emitting
    CI or Release business verdicts.
79. Explicit contract versioning is required only for intentional
    cross-revision exchange contracts, such as old reconciliation requests
    consumed by current remediation code.
80. Repository Model compilation binds a closed Provider Request Manifest and
    requires exactly one terminal Provider Result for every expected request.
    Each Release candidate run attempt branches by purpose and compiles exactly
    one same-revision request-local Snapshot. The resulting live Attempt or
    simulation pass reuses it without recomputation. A new run attempt compiles
    a new purpose-bound Snapshot, and cross-purpose admission is rejected.
81. Solely for the named first-slice Buddy live Attempt, owner-reviewed
    eligibility is waived for the selected-ref workflow, Planner, Finalizer,
    Providers, Adapters, compiler, authenticated clients, static catalogs,
    capability declarations, and publisher. CI and Official, future Buddy and
    production scopes, protected cross-revision compatibility code, and
    Break-Glass Remediation remain owner-reviewed or separately governed.
82. Privileged cross-revision consumers admit protected producer and original
    domain lineage; kind, version, and payload digest alone are insufficient.
83. The named live Buddy slice creates no Attempt until a current-attempt,
    exact-target Live Eligibility Decision proves consumer isolation and
    validates the fixed-source, unexpired protected-ref Governance attestation
    and `live_enabled: true`. Runtime does not claim complete writer or GitHub
    Packages grant enumeration. Human protected-source change response plus
    expiry bound staleness; CI and history cannot substitute. Immediately
    before Capability Admission, Release uses `contents: read` to freshly
    re-resolve and re-read that source and requires the boolean, provenance, and
    content to remain valid and identical; failure requires a new Attempt.
84. Exact first-slice npm package state requires matching coordinate,
    ownership, in-package immutable target witness, and bytes. Detached
    provenance is insufficient.
85. History-only artifact attribution is limited to platform-exposed facts and
    separately queried run/job phase data. Self-asserted historical producer or
    attempt lineage is diagnostic and never current authority.
86. First-slice npm publication is one compound version-and-routing-tag action.
    The tag is target-specific routing rather than provenance; exact state
    requires the tag-to-version mapping, while the in-package witness remains
    authoritative for target provenance.
87. Every physical Actions artifact name is deterministic and unique across its
    workflow run, normally by including `github.run_attempt` directly or in the
    hash preimage. Artifact IDs remain the only admission selector; same-run
    prior-attempt artifacts remain history-only.

## Open Decisions

No unresolved top-level terminology decisions remain.
