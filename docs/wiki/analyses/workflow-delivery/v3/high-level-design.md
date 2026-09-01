# Workflow Delivery v3 High-Level Design

## Status

Architecture version: **v3**.

Review state: **Confirmed; approved normal Live baseline incorporated on
2026-08-31**.

This page is the normative high-level design for the clean v3 implementation
line. It realizes the confirmed
[Workflow Delivery v3 Requirements](./requirements.md) and intentionally does
not inherit the v1 or v2 control-plane architecture.

Normative terminology is maintained in the
[Architecture Glossary](./architecture-glossary.md).

The current implementation remains delivered but disabled with
`live_enabled: false`. Completed acceptance, provisioning, and retry ceremony
belongs to Git history and the append-only log rather than this current
architecture.

## Architectural Shape

```text
                         Delivery Governance
             policy authority / review / environments / OIDC
               |                                     |
        CI Qualification                       Release Delivery
      planner / finalizer                    planner / finalizer
               |                                     |
               +------------------+------------------+
                                  |
                           Shared Foundation
 record / digest / repository / artifact / provenance / build / quality / clients
```

CI Qualification and Release Delivery are peer bounded contexts. Delivery
Governance is an external authority boundary, not a third business system. The
Shared Foundation provides mechanisms and normalized facts, not business
policy.

CI and Release each own their Planner, Finalizer, aggregate roots, Plans,
Evidence, Decisions, and state machines. There is no universal cross-system
Planner or Finalizer.

## System Boundaries

### CI Qualification

CI Qualification:

- evaluates the GitHub candidate tree;
- maps changed paths through Project Nodes, dependency relationships, and
  global inputs to affected Release Units;
- closes the complete Qualification Target;
- builds every publishable variant of each affected Release Unit;
- executes required and advisory quality obligations;
- admits Evidence and emits an explainable Final Decision; and
- publishes the latest authoritative Decision through the required GitHub
  check.

It has no publication side effects and does not authorize Release.

### Release Delivery

Release Delivery:

- accepts a Release Intent for one Release Unit and immutable target commit;
- verifies target eligibility through Delivery Governance;
- independently plans, builds, and qualifies the complete Release Unit closure;
- creates a Publication Snapshot bound to exact artifacts and destination state;
- prepares an immutable Approval Bundle before an action-bearing first-slice
  Environment wait;
- emits one Publication Authorization after approval for an action-bearing
  Attempt;
- performs the exact zero-or-one Publication Action through the Side-Effect
  Zone;
- for the one-action path, persists a pre-mutation marker and one Publication
  Result; a successful `published` Result embeds exactly one Receipt; and
- handles retry through a new manual dispatch and complete rebuild.

It does not consume CI Plans, Evidence, artifacts, status checks, or verdicts.

### Buddy and Official

Buddy and Official are Release policy channels over the same Release machinery.

- Buddy produces distributable but non-authoritative previews. Its intended
  action uses a Buddy-specific channel, destination, and package coordinate,
  and its package version remains the frozen native NBGV product version. For
  first-slice GitHub Packages, this does not imply token isolation to one
  package; all packages granting `hcoona/three` Actions access are in reach.
- Official Product Identity is channel, Release Unit, and canonical NBGV
  version. Official Release Execution Identity adds the immutable target.
  Different targets with the same Product Identity are separate Executions.
  Ecosystem publication and dry-run use the exact frozen native NBGV projection,
  such as `npmPackageVersion`, unchanged. Publication Authorization binds the
  immutable Publication Snapshot.
- Buddy Release Execution Identity is channel, Release Unit, and immutable
  target.
- Buddy and Official may use the same product-version string when their
  complete destination coordinates are isolated.
- Buddy artifacts, Evidence, and Receipts are never promoted to Official.
- Non-authoritative branches may exercise Official dry-run behavior but cannot
  obtain live Official publication capability.

### Delivery Governance

Delivery Governance controls:

- protected branch and target eligibility;
- policy and control-code review;
- protected environments and required reviewers;
- OIDC and registry trust policy;
- Publication Capability grant and revocation; and
- Break-Glass Remediation approval.

A business workflow may request authority but cannot grant authority to itself.

## Repository Facts and Core Domain Model

### Project Node

A Project Node is a normalized technical fact discovered from an ecosystem
manifest or workspace. Examples include a .NET project, Python project, or
JavaScript package.

Project Nodes and their dependency relationships describe how the ecosystem
builds software. They are not independently authored business objects and do
not define ownership, qualification policy, versioning, or publication
identity.

Repository Model Providers also identify global inputs and path relationships
that affect Project Nodes, such as `Directory.Build.props`, workspace
configuration, or an explicitly declared cross-ecosystem generated-source
dependency.

### Release Unit

A Release Unit is an independently versioned and delivered product unit
containing one or more artifact variants and the Build Definitions needed to
produce them.

Release Units are explicit business objects. Their Build Definitions select
entry points, while ecosystem build systems retain responsibility for internal
project dependency closure and output composition.

### Qualification Target

A Qualification Target is the immutable object covered by a quality decision.

- CI derives it from a candidate change, affected Project Node closure, affected
  Release Units, and applicable repository obligations.
- Release derives it from the complete Project Node and declared-input closure
  needed by the Release Unit Build Definitions, the complete artifact variant
  set, and
  explicit compatibility obligations.

The target closes before execution. Unknown or unclassified scope is blocking.

### Aggregate Ownership

CI Qualification owns the CI Qualification Plan, CI Evidence admission state,
and CI Final Decision.

Release Delivery owns Release Intent, Release Attempt, Release Plan lineage,
Qualification Decision, destination observations, publication actions,
Receipts, and Release outcome.

Shared Foundation values may appear in either aggregate, but no aggregate
imports the other system's runtime state.

### Abstraction Discipline

A named domain abstraction must have concrete independent behavior, identity,
lifecycle, or policy responsibility. The architecture does not add a layer
solely to group existing objects or make the model appear more symmetrical.

Project Nodes remain technical repository facts because the current CI and
Release scenarios require no independent domain object between them and Release
Units.

## Shared Foundation

The Shared Foundation exposes six stable mechanism families:

1. Shared Record Primitives provide canonicalization, digest, strict record
   validation, and exact binding functions without defining a universal record
   envelope.
2. Artifact and Provenance Primitives separate logical, content, transport, and
   producer identity while leaving admission to CI or Release.
3. Repository Model Providers and the Repository Model Compiler normalize
   ecosystem manifests, workspaces, Project Nodes, dependency relationships,
   path impact, and capabilities.
4. Static Definition Catalogs describe Build, Quality, and other mechanism
   contracts without self-activating into a Plan.
5. Build and Quality Adapters execute closed family-specific invocations and
   return mechanical results rather than authoritative Evidence.
6. Execution and Client Primitives declare execution classes, capability
   requirements, mechanical outcomes, cache hints, and generic platform calls
   without creating jobs or granting authority.

Adding an ecosystem normally adds Provider, Build Adapter, or Quality Adapter
implementations and policy mapping rather than modifying context-owned decision
semantics.

Repository Model compilation emits target-bound canonical NBGV facts and the
required native ecosystem projections. CI and Release select and freeze the
exact projection in their Plans and Build Requests. Build Adapters apply and
verify that frozen value; they do not recompute NBGV, derive another version, or
fall back to ambient manifest fields. Before those facts are compiled, the
NBGV-owning Provider checks out and remains pinned to the exact target with
complete ancestry and tags, equivalent to `fetch-depth: 0`, and fails closed on
shallow or otherwise incomplete history.

Destination projection classification, action planning, Receipt semantics, and
remediation belong to Release Delivery. Shared Foundation may provide generic
GitHub or registry client primitives, but it does not own a Destination Adapter
business contract.

Providers provide normalized facts. Adapters execute closed mechanical
operations. Neither decides business scope, downgrades obligations, authorizes
publication, or reinterprets verdicts.

## Governance and Trust

### Context-Owned Planning and Finalization

CI Qualification owns CI scope planning, obligation disposition, Evidence
Admission, and CI Final Decision.

Release Delivery owns Release planning, qualification finalization,
Publication Snapshot finalization, Receipt admission, and Release outcome.

Shared Foundation supplies mechanical canonicalization, digest, strict record
validation, fact compilation, artifact and provenance identity, closed
invocation, result normalization, and binding functions. It does not choose
scope, policy, authorization, or verdicts.

### Governed Same-Revision Control

CI uses its Planner and Finalizer from the tested candidate revision. Live
Release uses its Planner and Finalizer from the exact selected target revision.
Official requires that target to be protected and authoritative. The
`hcoona-release-smoke-npm` live Buddy GitHub Packages slice may use any
same-repository ref selected by `workflow_dispatch`, without protected-ref or
CODEOWNERS-approved eligibility. The selected ref resolves to one exact SHA,
and that SHA is both the workflow/control revision and Release target. Protected
Governance is fetched independently from `main`. Dry-run simulation uses the
Planner and Finalizer from the exact selected simulation revision and receives
no approval or live publication Capability. There is no independently selected
decision-code revision or runtime promotion protocol.

GitHub Governance supplies authority through control-code ownership, required
review, protected refs, workflow permissions, protected environments, and OIDC
trust. A change to planning, finalization, workflow control code, authoritative
record shape, minimum policy, executable Provider, Adapter, compiler,
authenticated client, static catalog, capability declaration, or cross-revision
compatibility code becomes eligible only as part of the reviewed revision that
contains it. The named first-slice Buddy exception instead accepts selected-ref
workflow, Planner, Finalizer, Providers, Adapters, compiler, authenticated
clients, static catalogs, capability declarations, and publisher without
protected-ref or CODEOWNERS eligibility. CI and Official, future Buddy and
production scopes, protected cross-revision compatibility code, and Break-Glass
Remediation remain owner-reviewed or separately governed.

These controls protect the normal process against outsiders, accidental
operators, and mistakes. They do not constrain a malicious accepted writer.

A control-code fix therefore creates a new candidate or Release target. A later
dispatch of an older target continues to use that target's original control
code. Exceptional state left by an older target is handled through
reconciliation or separately authorized remediation.

### Runtime Zones

The architecture has three runtime trust zones:

1. **Decision Zone:** Runs authoritative planning, Evidence Admission, and final
   decision logic from same-revision control code. It executes no
   target-defined project/build hooks and holds no publication credentials. For
   the named Buddy exception, the control code itself is branch-controlled.
2. **Build and Qualification Zone:** Executes candidate or release-target code.
   It holds no publication credentials and cannot approve itself.
3. **Side-Effect Zone:** Receives a short-lived destination Capability and
   consumes only verified immutable artifacts plus a fully materialized
   Publication Snapshot. It does not execute target-defined product/build code.

The zone separation above remains normative for CI, Official, simulation, and
future destinations unless separately approved. The first live Buddy
`hcoona-release-smoke-npm` GitHub Packages slice is an explicit trust
exception. After successful human approval through
`workflow-delivery-v3-buddy-approval`, its target-revision publisher runs
target-revision code with short-lived repository `GITHUB_TOKEN` and effective
`packages: write`. The publisher validates the complete Publication
Authorization before mutation. This exception permits target-revision control
and publisher code, not target-defined product/build code. It receives no PAT
and no `id-token: write`.

### First-Slice Approval and Publisher Authority

The first slice has one authority-bearing Environment:
`workflow-delivery-v3-buddy-approval`. It has required reviewer `hcoona`, the
confirmed self-review setting, and one exact Environment-scoped configuration
marker. A generic Environment Profile is intentionally deferred until a second
concrete policy requires one. There is no first-slice Capability Environment.
A future OIDC channel may introduce a channel-specific Environment only when
the external destination trust validates its OIDC claims.

For an action-bearing Publication Snapshot, Release prepares an immutable
Approval Bundle before waiting on the Environment. The bundle closes the target
and selected ref, Qualification Decision, Publication Snapshot, reviewer
summary, artifacts and digests, manifest and lifecycle information, and the
exact action with its complete mutable-resource keys.

The Approval job:

- references the literal Approval Environment;
- validates the resolved exact marker value as its first authority-critical
  executable check;
- has no publication capability;
- freshly validates protected Governance, including path-touch anti-rollback;
- strictly admits the Approval Bundle, Publication Snapshot, artifact, and
  action/resource closure; and
- after approval, durably emits one complete Publication Authorization.

The job cannot determine the marker's source scope or prove the absence of
same-name repository or organization variables. That absence is authenticated
native Governance/provisioning/activation readback and attestation evidence.
Only under that externally verified precondition does marker validation make
accidental implicit creation of the named Environment fail closed. The job uses
`contents: read` to reread protected Governance; the least-privilege claim
is that it has no publication capability, not that it is fully credential-free.

That Publication Authorization is the post-approval publication-admission
boundary. It binds every current-Attempt Governance, action, artifact, and
resource input and does not bind `github.run_attempt`. There is no
approval-finalizer, Capability Admission Decision, capability group, group
manifest, or group result bundle.

The publisher has an ordinary success dependency on the Approval job. It
strictly validates the Publication Authorization and exact closure, performs a
final fresh Governance check, and is the only step-running job with effective
`packages: write`. A `uses`-only caller may declare the same permission solely
as a non-elevating reusable-workflow ceiling and has no steps or direct token
use.

Immediately before its first mutating destination operation, the publisher
durably persists a mutation-may-have-started marker. Marker failure blocks the
mutation. After the attempted or completed operation, it persists one
Publication Result. A successful `published` Result embeds exactly one Receipt.
A controlled failed Result after the marker may omit the Receipt and must
preserve mutation classification and diagnostics. A missing durable Result
after the marker means unknown, possibly mutated state and requires fresh
observation on the next dispatch.

The GitHub Packages credential principal is repository `hcoona/three`. Every
package whose package-side Actions grant authorizes that repository is in the
effective publisher blast radius. Exact smoke coordinate, action, artifact, and
resource validation is an intended-action and reconciliation contract, not
token or package isolation.

`hcoona` is the sole accepted writer and publisher TCB member. Environment
approval, protected `main`, workflow permissions, exact validation, and the
static-reference policy remain useful against outsiders, mistakes, and
accidental operators. They do not constrain a malicious accepted writer to one
package. Official npmjs PAT, OIDC, and secret isolation remain separate.

### Protected Governance and Static Reference

The live eligibility read and the exact-satisfied, Approval, and publisher
fresh checks use the protected Governance source at repository `hcoona/three`,
ref `refs/heads/main`, and path
`.github/workflow-delivery/governance/hcoona-release-smoke-npm.json`.
The attestation expires within 90 days and identifies `hcoona` as the sole
accepted writer/publisher.

Admission binds repository, ref, path, and attestation blob/content identity or
an explicit attestation generation. It does not bind equality of the complete
resolved `main` commit, so unrelated `main` commits do not invalidate an
Attempt. Anti-rollback is monotonic: any commit touching the protected path
after the Attempt's eligibility read invalidates that Attempt, even if later
bytes revert. Governance restoration requires a new dispatch and Attempt.
`live_enabled: false` blocks fresh admission and the publisher's final fresh
check but cannot revoke a publisher already past that check.

The replacement uses exact Governance schema
`workflow-delivery/v3/normal-live-governance-attestation-v1`. Selected-revision
control must require that schema exactly. This intentionally makes superseded
parsers fail before Release Execution lookup, Attempt creation, or any
Environment job without substituting protected-main control code.

The new-version static-reference policy is bounded to a closed supported
catalog. Its canonical source kinds are exactly `git-target`, `index`, and
`worktree`. `git-target` enumerates and reads exact blobs from an explicit full
commit SHA; only `git-target` is admissible Live Eligibility evidence. `index`
enumerates and reads stage-0 Git index entries for staged or pre-commit
candidate feedback. `worktree` enumerates tracked plus eligible untracked paths
and reads filesystem bytes for manual developer feedback. Every result binds
its source kind. Index or worktree bytes are never represented as `HEAD` or
commit identity.

The catalog covers only disjoint path selectors paired with an exact Ecosystem
Authority Graph in the first-slice LLD. Git Source Authority supplies exact
bytes directly or materializes only declared exact-source files into a
Session-owned isolated snapshot for file-oriented APIs or commands. Each graph
binds authoritative artifact schemas and standards, exact library/CLI/runtime
identities and versions, lock or checksum provenance, public APIs or commands,
input mode, admitted format generation, required normalized facts, applicable
prohibited forms, and unsupported cases. Raw-byte, strict-UTF-8, and XML input
modes are explicit; no adapter performs replacement decoding or hidden
normalization.

Authoritative manifests or lockfiles, official ecosystem libraries or CLIs,
and published standards own manifest, lock, descriptor, locator, workspace,
and package-reference models. Different semantic layers may compose in one
ordered graph, but the design admits no competing authority for the same layer.
The graph performs no evaluation, candidate execution, installation, restore,
network access, undeclared file read, ambient configuration load, or external
write. Policy code owns only normalized prohibited-fact comparison,
repository-relative path policy, exact allowances, failure typing, and Result
construction.

The policy rejects direct, versioned, aliased, and workspace facts assigned to
each retained surface, plus normalized local dependency paths that resolve to
the known producer root. It does not reject that producer path globally because
build configuration may legitimately name it outside dependency positions. The
top-level `package.json` `name` field is allowed only at exact known producer
paths.

The result binds schema, result, source kind, exact target when applicable,
policy ID and digest, sorted exact implementation identities actually loaded,
canonical error kind when result is error, and sorted findings. The policy
digest binds the full authority graph. The invocation schema rejects an omitted
or unknown source kind and malformed required source parameters before Result
construction. Once the source request is admitted, exact-source acquisition
failure is `source-acquisition-failed`; encoding or authority rejection,
authority execution failure, inability to project a required fact, authority
mismatch, and required-root cleanup failure are distinct fail-closed errors.
Candidate paths and graph-owned projections follow one deterministic declared
traversal. The first typed non-cleanup failure is canonical; required cleanup
failure overrides it and preserves the earlier sanitized cause only as
diagnostic.
Findings are prohibited references, not proven consumers. Counts are
diagnostics only. The architecture makes no evaluator, dataflow,
exhaustive-consumer, trigger-catalog, or whole scanned-surface-digest claim and
contains no handwritten ecosystem grammar, lock schema, locator splitter, or
competing-authority hardening. Encoded/split construction, arbitrary runtime
downloads, excluded authority-less surfaces, external configuration, and novel
layouts remain explicit non-goals.

### Normal Live Activation Control

The implementation is delivered independently and remains disabled with
`live_enabled: false`. Activation is one small protected Activation PR. There
is no separate Preparation PR, `main` freeze, pre-pinned Activation SHA, or
activation tag.

Before activation, fresh authenticated readback must prove that repository
artifact retention supports at least 45 days. Destination acceptance must also
prove that the selected GitHub Packages mutation primitive conditionally
creates the complete version-and-tag projection without overwriting a
conflicting tag introduced after Observation. Standard `npm publish --tag`
does not provide that conditional operation and is not admitted. The first
slice remains disabled until a reviewed design identifies a supported primitive
and the required race acceptance passes.

After the Activation PR merges, the first proving run is dispatched from
then-current protected `main` through an explicitly supported REST API version
whose success response contains `workflow_run_id`. The operator validates that
response schema and reads back the returned workflow/run identity, actor,
`workflow_dispatch` event, exact actual head SHA, `refs/heads/main`, and
`run_attempt == 1`. A lost response or ambiguous correlation triggers
read-only reconciliation and never blind redispatch. Later normal Buddy runs
may select arbitrary same-repository refs whose selected-revision control
admits the active Governance schema. Before activation, retained dispatchable
refs are checked or fixture-proven to implement the one-Environment contract
or reject that schema before any Environment job or deployment.

Every authoritative normal-Live job independently requires
`github.run_attempt == 1`, including eligibility and planning, the Approval
job, exact-satisfied no-op finalization, publisher, and Finalizer. This protects
against partial reruns. The value is a platform guard and diagnostic, not
domain identity or a record, artifact, or Publication Authorization binding.

An Attempt Outcome with `result: success` has one of two explicit dispositions:

- `exact-satisfied`: read-only reconciliation found exact destination state and
  used no Environment approval, Publication Authorization, publisher,
  destination write or publication credential, Publication Capability, marker,
  Publication Result, or Receipt; minimum read-only Observation authority may
  have been used; or
- `published`: the action completed successfully under the complete Publication
  Authorization and produced a Publication Result with exactly one embedded
  Receipt.

Incomplete, unknown, conflicting, partial, or possibly mutated state is never a
no-op success. If current-DAG facts prove the publisher never started and the
read-only Finalizer runs, it may retain a replayable
`failed-before-publication` outcome without reconstructing an Environment
rejection reason. GitHub cancellation or finalizer transport failure may leave
no durable Attempt Outcome. Retry is always a new manual dispatch that rebuilds,
requalifies, reobserves, and reapproves when an action remains.

Flag-off blocks fresh admission and the publisher's final fresh check. It is not
package rollback or instantaneous capability revocation, and it cannot stop a
publisher already past that check.

## CI Qualification Design

### Flow

```text
GitHub event
  -> candidate identity (base/head/tested merge, merge-group, or push SHA)
  -> repository model facts
  -> closed CI Qualification Plan
       + opaque source-tree conformance
       + affected-system quality obligations
  -> parallel required and advisory execution lanes
  -> CI-owned Evidence envelopes
  -> required Evidence Admission
  -> immutable required Final Decision
  -> stable required-check projection
  -> non-authoritative advisory reporting
```

### Responsibility Split

The Planner owns semantic scope. It resolves the candidate identity, affected
Project Node and Release Unit closure, project-selected quality policy, concrete
quality targets and dimensions, artifact variants, and required and advisory
obligations.

The repository-root HK gate implements one required opaque
`SourceTreeConformance` definition. The Planner binds the definition and
candidate input but does not inspect HK profiles, steps, file applicability, or
internal planning.

Whenever root HK runs, its lightweight static-reference policy runs in the
caller-selected `index` or `worktree` feedback mode. Separately, HK includes an
expensive path-selected v3 control-package pytest step for the complete v3
control package/catalog/test tree, first-slice descriptors, the exact
first-slice Release policy, every v3 workflow consumer, direct Python
workspace/lock inputs, and HK configuration/helpers. Unrelated product source
alone does not trigger that pytest step. Manual `slice-validation` runs it
unconditionally. Both remain internal to the opaque root-HK invocation and do
not create another CI obligation, Evidence record, or job.

Executors resolve only mechanical details required to perform an immutable
Plan. They may not add, remove, substitute, or downgrade planned scope.

Evidence producers execute in the Build and Qualification Zone. Evidence
Admission and Final Decision execute in the Decision Zone.

Success requires a ready Plan and `satisfied` Evidence for every required
obligation. Missing, skipped, canceled, timed-out, unknown, and conflicting
states cannot become success.

The CI Finalizer considers required obligations only. Advisory obligations use
Plan-bound Evidence and a separate non-authoritative Reporter so they do not
delay or indirectly gate the required check.

During first-slice coexistence, v3 CI has only two slice-scoped modes:

- a shadow pull-request incremental check for slice-relevant changes; and
- non-authoritative manual `slice-validation` of the complete
  `hcoona-release-smoke-npm` slice.

Neither becomes a Ruleset required check or replaces v1 required CI, and v1 and
v3 do not issue parallel authoritative Decisions. Manual slice validation is
not repository-wide full validation. Canonical explicit and scheduled full
validation remains the complete-repository mode defined by the CI MLD and is
deferred until every active Project Node, Release Unit, and repository
obligation is modeled.

## Release Delivery Design

### Flow

```text
manual Release Intent on selected Git ref
  -> selected ref resolves one exact same-revision target/control SHA
  -> branch by requested purpose
       live release:
         -> every authoritative job requires github.run_attempt == 1
         -> compile exactly one live-purpose request-local Repository Model Snapshot
         -> produce exact-target git-target static-reference result
         -> independently read protected Governance from main
         -> validate live channel, Release Unit, and target lineage eligibility
         -> derive Product/Execution Identity inputs from that Snapshot
         -> enter one Release Execution concurrency-scoped caller
         -> caller invokes same-revision reusable live-Attempt workflow
              -> identify Attempt by Execution + workflow_run_id
              -> reuse the same Snapshot throughout the Attempt
              -> Qualification Snapshot, build, qualify, observe
              -> Publication Snapshot with exactly zero or one action
              -> zero action: exact-satisfied read-only finalization
              -> one action: Approval Bundle, Approval Environment,
                 Publication Authorization, publisher marker, action,
                 Publication Result, with exactly one Receipt on published
              -> best-effort Attempt Outcome
         -> caller holds the Execution identity slot through terminal workflow state
       release simulation:
         -> compile exactly one simulation-purpose request-local Snapshot
         -> bind workflow_run_id and github.run_attempt
         -> create separately namespaced request-scoped Simulation Identity
         -> reuse the same Snapshot throughout the simulation pass
         -> simulate channel policy, build, qualification, observation,
            requirements, and hypothetical actions
         -> Simulation Outcome
         -> no live Product/Execution/Attempt identity,
            Publication Authorization, Capability, Receipt, or mutation
```

### Release Plan Lineage

Each Release Attempt has one logical Plan lineage with two immutable snapshots.

- Before live eligibility or identity lookup, each request branches to live
  release or release simulation. Each branch compiles one same-revision
  Repository Model Snapshot for its purpose and reuses it throughout that pass.
  Snapshot authority binds request, `workflow_run_id`, target, producer,
  control, and purpose. For normal Live, `github.run_attempt` is excluded from
  domain and transport bindings because the all-authoritative-job guard makes
  first attempt a platform invariant. Simulation retains its existing
  run-attempt binding and treats a rerun as a distinct simulation pass.
  Cross-purpose and prior-Attempt artifacts are rejected.
- For the named live Buddy slice, the exact-target Release eligibility stage
  runs after Snapshot compilation and before Product/Execution lookup,
  concurrency, or Attempt creation. Its `git-target` static-reference source
  kind enumerates and reads exact blobs from the explicit full target commit SHA
  and emits schema, result, source kind, target, policy ID/digest, sorted exact
  implementation identities actually loaded, canonical error kind when result
  is error, and sorted findings. Only `git-target` can satisfy this gate.
  The `index` source kind enumerates and reads stage-0 Git index entries for
  staged or pre-commit candidate feedback. The `worktree` source kind
  enumerates tracked plus eligible untracked paths and reads filesystem bytes
  for manual developer feedback. Neither feedback source kind can satisfy Live
  Eligibility.
- The static-reference catalog covers only first-slice paths with a bound exact
  Ecosystem Authority Graph. A graph may compose non-competing nodes across
  distinct semantic layers and emits stable normalized facts; repository policy
  projects those facts through the per-surface matrix without reproducing
  foreign grammar. The matrix rejects assigned coordinate forms and local
  dependency paths resolving to the producer root, while allowing workflows to
  name that root for legitimate builds and allowing only the producer
  `package.json` top-level `name`. Findings are prohibited references, not
  proven consumers; counts are diagnostics only.
- Eligibility independently reads protected Governance from repository
  `hcoona/three`, ref `refs/heads/main`, and path
  `.github/workflow-delivery/governance/hcoona-release-smoke-npm.json`. The
  attestation identifies sole accepted writer/publisher `hcoona`, expires
  within 90 days, and carries `live_enabled`. The eligibility record binds
  repository/ref/path and blob/content identity or explicit generation, not the
  complete resolved `main` commit. Every later protected-path touch invalidates
  the Attempt, including change-then-revert. Approval and publisher checks
  repeat this freshness decision. A false flag blocks fresh admission and the
  publisher's final check but cannot revoke a publisher already past it.
- Attempt planning uses that same request-local Snapshot to compile complete
  channel policy and validate policy-selected variants and obligations,
  compatibility obligations, and required native projection selection. It
  selects and freezes the native projections from the Snapshot rather than
  deriving or recomputing them. The Qualification Snapshot freezes complete
  destination projections and coordinates, Adapter and version bindings,
  logical operations, potential action schema, publication policy, and
  deterministic complete mutable-resource-key derivation and enforceability
  basis. It does not freeze an actual mutation action before build,
  qualification, and observation. The admitted request does not recompute the
  Repository Model.
- First-slice npm build output contains canonical
  `workflow-delivery/provenance.json` inside the tarball. The witness binds the
  immutable target, Release Unit, canonical/native NBGV facts, Build Definition,
  catalog/control digests, purpose, and schema, but no run or Attempt identity.
  Build, npm contents, and install/import qualification validate it. Remote
  observation downloads and hashes the tarball and extracts the witness;
  coordinate, ownership, witness, bytes, and the target-specific
  `buddy-sha-<40-lowercase-target-sha>` mapping to the frozen native version
  must all match. The tag is routing, not provenance. A sidecar alone is
  insufficient, and a different target is conflict even when version or tag
  claims otherwise.
- The Qualification Snapshot binds the request-local Repository Model Snapshot
  digest and freezes what must be built and qualified plus the deterministic
  pre-observation publication basis.
- The Publication Snapshot references the Qualification Snapshot and adds the
  exact artifact bytes and provenance, snapshot-bound desired state,
  observations, the exact zero-or-one Publication Action and inputs, its
  complete Adapter-declared mutable-resource keys, the Qualification Decision,
  and the Publication Result/Receipt contract.

The Publication Snapshot cannot alter fields frozen by the Qualification
Snapshot. Canonical Snapshot JSON travels as its own immutable artifact. A
deterministic reviewer summary travels in a separate immutable reviewer
artifact whose transport is bound to the exact Snapshot and summary payloads.
The standalone Snapshot artifact is the durable lifecycle boundary. The
Approval Bundle for an action-bearing Snapshot binds both Snapshot and reviewer
artifact. The Approval job validates the complete closure and emits the
Publication Authorization; mismatch fails closed. A zero-action Snapshot
bypasses approval and may produce `exact-satisfied` success.

For the first slice, live Buddy and Official simulation each qualify the built
npm tarball through distinct artifact-content and install/import obligations.
One physical tarball-dependent job may batch them, but it emits two Evidence
records and qualification requires both.

This structure preserves one Release Attempt identity while preventing
post-qualification mutation from changing what was qualified.

Native GitHub run history remains available for operator diagnostics only. It
is not exhaustively discovered, admitted, or aggregated into Release authority.
Fresh destination observation determines absent, exact, unknown, or conflict.

### Build Alignment

CI and Release use the same Build Definitions and Build Adapters.

CI builds every publishable variant of an affected Release Unit. Release
rebuilds every publishable variant of the Release Unit for its final target
commit and reruns all required Release quality obligations. Artifact variants
belong to the Release Unit rather than to Buddy or Official. After Attempt
admission, channel policy selects complete destination projections without
altering the NBGV product version. For Buddy, the frozen native NBGV version and
complete deterministic projection set are Snapshot bindings derived during
Attempt planning from same-target control definitions, not Product or Execution
Identity fields.

Pull-request artifacts and CI Evidence are never used by Release.

The first-slice npm Release Unit is required to produce bit-for-bit identical
bytes for the same target, frozen inputs, Build Definition, and toolchain. The
delivery system records and compares digests but does not certify
reproducibility through duplicate builds. Nondeterministic Release Units require
a future sealed-artifact publication-resume design and are unsupported by this
slice.

### Remote-State Observation

Every Release Attempt observes all destinations before requesting publication
Capability.

- Each logical projection is classified atomically against snapshot-bound
  desired projection state, not Product or Execution Identity.
- Desired state derives from the Qualification Snapshot and admitted qualified
  artifacts and includes exact destination coordinate, expected ownership,
  target binding, and artifact bytes or digest.
- The Observation Record binds the Release Attempt, logical projection,
  immutable desired-state basis, and canonical remote response and observed
  facts, including observed artifact digests. It cannot bind a future
  Publication Snapshot; that later Snapshot seals admitted Observation Records
  with resulting desired state and materialized actions.
- Absent state may publish.
- Exact satisfied state skips the side effect.
- Partial, unknown, conflicting, or unprovable projection state fails closed.

An absent registry coordinate is not a reservation gap. With or without
retained operational lineage, it is a legitimate initial-publication state.
After qualification and observation, the Destination Adapter uses atomic
non-overwriting create semantics when an action remains. Pre-observed exact
state has no action and may finalize as `exact-satisfied` success without
Environment approval or publication lineage.
At mutation linearization, absent state creates; an atomic create-or-exact
operation may accept a concurrently created exact state without mutation;
differing state fails without mutation. Release never implements this as
read-then-upsert, overwrite, or delete-and-recreate. A pure create-only
destination may conflict and rely on a new dispatch. Durable creation establishes
observable state; an Attempt that fails before mutation burns nothing.

Reconciliation is exceptional handling for state that cannot safely proceed.
Build and qualification receive no destination credential or publication
capability. Destination Observation may use public APIs or the minimum read-only
destination authority required for exact-state readback, but receives no
destination write authority, PAT, `id-token: write`, Approval Environment, or
publication capability. The initial architecture assumes Delivery Governance
is the only normal writer and accepts an out-of-band mutation after observation
as a residual risk only when the destination mutation primitive still enforces
atomic non-overwriting behavior at linearization. A second observation,
repository concurrency, or post-action readback is not a substitute. Standard
`npm publish --tag` can move a conflicting tag and is therefore not an admitted
primitive for the complete first-slice projection. Live registry support
depends on a documented lower-layer contract for atomic non-overwriting
creation and durable exact-state observation. An incapable destination is
unsupported, not emulated through an application-level lock or permanent
index.

### Retry

Retry is a new manual dispatch. Both GitHub rerun commands are unsupported for
normal Live.

Every retry receives a new `workflow_run_id` and reruns request-local
Repository Model compilation and live eligibility. A retry rejected or
coalesced before admission creates no Attempt. An admitted retry creates a new
Attempt for the same deterministic Execution Identity and replans, rebuilds,
requalifies, reobserves, and reapproves when an action remains. It reuses no
older Attempt's Repository Model, Qualification Snapshot, artifacts,
Publication Snapshot, Environment approval, or Publication Authorization.
Already exact destination state becomes a new `exact-satisfied` success without
publication.

The first-slice npm unit must reproduce identical bytes for the same target,
frozen inputs, and toolchain. Existing destination bytes that differ fail closed
into reconciliation and separately authorized remediation. Nondeterministic
Release Units are unsupported until a future sealed-artifact
publication-resume design exists.

Separate manual requests retain separate Release Intents and Attempts. A
pending request coalesced before admission creates no Attempt. Native GitHub
history is diagnostic only; retry does not depend on reconstructing prior
Attempt lineage or aggregate Execution state.

### Publication Preparation Interruption

Successful Qualification does not imply that a Publication Snapshot exists.
Remote-state Observation and the later sealing of exact artifacts,
observations, and materialized actions are a separate staged planning boundary.
Failure or cancellation before that second Snapshot is durably persisted stops
approval and publication.

When the Finalizer runs, it may classify this boundary as
`publication-preparation` only when direct platform execution facts and the
record set consistently prove all of the following:

- the exact Qualification Decision succeeded;
- no durable Publication Snapshot exists;
- no Publication Authorization or mutation marker exists; and
- the publisher did not start.

The resulting Attempt has replayable `failed-before-publication` disposition,
is not possibly mutated, and requires a new manual dispatch. The Finalizer does
not invent a Publication Snapshot or copy platform results into domain
Evidence. A missing Snapshot alone is not proof: contradictory job success,
transport, or downstream lineage remains a contract failure.

The durable Publication Snapshot artifact is the lifecycle boundary. If it was
persisted before a later reviewer-payload or approval-input failure, approval
and publication still stop, but finalization retains the Snapshot and uses the
existing Snapshot-bound outcome model. Only the read-only Release Finalizer may continue after either failure to retain
facts and, when possible, produce an authoritative Attempt Outcome. Platform
cancellation may prevent even that Finalizer from running or persisting its
output. Finalization is best effort, and Workflow Delivery adds no watchdog to
bypass this platform limitation.

### Partial Publication and Remediation

The first slice has exactly zero or one Publication Action. It has no capability
group or group-level result.

With zero actions, the manual Release Intent authorizes read-only
reconciliation. Exact state may finalize as `success` with
`exact-satisfied` disposition without Environment approval, Publication
Authorization, publisher, destination write or publication credential,
Publication Capability, marker, Publication Result, or Receipt. Observation or
an explicit no-op reobservation may use only the minimum read-only destination
authority. Immediately before success, the zero-action path repeats protected
Governance ancestry, path-touch, blob/content, expiry, and `live_enabled`
validation and binds that fresh proof into finalization.

With one action, the Approval job is the complete post-approval admission
boundary. Its durable Publication Authorization closes current-Attempt
Governance, action, artifact, and resource bindings. The publisher has an
ordinary success dependency on that job, revalidates the closure, and repeats
the fresh Governance check immediately before mutation.

The publisher durably persists the mutation-may-have-started marker before the
first mutating destination operation. It then performs the action and persists
one Publication Result. A successful `published` Result embeds exactly one
Receipt.

The first-slice npm mutation sets highest-precedence `fetch-retries=0`, so one
CLI invocation cannot automatically resend its registry `PUT`. Read-only
readback may retry within bounded policy. A controlled failed Result after the
marker may omit the Receipt and must preserve mutation classification and
diagnostics. Marker without a durable Result is unknown and possibly mutated;
the next dispatch begins with fresh destination observation.

Environment rejection reason need not be reconstructed. If current-DAG facts
prove the publisher never started and the Finalizer runs, the Attempt may
become replayable `failed-before-publication`. GitHub cancellation or finalizer
transport failure may leave no durable Attempt Outcome. If publisher start or
mutation cannot be excluded, the state is incomplete or unknown and the next
dispatch reobserves.

Future multi-destination publication must use append-only Saga semantics, but it
is not a first-slice capability-group contract. Break-Glass Remediation
remains separately approved, uses expected-state checks and scoped capability,
and records append-only before-and-after state without rewriting the original
Attempt.

## Concurrency Design

- CI cancels superseded candidate runs.
- Release Execution identity and duplicate request coalescing use the complete
  channel-specific Release Execution Identity. Official uses Product Identity
  plus immutable target. Buddy uses channel, Release Unit, and immutable target.
- Every live Destination Adapter declares complete deterministic
  mutable-resource keys for each mutating action. Publication Snapshots and
  action manifests bind them, and overlapping actions serialize on them.
- Package mutation keys include the exact External Package Coordinate:
  channel, destination, package, and version, plus any additional
  Adapter-required keys. First-slice npm publication is one compound
  version-and-tag action keyed by both that coordinate and
  destination/package/`buddy-sha-<40-lowercase-target-sha>`. The tag is routing,
  not provenance; no separate normal tag mutation is allowed. Non-package
  destinations define their exact keys through Adapter contracts.
- GitHub provides equality concurrency groups rather than arbitrary
  set-overlap locks. The first-slice GitHub Packages Adapter therefore maps
  every mutation for one physical destination and normalized npm package name
  to one conservative shared group, including mutations with different
  versions or target-derived tags. This intentionally over-serializes while
  preserving the complete coordinate-plus-tag key set in the Publication
  Snapshot, action, Receipt, and validation. Future Adapters retain abstract
  complete-set overlap semantics and must block if no safe platform projection
  can enforce them.
- Missing, unknown, incomplete, or conflicting required keys block live
  publication.
- In-progress Release executions are never auto-canceled.
- `queue: single` retains only the newest pending duplicate request for the same
  complete Release Execution Identity.
- Every request that survives coalescing and is admitted creates a distinct
  Attempt identified by the Execution and unique `workflow_run_id`. A pending
  dispatch replaced before execution creates no Attempt.
- Each candidate compiles its request-local Repository Model before entering
  execution concurrency. The surviving concurrency-scoped caller invokes one
  same-revision reusable live-Attempt workflow and holds the Execution identity
  slot through terminal workflow state, including the read-only Finalizer when
  it runs.
- Live actions may run concurrently only when their complete mutable-resource
  key sets do not overlap.
- Remediation reuses exactly the complete frozen Adapter-declared resource-key
  set from the original action and never derives it from Product or Execution
  Identity.

Release Execution and coalescing keys derive from Release Execution Identity.
Live-action resource keys derive independently from complete Adapter-declared
mutable-resource keys. A platform group may be a conservative Adapter-declared
projection that preserves all overlap serialization while over-serializing.
GitHub concurrency is a best-effort repository execution mechanism, not a
distributed correctness lock, authorization source, or protection against
external writers.

## Evidence, Decisions, and Explanation

Evidence Admission verifies exact ownership and integrity without rerunning the
quality command.

Final Decisions are append-only. GitHub checks and summaries are projections of
the latest authoritative Decision, not the audit record itself.

CI explanations connect paths, Project Nodes, dependency relationships, Release
Units, obligations, variants, Evidence, outcomes, and verdicts.

Release explanations connect target, version, channel, artifacts,
destinations, observations, actions, Receipts, authority, authorization, and
allowed operator actions.

The same structured model drives machine-readable records and human
projections. Diagnostics explain a verdict but never determine it.

## Platform-Aware Record Retention

Caches are non-authoritative performance mechanisms.

GitHub Actions artifacts and logs are operational records subject to the
platform retention window. This public repository supports at most 90 days. The
first slice uses 45-day Release control and artifact retention to exceed the
platform Environment gate-expiry window, currently up to 30 days, and blocks
activation if repository policy cannot supply that margin. Fresh authenticated
preactivation and post-merge readback must prove that the effective repository
policy permits at least 45 days. The approval wait does not preserve Governance
eligibility: Approval and publisher checks require the protected attestation to
remain unexpired and enabled, retain its admitted repository/ref/path and
blob/content or generation binding, and have no protected-path touch since
eligibility. Unrelated `main` commits are allowed.

Artifact names are non-authoritative indexes and use collision-safe,
overwrite-disabled transport within one workflow run. Producers retain artifact
ID, digest, and URL. Current-Attempt consumers use artifact IDs only and verify
record kind, producer, `workflow_run_id`, target, purpose, payload identity, and
digest. For normal Live, `github.run_attempt` is not an artifact or record
binding because every authoritative job independently requires attempt 1.
Simulation and other contexts retain their own run-attempt contracts. Name
fallback, latest selection, and history-derived authority are rejected.

Longer-lived release identity and provenance rely on Git tags, registry
records, GitHub Releases when selected, and GitHub Artifact Attestations with
public Sigstore transparency-log publication.

The first architecture does not add a Durable Release Ledger, a global Official
Product Identity-to-target mapping, or a GitHub Release audit anchor for every
Release Unit. Different target-specific Executions may share Product Identity;
destination serialization and durable observation determine absent, exact, or
conflict. State that cannot be proved after operational records expire fails
closed. An absent destination is still a valid initial-publication observation
and does not require exhaustive retained Attempt history, a tag witness, or a
binding index.

## Validation Strategy

Business-flow validation is scenario-oriented and asserts semantic outcomes:
admission, qualification, approval, exact-satisfied reconciliation,
publication, mutation uncertainty, retry, and fail-closed recovery. Strict unit
and contract tests remain appropriate for schemas, canonicalization, identity,
resource concurrency, mutation and recovery ordering, and fail-closed
validation.

Tests do not freeze the exact job DAG, non-authoritative shell choreography,
fixed inventory counts, parser branches, or step order beyond authority-critical
ordering such as Publication Authorization before publisher start and durable
marker before mutation.

## Requirement Coverage

| Requirement Group | Owning Design Elements                                                                                                                                                                                          |
| ----------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `WD-SYS-*`        | Peer bounded contexts, aggregate ownership, Shared Foundation, Delivery Governance                                                                                                                              |
| `WD-CI-*`         | CI Qualification flow, Planner, executors, Evidence Admission, Final Decision                                                                                                                                   |
| `WD-REL-*`        | Release Attempt, Plan lineage, independent build and qualification, Side-Effect Zone                                                                                                                            |
| `WD-CHN-*`        | Buddy and Official channel policy over Release Delivery                                                                                                                                                         |
| `WD-AUTH-*`       | Same-revision context decision code, protected review, Delivery Governance                                                                                                                                      |
| `WD-SEC-*`        | Decision, Build and Qualification, and Side-Effect runtime zones                                                                                                                                                |
| `WD-EVD-*`        | Evidence Admission, append-only Decisions, structured explanation projections                                                                                                                                   |
| `WD-OPS-*`        | Remote-State Observation, new-dispatch retry, reconciliation, remediation                                                                                                                                       |
| `WD-CON-*`        | Domain-derived GitHub execution serialization and destination resource serialization                                                                                                                            |
| `WD-RET-*`        | Platform-aware records, durable destination identities, fail-closed expiration                                                                                                                                  |
| `WD-SLICE-*`      | Same-revision Buddy control, accepted writer TCB and repository-principal blast radius, static-reference policy, one Approval Environment, Publication Authorization, publisher ordering, and one-PR activation |
| `WD-NFR-*`        | Context separation, adapter extension model, explanation contract, CI objective                                                                                                                                 |

## Middle-Layer Design Decomposition

The architecture is decomposed into these MLDs:

1. **Repository Model and Release Units:** Project Node discovery, dependency
   and path-impact facts, Release Unit authoring, variants, and Build
   Definitions.
2. **Governance Integration:** same-revision control, channel-specific review
   policy, the explicit first-slice Buddy trust exception, platform-native
   authority, and authorization boundaries.
3. **CI Qualification:** candidate identity, affected-scope planning,
   project-selected quality policy, opaque source-tree conformance,
   model-driven execution, Evidence, Decision, advisory reporting, and GitHub
   projection contracts.
4. **Release Delivery:** manual same-revision Intent, channel identity, Plan
   lineage, complete variant build and qualification, projection observation,
   Approval Bundle, Publication Authorization, publication, Result/Receipt,
   new-dispatch retry, and remediation contracts.
5. **Shared Foundation:** record primitives, Provider and Adapter interfaces,
   Repository Model compilation, static Definition catalogs, artifact identity,
   provenance, execution classes, and generic client primitives.

CI and Release remain together in this HLD because their separation, shared
foundation, and governance relationship are top-level architectural decisions.
They separate into bounded-context documents at the MLD stage.

## Deferred Lower-Layer Questions

The HLD intentionally leaves these questions to later design:

- exact strict record schemas and transport;
- adapter package decomposition;
- exact GitHub workflow and job topology;
- artifact and Evidence physical naming;
- batching and matrix partitioning;
- destination-specific observation and digest APIs;
- user-interface rendering and operator commands; and
- destination-specific lower-layer implementation choices not fixed by this
  HLD.
