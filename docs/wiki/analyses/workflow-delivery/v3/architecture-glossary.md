# Workflow Delivery v3 Architecture Glossary

## Status

Architecture version: **v3**.

This is the normative glossary for the clean v3 implementation line. It records
the approved current architecture rather than completed rollout or retry
ceremony.

The current implementation remains disabled with `live_enabled: false`.

Confirmed entries should remain stable. Any unresolved term is identified
inline and must not be treated as settled architecture.

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
reconciles destination state, obtains authorization when a publication action
is required, and records the resulting external state.

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
after human approval through the literal Approval Environment
`workflow-delivery-v3-buddy-approval`. Before mutation, the publisher validates
the complete Publication Authorization and persists the
mutation-may-have-started marker. A successful `published` Publication Result
carries authoritative exact post-action readback; a controlled failed Result
preserves mutation classification, diagnostics, and any available post-action
remote evidence without becoming successful.

### Buddy

A Release Delivery policy channel for distributable, non-authoritative preview
releases.

Buddy may produce externally installable outputs. Normally its target
eligibility is channel policy. For the first `hcoona-release-smoke-npm` live
GitHub Packages slice, any same-repository ref selected by
`workflow_dispatch` is eligible without protected-ref or CODEOWNERS approval.
Its selected-revision control must strictly admit the active protected
Governance schema; an older incompatible ref fails before Execution lookup,
Attempt creation, or any Environment job. Compatible refs use the same Release
planning, build, qualification, evidence, and side-effect lifecycle as
Official. The normal intended action names the dedicated smoke coordinate,
while Official credentials and destinations remain separate. The first-slice
GitHub Packages token is repository-scoped and is not isolated to that
coordinate.

Buddy does not occupy an Official canonical destination or create an
authoritative production release record.
Buddy artifacts and evidence are not promoted to Official.

Buddy uses the frozen native NBGV product version unchanged. Buddy and Official
may therefore have the same version string while retaining distinct complete
publication coordinates and authority boundaries.

Buddy is not a separate release implementation and is not merely another name
for dry-run.

### First-Slice Buddy Risk Exception

The explicitly accepted first-slice trust exception for live publication of
the dedicated `hcoona-release-smoke-npm` coordinate to GitHub Packages.

The selected same-repository ref resolves to one exact SHA that supplies the
workflow, control, Planner, Finalizer, publisher, and Release target. Protected
Governance is fetched independently from `main`. The replacement uses exact
schema `workflow-delivery/v3/normal-live-governance-attestation-v2` as a
compatibility fence. V2 replaces the disabled v1 contract because native
destination acceptance has a different closed field set; v1 is not an
admission alias. Superseded selected-revision parsers fail before any
Environment job. An action-bearing Attempt uses the literal Approval
Environment `workflow-delivery-v3-buddy-approval`; after approval and complete
Publication Authorization, the target-revision publisher receives short-lived
repository `GITHUB_TOKEN` with effective `packages: write`, no PAT, and no
`id-token: write`.

`hcoona` is the sole accepted writer and publisher TCB member. Controls against
outsiders, accidental operators, and mistakes remain relevant. A malicious
accepted writer is not constrained by protected `main`, Environment approval,
workflow permissions, static-reference policy, or exact action validation.

The GitHub Packages credential principal is repository `hcoona/three`. Every
package whose package-side Actions grant authorizes that repository is in the
effective publisher blast radius. The exact smoke coordinate/action contract
governs intended operation and reconciliation; it is not token or package
isolation. Official npmjs PAT, OIDC, secret, and destination boundaries remain
separate and unchanged.

The bounded static-reference policy proves only that no prohibited direct
reference was found in its closed selector-to-fact catalog. Each retained
selector invokes one exact Ecosystem Authority Graph over exact source bytes or
a minimal isolated snapshot, then projects normalized policy facts.
The policy does not maintain a competing ecosystem grammar or schema, harden
the selected authority graph, prove absence of every runtime consumer, or constrain
`GITHUB_TOKEN` reach. Ordinary delete, restore, permission, visibility, and
admin actions remain outside normal publication; deletion or restoration
requires Break-Glass Remediation. Official and future Buddy destinations or
production packages do not inherit this exception.

Protected Governance identifies the accepted writer and relevant access
inspection, expires within 90 days, and uses `live_enabled` for fresh admission
and publisher-final-check control. A later protected-path touch invalidates the
current Attempt even after a byte-for-byte revert. Disablement cannot revoke a
publisher already past its final fresh check.

### First Proving Run

The first normal-Live run after Activation. It is dispatched from then-current
protected `main` through an explicitly supported REST API version whose success
response includes `workflow_run_id`.

The operator validates the response schema and reads back the returned workflow
and run identity, actor, `workflow_dispatch` event, exact actual head SHA,
`refs/heads/main`, and `run_attempt == 1`. A lost response or ambiguous
correlation is reconciled read-only and is never blindly redispatched. Later
normal Buddy runs may select arbitrary same-repository refs whose
selected-revision control strictly admits the active Governance schema.

Every authoritative normal-Live job independently rejects
`github.run_attempt != 1`. This includes eligibility and planning, the Approval
job, exact-satisfied no-op finalization, publisher, and Finalizer. The value is
a platform guard and diagnostic, not domain identity or a record, artifact, or
Publication Authorization binding.

### First-Slice Normal Live Activation

The separately authorized transition from the independently delivered disabled
implementation to normal Live. It uses one small protected Activation PR that
sets `live_enabled: true`. There is no separate Preparation PR, repository-wide
`main` freeze, pre-pinned Activation SHA, or activation tag.

The first proving run is dispatched from then-current protected `main` and is
correlated by the REST-returned `workflow_run_id` plus readback of its actual
identity and revision.

Normal Live activation is not a rollback boundary. Setting protected Governance
to `live_enabled: false` blocks future admission after fresh observation but
cannot revoke a publisher already past its final Governance check or reverse
package state.

### Approval Environment

The one first-slice authority-bearing GitHub Environment:
`workflow-delivery-v3-buddy-approval`.

It has required reviewer `hcoona`, the confirmed self-review setting, and one
exact Environment-scoped marker. The Approval job has no publication
capability. Its post-approval output is the complete Publication Authorization
for one action-bearing Attempt.

There is no first-slice Capability Environment. A generic Environment Profile
is deferred until a concrete second policy demonstrates independent semantics.
A future OIDC channel may introduce a channel-specific Environment only when
external destination trust validates its OIDC claims.

### Environment Configuration Sentinel

A fixed non-secret Environment-scoped variable whose exact value the Approval
job validates as its first authority-critical executable check before it emits
Publication Authorization. The job sees the resolved value and cannot determine
its source scope or prove whether same-name repository or organization
variables exist. Their absence is authenticated native
Governance/provisioning/activation readback and attestation evidence. Only
under that externally verified precondition does marker validation make
accidental GitHub implicit creation of a missing named Environment fail closed.
The marker does not prove reviewer, self-review, administrator-bypass,
branch-policy, secret, credential, or Environment-identity settings and never
replaces authenticated native configuration inspection.

### Single-Maintainer Approval Exception

The first-slice decision that permits sole accepted writer and reviewer
`hcoona` to approve their own normal Buddy deployment with
`prevent_self_review: false`. It applies only to repository `hcoona/three`,
package `@hcoona/hcoona-release-smoke-npm`, and approval Environment
`workflow-delivery-v3-buddy-approval`. Approval remains explicit operator
self-confirmation against mistakes, not independent review or a security
boundary. Any effective writer, reviewer, role, team, or relevant access change
requires `live_enabled: false` and a new Governance decision.

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

Whenever root HK runs, its lightweight static-reference policy runs in the
caller-selected `index` or `worktree` feedback mode. Separately, the first-slice
root HK implementation includes an expensive path-selected v3 control package
pytest step and runs that suite unconditionally only for manual
`slice-validation`. Both remain internal to Source-Tree Conformance and do not
create separate CI obligations, Evidence records, or jobs.

### Static-Reference Policy

The new-version bounded repository policy that reports prohibited direct static
references to the exact smoke package coordinate within a closed supported
catalog.

Supported surfaces are disjoint selectors paired with an exact Ecosystem
Authority Graph in the first-slice LLD. The graph binds authoritative artifact
schemas and standards, official library/CLI/runtime identities and versions,
provenance, public APIs or commands, input mode, admitted format generation,
required normalized facts, applicable prohibited forms, and unsupported cases.
Adapters emit package identity, reference kind, local path, and source-location
facts. The policy rejects only the coordinate and local-dependency forms
assigned to each selector row. The producer path is not globally prohibited
because build configuration may legitimately name it outside dependency
positions. Only the top-level `package.json` `name` at an exact known producer
path is allowed to equal the package name.

The canonical source kinds are exactly:

- `git-target`: enumerates and reads exact blobs from an explicit full commit
  SHA; only this source kind is admissible Live Eligibility evidence.
- `index`: enumerates and reads stage-0 Git index entries for staged or
  pre-commit candidate feedback.
- `worktree`: enumerates tracked plus eligible untracked paths and reads
  filesystem bytes for manual developer feedback.

Every result binds its source kind. Index or worktree bytes are never
represented as `HEAD` or commit identity. The result also binds schema, result,
the exact target when applicable, policy ID and digest, sorted exact
implementation identities actually loaded, canonical error kind when result is
error, and sorted findings. Counts and isolated-snapshot paths are
diagnostics only.

The invocation schema rejects an omitted or unknown source kind and malformed
required source parameters before Result construction. After one source kind
and its parameters are admitted, failure to deterministically enumerate, read,
or minimally materialize its declared exact source is
`source-acquisition-failed`. Callers propagate a pre-Result invocation failure
without synthesizing a Result; required cleanup failure may override an
admitted source-acquisition failure.

A finding is a prohibited reference, not a proven consumer. A clean result
proves only that no prohibited direct reference was found in the supported
catalog. Encoded or split construction, arbitrary runtime downloads, external
configuration, novel layouts, and universal consumer discovery are non-goals.
The policy does not constrain `GITHUB_TOKEN` reach.

### Ecosystem Authority Graph

The exact ordered set of authoritative source artifacts, official ecosystem
libraries or CLIs, and published standards that owns the semantic model for one
static-reference selector. Git Source Authority provides exact bytes. When an
authority is file-oriented, those bytes are materialized into a Session-owned
snapshot containing only declared files from the same source kind.

Different semantic layers may compose, such as the pnpm lockfile model, its
dependency-path and lockfile utilities, and its pure workspace and registry
specifier parsers. Two authorities must not compete over the same semantic
layer. Repository code validates the normalized fact envelope and applies
policy; it does not recreate lockfile schemas, descriptor or locator grammars,
comment handling, case rules, or normalization owned by the graph.

The graph identity includes source schema and standard versions; exact package,
CLI, runtime, tool, module, or assembly versions; lock or checksum provenance;
public APIs or commands; input and BOM behavior; admitted format generation;
required facts; and unsupported cases. A change to any of these changes the
bounded static-reference policy digest.

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

The deterministic identity used for Release Execution lookup, concurrency, and
request coalescing.

- Official Release Execution Identity is Official Product Identity plus
  immutable target.
- Buddy Release Execution Identity is channel, Release Unit, and immutable
  target.

Different targets always create different Release Executions, including when
they share one Official Product Identity or derive the same destination
coordinate. Overlapping live actions serialize on complete Adapter-declared
mutable-resource keys. Durable destination state determines absent, exact, or
conflict; failed pre-mutation Attempts reserve nothing.

An Attempt is identified by Release Execution Identity plus its unique
`workflow_run_id`. Plan and artifact digests are immutable bindings created
inside that Attempt. `github.run_attempt` is a platform guard and diagnostic,
not an identity or record field. Tags and branches are indexes.

Before live eligibility, identity lookup, coalescing, or admission, each request
branches to live release or release simulation. Each branch compiles one
same-revision, request-local Repository Model Snapshot for its purpose and
reuses it throughout that pass. The Snapshot binds purpose, request,
`workflow_run_id`, target, producer, and control identity. Cross-purpose and
prior-Attempt artifacts are rejected.

For the live branch, Official Product and Execution identities use the
Snapshot's target-bound canonical NBGV facts. Buddy Execution Identity needs
only channel, Release Unit, and target. The Snapshot already contains
authoritative native facts, including `npmPackageVersion`; Buddy identity
ignores them.

### Native Actions History Diagnostics

GitHub's native workflow and job history used for operator inspection only.

Workflow Delivery does not exhaustively discover, admit, snapshot, or aggregate
prior runs, reruns, artifacts, Attempt bindings, or outcomes as publication
authority. History does not establish current Evidence, eligibility,
Publication Authorization, artifact identity, Publication Result, Attempt Outcome,
aggregate Execution state, or exhaustive Attempt lineage.

Recovery starts from a new Release Intent and fresh destination observation.
Native history may explain what GitHub displayed, but missing, incomplete, or
expired history does not alter the current admission contract.

### Live Eligibility Decision

The immutable Release-owned pre-Attempt decision for the named live Buddy
slice. It is produced after exact target pinning and request-local Repository
Model compilation and before Execution lookup, concurrency, or Attempt creation.

It binds live purpose, request, `workflow_run_id`, selected ref and target SHA,
Repository Model digest, producer/control, exact static-reference policy result,
and protected Governance result. It does not bind `github.run_attempt`.

The static-reference evidence contains schema, result, source kind `git-target`,
exact full target, policy ID and digest, sorted exact implementation identities
actually loaded, canonical error kind when result is error, and sorted findings.
A clean result means only that no prohibited direct static reference was found
in the closed supported catalog.

The Governance source contract is repository `hcoona/three`, ref
`refs/heads/main`, and path
`.github/workflow-delivery/governance/hcoona-release-smoke-npm.json`. The
attestation identifies sole accepted writer/publisher `hcoona`, binds policy and
package, records relevant access inspection, issuer, inspection time and
limitations, expires within 90 days, and carries `live_enabled`.

The Decision binds repository/ref/path and attestation blob/content identity or
explicit generation. It does not require equality of the complete resolved
`main` commit. Missing, unreadable, malformed, expired, disabled, or
binding-mismatched state blocks without creating an Attempt. Any later commit
touching the protected path invalidates the Attempt, even if later content
reverts.

### Governance Freshness Revalidation

The protected Governance validation performed by the Approval job and repeated
by the publisher immediately before mutation.

It verifies repository/ref/path, schema and policy bindings, current expiry,
`live_enabled: true`, the admitted blob/content identity or generation, and
the absence of any protected Governance-path touch since eligibility. An
unrelated `main` commit does not invalidate the Attempt. A path touch does,
including change-then-revert, and restoration requires a new dispatch.

A false flag blocks fresh admission and the publisher's final fresh check. It
cannot revoke a publisher already past that check. This control adds no
independent malicious-writer boundary.

### Package Target Witness

Canonical `workflow-delivery/provenance.json` embedded inside the npm tarball
and therefore covered by its bytes and digest. It binds target commit, Release
Unit, canonical and native NBGV facts, Build Definition, catalog and control
digests, purpose, and schema. It excludes run and Attempt identities so builds
of one target remain reproducible across attempts. For first-slice npm,
immutable remote exactness requires the matching normalized package/version
coordinate, tarball bytes and digests, and embedded witness. Ownership,
repository association, visibility, and access are validated separately through
Package-Control Proof; a sidecar is not a substitute.

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

For the first-slice npm Release Unit, the contract requiring the same target
commit, frozen inputs, Build Definition, and toolchain to produce bit-for-bit
identical release artifacts.

The delivery system does not certify reproducibility by performing duplicate
builds. Remote observation records artifact digests and refuses to continue
when destination state conflicts with the current Attempt's snapshot-bound
desired projection state. That integrity check is not a general
reproducible-build certification.

A nondeterministic Release Unit requires a future explicit sealed-artifact
publication-resume design and is unsupported by this slice.

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
mutation key sets, the zero-or-one first-slice Publication Action,
Publication Result contract, and authorization basis before side effects.

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

The Release Finalizer is read-only and best effort. GitHub cancellation or
transport failure may prevent it from persisting an Attempt Outcome.

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

Each immutable target has a distinct Official Release Execution Identity. No
permanent global Product Identity-to-target ledger is required. A new manual
dispatch recompiles the request-local Repository Model and creates new
Attempt-specific Qualification and Publication Snapshots. Existing destination
state is accepted only when it matches snapshot-bound desired projection state;
a differing coordinate, target binding, or artifact digest requires
reconciliation. Ownership, repository association, visibility, and access are
admitted separately through Package-Control Proof. A missing, differing, or
unprovable package-control fact blocks admission and finalization without
classifying version projection state or producing a publication action.

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
reservation. No Intent reserves a coordinate absent from the active projection,
and that absence does not exclude a retained tombstone. Successful durable
destination creation establishes its observable package binding.

### External Package Coordinate

The address of one package-version resource: channel, destination, package, and
version.

It excludes Release Unit and target. Every live action that may establish or
mutate the package resource includes this exact coordinate in its
mutable-resource key set; an Adapter may also require additional keys.

### Package Routing Tag

A declared non-authoritative routing side effect distinct from package identity,
exactness, and provenance. The first-slice npm Adapter uses exactly
`buddy-sha-<40-lowercase-target-sha>` and requests that it map to the frozen
native `npmPackageVersion`. It never uses implicit `latest` or a shared moving
Buddy tag.

The tag remains inside the Publication Action, reviewer disclosure,
Authorization lineage, complete mutable-resource keys, Publication Result
post-action diagnostics, and the combined pre-action Observation/post-action
readback because `npm publish` mutates it. If the version is exact, tag state
cannot create a repair action or defeat `exact-satisfied`. If the version is
absent, a present or unprovable tag blocks action formation. A post-Observation
race may still move the tag under the explicitly accepted first-slice risk;
supported consumers resolve only exact `name@version`.

### Destination Operation Profile

The closed, canonical description of one admitted destination mutation
implementation. For first-slice npm it binds one stable profile identity,
registry and access mode, exact Node/npm versions, a static normalized
command/request template with typed package, version, tarball, and tag operand
slots, deterministic operand derivation and validation rules, all fixed command
options inside that template, highest-precedence
configuration, request-generation behavior, and prohibition of mutating
retries or secondary tag commands.

Its canonical digest is carried by the Publication Action. Protected
Governance reuses the destination-primitive attestation to bind the accepted
profile digest and the native evidence that admitted it. Approval compares the
action digest with current Governance and verifies that the immutable action is
a valid profile instantiation. Publisher admission repeats those checks,
verifies the actual pinned toolchain and effective command configuration, and
binds canonical match evidence in the mutation marker. Concrete package,
version, tarball, and tag values are bound exclusively by the Publication
Action. A toolchain or normalized request-profile change requires new
acceptance rather than compatibility inference.

### Native Acceptance Suite

The versioned destination-qualification scenarios and their closed canonical
before/after comparison shape. For first-slice npm the suite-owned shape
contains normalized package identity, complete active version-name inventory,
complete dist-tag mapping, remote-observed bytes/digests and witness for
scenario versions, and supported package-control facts. The
deleted/restorable scenario additionally uses separately authorized
package-admin evidence for the complete disposable-package deleted-version
inventory, targeted tombstone identity and continued restorability, and
original bytes/digests/witness after restoration. Runtime Observation never
receives those credentials or deleted-state facts. The suite explicitly
excludes enumerated server-generated timestamps, request identifiers, URLs,
and equivalent volatile metadata. Derived counters are recomputed from
included facts or validated against the scenario's declared delta.

Raw native responses and their digests remain evidence, but pass/fail compares
the suite-owned canonical shape with the scenario's allowed semantic delta. The
deleted/restorable case publishes and verifies a fresh disposable version,
deletes it with acceptance-only package-admin authority, requires sequential
identical- and differing-byte same-version publishes to fail definitively with
an empty active-plus-deleted semantic delta, then restores and verifies the
original bytes and witness. Any ambiguity, delta, lost restorability, or failed
restoration rejects the profile. Each newly admitted operation profile requires
acceptance captured after its implementation before initial activation. Later
Governance may reuse that generation while every bound input remains identical,
but action-bearing admission expires 90 days after capture. Binding change or
expiry requires recapture; expiry does not block zero-action exact-satisfied
finalization.

### Mutable-Resource Key

A deterministic destination-defined identity for external mutable state touched
by one Publication Action.

Every mutating action declares its complete key set through its Destination
Adapter. Package actions include the exact External Package Coordinate.
The first-slice standard npm publish action additionally includes the
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
Snapshot and action binding. Publication Authorization and publisher admission
validate it transitively through the immutable predecessor chain. Publication
Result admission reaches the same set transitively through the marker and
Authorization; the Result does not copy it.

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
- `incomplete`, including skipped, canceled, timed out, missing, or lost
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
the Publication Authorization transitively admits the exact Publication
Snapshot through its Approval Bundle. The Snapshot owns the Qualification
Decision, qualified artifacts, observations, one materialized action, complete
resource keys, Governance bindings, and Publication Result contract. An
in-progress side-effect execution cannot switch to a later Snapshot or
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

The request to deliver exactly one Release Unit through exactly one channel and
one immutable target.

For normal Buddy, manual initiation selects a same-repository ref that resolves
to one exact SHA. That SHA is both workflow/control revision and Release target;
there is no independent target input or second source identity.

Intent is request identity, not Product, Execution, or package identity.
Multiple admitted Intents may address the same Release Execution Identity.

### Release Execution

The conceptual channel-specific scope for one immutable Release Execution
Identity. Multiple Release Intents may address the same identity, and each
admitted dispatch creates a distinct Attempt.

The first slice does not maintain an authoritative aggregate Execution state or
exhaustive append-only Attempt lineage. Caller-held concurrency serializes the
identity; current-Attempt records and fresh destination observation supply
authority.

### Release Simulation

A dry-run execution that performs planning, build, qualification, observation,
and publication simulation without obtaining live publication Capability or
entering the Buddy or Official Release lineage.

Simulation branches before live eligibility, Product or Execution Identity
lookup, coalescing, admission, or Attempt creation. It first compiles and
validates one simulation-purpose Repository Model Snapshot binding request
identity, `workflow_run_id`, `github.run_attempt`, target, channel, Release Unit,
canonical and native version facts, producer, and control identity.

Only after that validation does the Planner derive the separately namespaced,
request-scoped Simulation Identity from the validated bindings. Later simulation
planning records bind both the Simulation Identity and Repository Model Snapshot
digest. A Buddy simulation uses the same target-derived native NBGV version
projection and hypothetical destination coordinate as live Buddy planning. It
retains `github.run_attempt` as part of its request-scoped identity so a rerun
is a distinct simulation pass; the normal-Live first-attempt-only contraction
does not apply. It never contains or acquires a live Product, Release Execution,
or Attempt identity, Publication Authorization, Capability, Publication Result, or
mutation. It may emit hypothetical requirements and actions and a Simulation
Outcome.

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
version bindings, logical operations, potential action schema, publication
policy, and deterministic complete mutable-resource-key derivation and
enforceability basis.

It authorizes only unprivileged build and qualification work. It does not
freeze an actual mutation action before artifacts and remote disposition are
known.

### Publication Snapshot

The second sealed snapshot in a Release Plan Lineage. It references the
Qualification Snapshot digest, preserves every frozen semantic field, and adds
actual artifact identities, content digests, provenance, snapshot-bound desired
projection state, destination observations, the exact zero-or-one first-slice
Publication Action and inputs, its complete Adapter-declared mutable-resource
keys, Qualification Decision, and Publication Result contract.

For an action-bearing Snapshot, the Approval Bundle binds the Snapshot and
immutable reviewer-summary artifact. After approval, the Approval job validates
the complete closure and emits the Publication Authorization. A zero-action
Snapshot bypasses approval and may finalize as `exact-satisfied`.

GitHub transports the snapshots as separate Attempt-specific artifacts even
though they share one logical Release Plan lineage.

### Publication Preparation Interruption

The terminal live-Attempt condition after an exact successful Qualification
Decision but before a durable Publication Snapshot exists.

Observation, Snapshot materialization, Snapshot upload, or platform
cancellation may produce this condition only when direct platform facts prove
that the publisher did not start and no Publication Authorization or mutation
marker exists. Missing Snapshot transport alone is not proof.

When the sole Release Finalizer runs and the direct facts suffice, it may record
`failed-before-publication` disposition and `possibly_mutated: false`, binding
the sole blocking Observation when present, otherwise the exact successful
Qualification Decision only when no Observation exists, as the latest valid
predecessor. The Observation directly binds that Decision. A retained
non-blocking Observation without a Snapshot has no admitted direct predecessor
and forms no Outcome. Publication Preparation Interruption is a descriptive
lifecycle condition, not an Attempt Outcome field or independent
classification. A later normal continuation is a new manual dispatch, but
reconciliation or remediation may be required first. The Finalizer does not
fabricate a Publication Snapshot or create domain Evidence from GitHub job
results. A durably persisted Publication Snapshot ends this phase. GitHub
cancellation or transport failure may prevent the Finalizer from retaining any
Outcome.

### Release Attempt

One coherent planning, build, qualification, observation, and reporting pass
within a Release Execution, with authorization and publication only when its
Publication Snapshot contains an action.

An Attempt is identified by Release Execution Identity and unique
`workflow_run_id`. It binds its Release Intent, request identity, Repository
Model Snapshot, Qualification Snapshot, Publication Snapshot when formed, and
conditional Publication Authorization and Result. `github.run_attempt` is not
part of the identity or any normal-Live record binding.

Every admitted, non-coalesced manual dispatch creates a new Attempt. GitHub
rerun commands are unsupported for normal Live. Every authoritative job
independently rejects `github.run_attempt != 1`, so partial reruns cannot form
authority from a mixed job graph.

An Attempt Outcome records the applicable validated lineage and reporting
result with an explicit disposition. An action-bearing Outcome with a Result
binds that Result directly and reaches its marker, Authorization, and action
through the Result. A marker-without-Result Outcome binds the marker.
Finalization is best effort and may be absent.

### New-Dispatch Retry

The supported retry model for a failed or incomplete Release Attempt.

Retry is a new manual dispatch with a new `workflow_run_id`. It recompiles,
rebuilds, requalifies, reobserves, and reapproves when an action remains. It
reuses no prior Snapshot, Environment approval, Publication Authorization,
artifact, or Publication Result.

For the first-slice npm Release Unit, the same target, frozen inputs, and
toolchain must reproduce identical bytes. Differing destination bytes fail
closed into reconciliation and separately authorized remediation.
Nondeterministic Release Units require a future sealed-artifact
publication-resume design and are unsupported here.

### Remote-State Observation

The mandatory read-only pre-authorization planning step in every Release
Attempt, including the first attempt. Build and qualification receive no
destination credential or publication capability. Observation may use public
APIs or the minimum read-only destination authority required for exact-state
readback, but receives no destination write authority, PAT, `id-token: write`,
Approval Environment, or publication capability.

Each logical publication projection is classified atomically against its
snapshot-bound desired projection state, not Product or Execution Identity.
Desired state is derived from the Qualification Snapshot and admitted qualified
artifacts and includes exact destination coordinate, target binding, and
artifact bytes or digest. For first-slice npm, authoritative
exactness is the normalized package name, frozen version, downloaded tarball
bytes and digests, and embedded witness in the active registry projection.
Runtime Observation does not enumerate deleted/restorable versions. The
target-derived dist-tag is separately observed non-authoritative routing
metadata. Each Observation Record binds its Release Attempt, exact successful
Qualification Decision, logical projection, immutable desired-state basis, and
canonical remote response and observed facts. It cannot bind a future
Publication Snapshot. The later Publication Snapshot seals admitted Observation
Records with the resulting desired state and materialized actions:

- state absent from the active projection may produce a publish action only
  under current Governance-bound tombstone acceptance;
- exact satisfied state produces no side effect;
- partial, unknown, conflicting, or unprovable state fails closed and requires
  reconciliation.

For first-slice npm, the Observation also embeds a Package-Control Proof for
the version-independent package container. That proof is an admission
precondition and is not part of version-projection equality.

An active-absent coordinate remains a legitimate action candidate when no
operational Release lineage is retained and does not require a binding index or
permanent ledger. It does not prove the version was never published, is not
retained as deleted/restorable state, or will accept creation. After
authorization, registry publication relies on the destination's attested
non-overwriting exact-version behavior. Pre-observed exact active version state
produces no action and may finalize as `exact-satisfied` success without
approval or publication lineage, regardless of tag state or tag-read
availability. Differing version bytes fail closed. A duplicate,
hidden-tombstone, conflict, non-success, or ambiguous response remains failed in
the current Attempt even when post-failure readback is exact. A new dispatch
may reobserve the exact active version and take `exact-satisfied`. It is never
version overwrite, delete-and-recreate, compensation, or tag repair.

Cancellation does not create a separate reconciliation workflow. A later
manual dispatch performs the same Remote-State Observation before any new
write.

For first-slice npm, a version absent from active state may form an action only
when the target-derived tag is successfully observed absent and current
Governance binds unexpired acceptance covering the deleted/restorable
same-version case. The one approved standard `npm publish --tag` invocation may
move that declared tag if an authorized external writer races after
Observation. The tag remains in the action, reviewer summary, Authorization
lineage, mutable-resource keys, pre-action Observation, and Publication Result
post-action diagnostics and readback, but not in exactness, identity,
provenance, installation, retry, or success. This bounded last-writer-wins
routing risk is accepted only for the dedicated smoke package and sole-writer
TCB. Repository concurrency covers repository-controlled publishers but is not
a registry lock.

### Publication Projection

A channel-selected logical remote product whose authoritative state must become
exact, such as a registry package version or a GitHub Release with its required
assets. Declared non-authoritative side effects remain within the action's
mutation footprint without becoming projection equality.

The Release Unit selects projections, the Destination Adapter defines their
mechanics, and Delivery Governance grants authority only when an action exists.
For the first slice, an exact active projection materializes zero actions and
requests no Capability. A version absent from the active registry projection
with an observed-absent target-derived tag may materialize one standard npm
publish action only after bounded native acceptance proves the pinned operation
safely rejects a deleted/restorable same-version reservation. Active absence
does not claim the version was never published, is not retained as a tombstone,
or will accept creation. Multiple ordered actions and projection-internal
partial progression require a future explicit design.

### Approval Bundle

The immutable reviewer and machine-admission payload prepared before the
first-slice Environment wait.

It directly binds the Publication Snapshot and immutable reviewer-summary
artifact by canonical payload digest and Artifact Reference. The Snapshot
remains sole owner of the selected ref and target, Qualification Decision,
artifact, action, and mutable-resource closure. The Approval job strictly
resolves and admits the complete immutable chain; human-readable summary alone
is not authority.

### Approval Job

The publication-credential-free authoritative job that references
`workflow-delivery-v3-buddy-approval`.

It validates the resolved exact Environment marker value as its first
authority-critical executable check, has no publication capability, freshly
validates protected Governance including path-touch anti-rollback, and strictly
admits the Approval Bundle and its transitive Snapshot, reviewer, artifact,
action, and resource closure. Its `contents: read` access for the protected
Governance reread means it is not fully credential-free. It cannot determine
marker source scope or prove same-name broader-variable absence; authenticated
native Governance/provisioning/activation readback and attestation provide
that evidence. After Environment approval, it compares the immutable action's
Destination Operation Profile with current Governance, verifies native
acceptance remains unexpired for action-bearing admission, and validates the
action as a profile instantiation. It neither reads mutable package-control
state nor claims to validate the publisher's effective runtime. It then
durably emits one Publication Authorization.

The publisher has an ordinary success dependency on this job. There is no
approval-finalizer or separate Capability Admission Decision.

### Publication Authorization

The complete post-approval admission record for one action-bearing Attempt.

Its standard producer/current-run envelope identifies the Attempt. It directly
binds the Approval Bundle plus approval-boundary and fresh-Governance evidence.
The immutable Bundle-to-Snapshot chain supplies the selected ref and target,
Live Eligibility Decision, reviewer artifact, exact artifact identities and
digests, the one action and Destination Operation Profile digest, and complete
mutable-resource keys. Authorization validates but does not copy those ancestor
facts. It does not contain credentials or bind `github.run_attempt`.

A zero-action exact-satisfied Attempt has no Publication Authorization.

### Package-Control Proof

A closed embedded value that validates mutable package-container
preconditions separately from immutable version-projection exactness.

For first-slice npm its subject is the destination/registry plus normalized
package identity. It binds supported authoritative endpoints, observed owner,
repository association, visibility, exposed access facts, observation time,
and canonical response digests. It is admissible only inside a parent that
binds the applicable protected-Governance identity or proof, derives expected
package-control values from that parent-bound Governance, and jointly validates
the observed facts. It copies neither expected values nor the Governance
content digest. Complete access-grant inventory that GitHub Packages does not
expose remains an explicit Governance-attested limitation.

The initial Remote-State Observation, publisher-boundary mutation marker, and
zero-action exact-satisfied finalization proof each embed a proof observed at
their own freshness boundary. The value is not a standalone decision, authority
record, or artifact.

### Exact-Satisfied Finalization Proof

The canonical
`workflow-delivery/v3/exact-satisfied-finalization-proof` record formed
immediately before zero-action success. It directly binds the zero-action
Publication Snapshot, fresh protected-Governance continuity proof, and fresh
Package-Control Proof. It also directly embeds fresh authoritative
exact-version readback proving the Snapshot-bound normalized package and
version, tarball bytes and digests, and witness remain exact. Its presence does
not authorize mutation.

It replaces the narrower
`workflow-delivery/v3/exact-satisfied-governance-proof` schema without a
compatibility alias. Attempt Outcome binds this record as its sole direct
predecessor for `exact-satisfied`.

### Mutation-May-Have-Started Marker

The durable record persisted immediately before the publisher's first mutating
destination operation. Failure to persist the marker blocks mutation.

It directly binds the Publication Authorization, the final publisher-side
Governance proof, and the final supported package-control proof observed at the
later mutation boundary. It also binds canonical evidence that the actual
pinned toolchain and effective command configuration matched the admitted
Destination Operation Profile. Its normal-Live producer/current-run envelope
identifies the publisher. The Authorization reaches the approved Snapshot,
action, resources, artifact, and Attempt closure through its Approval Bundle.
The publisher validates durable marker persistence before crossing the
mutation boundary.

Once present, absence of a later Publication Result means mutation status is
unknown and possibly mutated. The next dispatch must reobserve the destination.

### Publication Result

The canonical `workflow-delivery/v3/publication-result` record for the first
slice's one attempted or completed Publication Action. `ActionResult` is not an
alternate schema.

It directly binds the durable mutation marker, which reaches the Publication
Authorization, plus command classification, post-action evidence, mutation
classification, sanitized command/response diagnostics, and actual destination
outcome. Its post-action readback may include actual remote
coordinate/version state, remote-observed artifact digests, remote-extracted
witness, and observed state of the action-bound target-derived tag. Those
newly observed facts may be retained on either a published or controlled failed
Result.

The Result does not repeat the requested coordinate or tag, pre-action
Observation, expected artifact digests, expected witness, action, resources, or
other authority already reachable through
`Result -> marker -> Authorization -> Approval Bundle -> Snapshot`. The
before/after audit view combines that Snapshot lineage with the Result's
post-action readback.

A `published` Result requires definitive command success,
`mutation-classification: mutated`, and successful authoritative exact-version
readback. Conflict, non-success, or ambiguous responses remain failed in that
Attempt even when readback is exact. Post-action tag state remains diagnostic
and never determines the publication outcome.

The first-slice Publication Snapshot admits exactly zero or one action. An
action-bearing publisher forms one logical Result for each controlled
post-marker terminal state and initiates one logical persistence operation.
Transport may retry only the same immutable payload without creating another
logical Result. The current DAG exposes one nullable scalar immutable
publication terminal reference to the Finalizer. It points to the Result when
one was durably persisted, otherwise to the marker when one was durably
persisted, otherwise it is null. Result takes precedence; this transport does
not introduce a wrapper schema. Only null or one well-formed, correctly bound
marker or Result Artifact Reference is admitted; malformed, non-scalar,
misbound, or other-kind input fails closed. A referenced Result resolves the
marker through its direct lineage. A Result cannot precede the mutation marker
for an action that may mutate. A failure before the marker emits no Result. A
terminal marker without a durable Result remains unknown and possibly mutated;
no later component repairs or synthesizes the missing Result.

### Attempt Outcome

The best-effort read-only final record for one Attempt.

Its authoritative classification is the disposition
`exact-satisfied`, `published`, `failed-before-publication`,
`publication-failed`, or `unknown`, plus `possibly_mutated`. Platform job
conclusions remain execution facts and are not Attempt Outcome dispositions.
Human-readable result summaries and operator guidance are non-authoritative
projections outside the canonical record.

Successful outcomes have an explicit disposition:

- `exact-satisfied`: destination state was already exact; no approval,
  Publication Authorization, publisher, destination write or publication
  credential, Publication Capability, marker, or Publication Result exists;
  only minimum read-only Observation authority may have been used, and a fresh
  protected Governance continuity proof was admitted immediately before
  success. The publisher conclusion is `skipped`, and no Approval Bundle or
  other action-bearing lineage exists; or
- `published`: the complete Publication Authorization and a successful
  Publication Result with definitive command success and authoritative exact
  post-action readback exist and validate.

`failed-before-publication` requires direct current-DAG proof that the isolated
publication step never started: either the publisher was skipped or a started
publisher exposes the exact platform-derived `skipped` outcome for that step.
Missing or script-produced execution facts do not provide that proof.

The HLD terminal-state matrix defines the valid classification tuples and
Publication Result/platform compatibility. Contradictory lineage fails closed.
GitHub cancellation or Finalizer transport failure may leave no durable
Attempt Outcome.

The canonical Outcome contains exactly one tagged direct predecessor. It is the
exact-satisfied finalization proof for `exact-satisfied`; Publication Result for
`published` or `publication-failed`; marker for marker-without-Result
`unknown`; zero-action Publication Snapshot for missing-proof `unknown`; or,
for a pre-marker action path, the Publication Authorization, otherwise a
persisted Approval Bundle, otherwise the action-bearing Snapshot, otherwise the
sole blocking Observation, otherwise the exact successful Qualification
Decision only when no Observation exists. A selected Observation directly
binds that Qualification Decision. A retained non-blocking Observation without
a Snapshot has no admitted direct predecessor and forms no Outcome. Multiple
candidates at the selected predecessor tier are contradictory. The Outcome
does not copy ancestor lineage reachable through that predecessor.

### Release Reconciliation

The read-only exceptional process used when Remote-State Observation cannot
classify a projection as safely absent or exactly satisfied.

Reconciliation resolves partial, unknown, conflicting, or unprovable state.
Existing successful publication is not automatically rolled back.
It is a separate exceptional process, not an Attempt Outcome field, and it
never resumes the old Attempt. The first-slice implementation defers the
standalone workflow and Reconciliation Record; operator investigation plus a
fresh normal Observation is not formal Release Reconciliation. Break-Glass
Remediation is likewise deferred and, when implemented, must consume a valid
Reconciliation Record.

### Release Execution Serialization

The best-effort GitHub boundaries that serialize Release identity and access to
externally visible mutable resources.

- Release Execution concurrency and pending-request coalescing use the complete
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
identity slot through terminal workflow state, including the read-only Finalizer
when it runs. A superseded pending caller never enters admission and creates no
Attempt.

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
Decision, Publication Authorization, mutation-may-have-started marker,
Publication Result, or Remediation Record.

Authoritative Delivery Records must be persisted before a later stage relies on
them. If the mutation marker exists but the Publication Result cannot be
persisted, the Attempt is unknown and possibly mutated; a later dispatch
observes remote state.

Performance metrics, optional diagnostic logs, dashboards, and notifications
are telemetry rather than Authoritative Delivery Records. Their failure may
reduce observability without changing the decision.

### Platform-Native Record Retention

The retention model that uses each existing platform for the facts it can
actually preserve.

GitHub Actions Plans, Evidence, Publication Results, reports, and build
artifacts are operational records available only within the configured Actions
retention window. In this public repository, GitHub supports at most 90 days. The
first-slice LLD uses 45 days for Release control and artifacts so retention
exceeds the platform Environment gate-expiry window, currently up to 30 days;
activation blocks if repository policy cannot provide that margin. Fresh
authenticated preactivation and post-merge readback must prove the effective
repository setting permits at least 45 days. Retention or pending approval does
not freeze the protected document's `live_enabled` value or extend the
at-most-90-day Governance attestation. Approval and publisher checks freshly
verify repository/ref/path, blob/content identity or generation, path-touch
anti-rollback, expiry, and the flag. Unrelated `main` commits do not invalidate
the Attempt.

Longer-lived release identity and provenance rely on Git tags, registry
package/version records, GitHub Release objects when selected, and GitHub
Artifact Attestations with public Sigstore transparency-log publication.

After Actions records expire, a new dispatch may use only facts still provable
from current records and the destination. Unprovable exact state fails closed.

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
purpose, `workflow_run_id`, target, control identity,
request artifact, immutable transport identity, and Bundle digest. Admission
requires the current purpose and workflow run and rejects a cross-purpose or
prior-Attempt Bundle. For normal Live it does not bind `github.run_attempt`. It
contains no CI or Release policy.

### Provider Request Manifest

The closed authoritative list of Provider requests required for one Repository
Model compilation.

It binds request identity, purpose, `workflow_run_id`, exact target, producer
and control identities, static catalog, Provider implementation and execution
mode, request digests, and expected terminal result identities. Normal-Live
records do not bind `github.run_attempt`. Compilation requires exactly one
terminal Provider Result per entry.

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
observation, publication, successful-result evidence, mutability, digest
visibility, and remediation semantics for one destination family. It defines
complete deterministic mutable-resource keys for every mutating action.

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

Actions artifact names are non-authoritative, collision-safe indexes within one
workflow run, with overwrite disabled. Producers capture immutable artifact ID,
digest, and URL. Current-Attempt consumers fetch only by ID and verify record
kind, producer, `workflow_run_id`, target, purpose, payload identity, and
digest. Normal-Live artifacts do not bind `github.run_attempt`. Name fallback,
latest-artifact selection, and history-derived authority are invalid.

### Mechanical Result

A family-specific Provider or Adapter output that reports normalized facts,
outputs, outcomes, and diagnostics without expressing a business verdict.

CI or Release binds the result to its own Plan or Attempt and forms the
authoritative Evidence, Observation Record, or Publication Result.

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
workflow, ref, job and revision, context-required request and run identity,
original domain lineage, payload digest, and compatibility rules.
Same-revision internal records do not require a universal API version.

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
artifacts, destination observations, planned actions, Publication Results, outcomes,
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

Independent trusted side-effect executors generally validate the Publication
Authorization, Publication Snapshot, artifact digests, action IDs, and Attempt
before using capability. Successful Publication Results reach the authorized
semantic action transitively through their marker lineage and record the actual
platform identity used.

For the First-Slice Buddy Risk Exception, that executor is target-revision code
and is not an independent adversarial enforcement boundary. It validates exact
bindings by contract. The publisher obtains the repository `GITHUB_TOKEN` only
after the Approval job emits the complete Publication Authorization. There is
no Capability Environment. Every package granting `hcoona/three` Actions access
is within effective token reach, and the normal action contract does not
constrain a malicious accepted writer.

Qualification declares Capability requirements but cannot request, approve, or
create a live Capability. In the first slice, only the publisher may use one,
after ordinary success dependency on the Approval job and strict validation of
Publication Authorization, Snapshot, summary, action, artifact, resource-key,
and fresh Governance bindings. Delivery Governance grants capabilities through
platform controls such as job permissions, OIDC trust, and registry
trusted-publishing policy.

Capabilities are destination-specific to the extent supported by the platform.
Buddy cannot reach Official destinations, dry-run receives no live Capability,
and Break-Glass Remediation uses a separate remediation Capability.

A Plan, artifact, attempt, or approval change invalidates the executor's
authorization to use a previously obtained capability even when the external
credential format cannot encode every such binding.
