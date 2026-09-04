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
- for the one-action path, persists a pre-mutation marker and, for each
  controlled post-marker terminal state, forms one logical Publication Result
  and initiates its persistence; a successful `published` Result carries
  authoritative exact post-action readback, while uncontrolled termination or Result
  transport failure may leave no Result; and
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
  such as `npmPackageVersion`, unchanged. Publication Authorization reaches the
  immutable Publication Snapshot through its admitted Approval Bundle.
- Buddy Release Execution Identity is channel, Release Unit, and immutable
  target.
- Buddy and Official may use the same product-version string when their
  complete destination coordinates are isolated.
- Buddy artifacts, Evidence, and Publication Results are never promoted to
  Official.
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
Publication Results, and Release outcome.

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

Destination projection classification, action planning, successful-result
evidence semantics, and remediation belong to Release Delivery. Shared
Foundation may provide generic GitHub or registry client primitives, but it
does not own a Destination Adapter business contract.

Providers provide normalized facts. Adapters execute closed mechanical
operations. Neither decides business scope, downgrades obligations, authorizes
publication, or reinterprets verdicts.

## Governance and Trust

### Context-Owned Planning and Finalization

CI Qualification owns CI scope planning, obligation disposition, Evidence
Admission, and CI Final Decision.

Release Delivery owns Release planning, qualification finalization,
Publication Snapshot finalization, Publication Result admission, and Release
outcome.

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
Approval Bundle before waiting on the Environment. The bundle directly binds
the Publication Snapshot and immutable reviewer-summary artifact by canonical
payload digest and Artifact Reference. The Snapshot remains sole owner of the
target, Qualification Decision, artifact, action, and mutable-resource closure.

The Approval job:

- references the literal Approval Environment;
- validates the resolved exact marker value as its first authority-critical
  executable check;
- has no publication capability;
- freshly validates protected Governance, including path-touch anti-rollback;
- validates the action's profile digest against current Governance and admits
  the immutable action as an instantiation of that profile;
- strictly admits the Approval Bundle and transitively resolves its complete
  Snapshot, reviewer, artifact, and action/resource closure; and
- after approval, durably emits one complete Publication Authorization.

The job cannot determine the marker's source scope or prove the absence of
same-name repository or organization variables. That absence is authenticated
native Governance/provisioning/activation readback and attestation evidence.
Only under that externally verified precondition does marker validation make
accidental implicit creation of the named Environment fail closed. The job uses
`contents: read` to reread protected Governance; the least-privilege claim
is that it has no publication capability, not that it is fully credential-free.

That Publication Authorization is the post-approval publication-admission
boundary. It directly binds the admitted Approval Bundle plus
approval-boundary and fresh-Governance evidence, and reaches the complete
Snapshot, reviewer, action, artifact, and resource closure transitively. It
does not copy that ancestor state or bind `github.run_attempt`. There is no
approval-finalizer, Capability Admission Decision, capability group, group
manifest, or group result bundle.

The publisher has an ordinary success dependency on the Approval job. It
strictly validates the Publication Authorization and exact closure, performs a
final fresh Governance and supported package-control check, validates the
actual pinned toolchain and effective command configuration against the
admitted operation profile, and is the only step-running job with effective
`packages: write`. A `uses`-only caller may declare the same permission solely
as a non-elevating reusable-workflow ceiling and has no steps or direct token
use.

Immediately before its first mutating destination operation, the publisher
durably persists a mutation-may-have-started marker. Marker failure blocks the
mutation. For each controlled post-marker terminal state, it forms one logical
Publication Result and initiates one logical persistence operation. Transport
may retry only the same immutable payload without creating another logical
Result. The current DAG exposes one nullable scalar immutable publication
terminal reference to the Finalizer. It points to the Result when one was
durably persisted, otherwise to the marker when one was durably persisted,
otherwise it is null; Result takes precedence and no wrapper record is added.
Only null or one well-formed marker or Result Artifact Reference is admitted;
malformed, non-scalar, misbound, or other-kind input fails closed. A successful
`published` Result carries the required exact post-action readback. A
controlled failed Result may retain available post-action remote evidence
without becoming successful. A terminal reference to a marker without a
durable Result means unknown, possibly mutated state and requires fresh
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
`workflow-delivery/v3/normal-live-governance-attestation-v2`. V2 replaces the
disabled v1 contract because the native destination-acceptance attestation has
a different closed field set. Selected-revision control must require v2
exactly; v1 is not an admission alias. This intentionally makes superseded
parsers fail before Release Execution lookup, Attempt creation, or any
Environment job without substituting protected-main control code.

The v2 `activation` object is a closed discriminated union. `blocked` carries
only `state: "blocked"`, requires `live_enabled: false`, and carries no native
evidence. `ready` carries complete pass-only Approval
Environment, artifact-retention, and destination-primitive attestations.
`live_enabled: true` requires `ready`; `ready` plus `live_enabled: false`
remains valid for fast revocation without rewriting evidence. Runtime
implementation migrates to blocked v2 before native acceptance.

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
establish the bounded observable mutation footprint of the pinned standard
`npm publish` operation against a pre-existing disposable package. Each
required invariant relies on either a cited documented lower-layer contract or
complete observation through a supported authoritative interface; an
unsupported and unobservable required invariant blocks activation. Protected
Governance binds the exact destination-operation-profile digest, native-
acceptance-suite version, contract/API revisions, capture time, and evidence
digest identifying the successful acceptance generation. The versioned suite owns a closed comparison shape
that includes package identity, complete active version-name inventory,
complete tag mapping, scenario-version bytes and witness, and supported
package-control facts while explicitly excluding enumerated server-generated
volatile metadata. The deleted/restorable scenario additionally uses
acceptance-only package-admin evidence for the complete disposable-package
deleted-version inventory, targeted tombstone identity and continued
restorability, and exact restored bytes and witness. Exact or deleted versions
are not replaced, exact bytes and witness can be read back, only the
scenario-declared version and target-derived tag change in projected state,
unrelated projected state remains unchanged, and conflict, non-success, and
ambiguous mutation responses are not upgraded to same-Attempt success. Initial
activation of a newly admitted operation profile binds acceptance captured
after implementation of that exact profile. Later Governance may reuse the
generation only while all bound inputs remain identical and action-bearing
admission occurs within 90 days of capture; a binding change or age expiry
requires recapture. Expiry blocks action-bearing publication, not zero-action
exact-satisfied finalization. The first slice remains disabled until initial
acceptance passes.

After the Activation PR merges, the first proving run is dispatched from
then-current protected `main` through an explicitly supported REST API version
whose success response contains `workflow_run_id`. The operator validates that
response schema and reads back the returned workflow/run identity, actor,
`workflow_dispatch` event, exact actual head SHA, `refs/heads/main`, and
`run_attempt == 1`. A lost response or ambiguous correlation triggers
read-only operator investigation and native run lookup, never blind
redispatch. This first-slice handling creates no formal Reconciliation Record
and does not invoke a standalone Release Reconciliation workflow. Later normal
Buddy runs may select arbitrary same-repository refs whose selected-revision
control admits the active Governance schema. Before activation, retained
dispatchable refs are checked or fixture-proven to implement the
one-Environment contract or reject that schema before any Environment job or
deployment.

The proving run remains state-driven. Exact destination state takes the
zero-action `exact-satisfied` path; active-absent state may take the one-action
`published` path only through the admitted destination primitive and its
Governance-bound tombstone acceptance. Activation does not manufacture a
mutation merely to exercise the publisher.

Every authoritative normal-Live job independently requires
`github.run_attempt == 1`, including eligibility and planning, the Approval
job, exact-satisfied no-op finalization, publisher, and Finalizer. This protects
against partial reruns. The value is a platform guard and diagnostic, not
domain identity or a record, artifact, or Publication Authorization binding.

An Attempt Outcome has one of two successful dispositions:

- `exact-satisfied`: normal read-only Observation found exact destination
  state, the publisher was skipped, no Approval Bundle or other action-bearing
  lineage exists, and an exact-satisfied finalization proof binds fresh
  Governance and package-control checks; no Environment approval, Publication
  Authorization, destination write or publication credential, Publication
  Capability, marker, or Publication Result exists; or
- `published`: the action completed successfully under the complete Publication
  Authorization and produced a Publication Result with definitive command
  success and authoritative exact post-action readback.

Attempt Outcome dispositions are exactly `exact-satisfied`, `published`,
`failed-before-publication`, `publication-failed`, and `unknown`. Platform
`success`, `failure`, `cancelled`, and `skipped` remain execution facts rather
than business dispositions.

Only these terminal combinations are valid:

| Durable domain and current-DAG facts                                                                                                                                                                                                    | Disposition                 | Possibly mutated |
| --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------- | ---------------: |
| Zero-action Snapshot, valid exact-satisfied finalization proof, publisher `skipped`, null publication terminal reference, and no Approval Bundle, Authorization, or other action-bearing lineage                                        | `exact-satisfied`           |            false |
| Valid `published` Result; publisher `success`, `failure`, or `cancelled`                                                                                                                                                                | `published`                 |            false |
| Exactly one admitted pre-marker predecessor; no valid zero-action Snapshot; null publication terminal reference; publisher `skipped`, or publisher `failure`/`cancelled` with exact platform-derived publication-step outcome `skipped` | `failed-before-publication` |            false |
| Valid failed Result proving `not-mutated`                                                                                                                                                                                               | `publication-failed`        |            false |
| Valid failed Result classified `possibly-mutated` or `mutated`                                                                                                                                                                          | `publication-failed`        |             true |
| Terminal reference resolves to marker, or is null while mutation cannot be excluded                                                                                                                                                     | `unknown`                   |             true |
| Valid zero-action Snapshot without a valid exact-satisfied finalization proof, publisher `skipped`, null publication terminal reference, and no Approval Bundle, Authorization, or other action-bearing lineage                         | `unknown`                   |            false |

A non-null publication terminal reference with publisher `skipped`, invalid or conflicting
record lineage, a zero-action Snapshot with publisher `success`, `failure`, or
`cancelled`, or any tuple not listed above is contradictory and produces no
authoritative Outcome. The generic missing-Result `unknown` row excludes a
valid zero-action Snapshot. Publisher `success` without a valid Result follows
the applicable action-bearing `unknown` row; a green job never supplies
publication evidence.
A failed or incomplete Qualification Decision remains the terminal
authoritative record and forms no Attempt Outcome.

`disposition` and `possibly_mutated` are the complete authoritative
classification. Human-readable result summaries and operator guidance are
derived outside the canonical Outcome.

Each Outcome has exactly one tagged direct predecessor:

| Terminal case                                            | Direct predecessor                                                                                                                                                                |
| -------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `exact-satisfied`                                        | exact-satisfied finalization proof, which directly binds the zero-action Snapshot plus fresh Governance and package-control proofs and fresh authoritative exact-version readback |
| `published` or `publication-failed`                      | Publication Result                                                                                                                                                                |
| `unknown` with a marker and no Result                    | mutation marker                                                                                                                                                                   |
| `unknown` for a zero-action Snapshot missing fresh proof | zero-action Publication Snapshot                                                                                                                                                  |
| pre-marker action path with Authorization                | Publication Authorization                                                                                                                                                         |
| pre-Authorization path with a persisted Approval Bundle  | Approval Bundle                                                                                                                                                                   |
| pre-bundle path with an action-bearing Snapshot          | action-bearing Publication Snapshot                                                                                                                                               |
| pre-Snapshot path with a sole blocking Observation       | blocking Observation Record                                                                                                                                                       |
| interruption before any Observation                      | exact successful Qualification Decision                                                                                                                                           |

The selected Observation directly binds the exact successful Qualification
Decision. Multiple candidates at the selected predecessor tier are
contradictory. The Outcome does not copy ancestors reachable through that
predecessor.

A blocking destination Observation does not imply Attempt mutation uncertainty.
After exact successful Qualification, a `partial`, `conflicting`, `unknown`, or
`unprovable` Observation with publisher `skipped`, no valid zero-action
Snapshot, and a null publication terminal reference takes
`failed-before-publication`. Separate
Release Reconciliation or remediation may be required before a productive
later dispatch; operator guidance must preserve that prerequisite and never
resumes the old Attempt.

Incomplete, unknown, conflicting, partial, or possibly mutated state is never a
no-op success. If current-DAG facts prove that mutation-capable execution never
started—because the publisher was skipped or its isolated publication step has
the exact platform-derived `skipped` outcome—and the read-only Finalizer runs,
it may retain a `failed-before-publication` outcome without reconstructing an
Environment rejection reason. Missing or script-produced execution facts do
not suffice. GitHub cancellation or finalizer transport failure may leave no
durable Attempt Outcome. Retry is always a new manual dispatch that rebuilds,
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
                 Publication Result, with exact post-action evidence on published
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
            Publication Authorization, Capability, Publication Result, or mutation
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
  coordinate, witness, and bytes must all match. Ownership, repository
  association, visibility, and access are admitted separately through the
  embedded Package-Control Proof. A missing, differing, or unprovable
  package-control fact blocks admission and finalization without creating a
  publication action. The separately
  observed target-specific `buddy-sha-<40-lowercase-target-sha>` tag is
  non-authoritative routing metadata. A sidecar alone is insufficient, and a
  different target is conflict even when version or tag claims otherwise.
- The Qualification Snapshot binds the request-local Repository Model Snapshot
  digest and freezes what must be built and qualified plus the deterministic
  pre-observation publication basis.
- The Publication Snapshot references the Qualification Snapshot and adds the
  exact artifact bytes and provenance, snapshot-bound desired state,
  observations, the exact zero-or-one Publication Action and inputs, its
  complete Adapter-declared mutable-resource keys, the Qualification Decision,
  and the Publication Result contract.

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
  artifacts and includes exact destination coordinate, target binding, and
  artifact bytes or digest. For first-slice npm, exactness is the normalized
  package name, frozen version, downloaded tarball bytes and digests, and
  embedded witness in the active registry projection. Runtime Observation does
  not enumerate deleted/restorable versions.
- Each first-slice Observation separately embeds a package-control proof for
  the destination/normalized-package subject, supported authoritative
  endpoints, owner, repository association, visibility, exposed access facts,
  observation time, and response digests. The proof is never standalone; its
  parent binds the applicable protected-Governance identity or proof, derives
  expected package-control values from that parent, and jointly validates the
  observed facts. The proof copies neither expected values nor the Governance
  content digest. Unexposed access-grant completeness remains a
  Governance-attested limitation.
- The Observation Record binds the Release Attempt, exact successful
  Qualification Decision, logical projection, immutable desired-state basis,
  and canonical remote response and observed facts, including observed artifact
  digests and separately classified dist-tag diagnostics. It cannot bind a
  future Publication Snapshot; that later Snapshot seals admitted Observation
  Records with resulting desired state and materialized actions.
- State absent from the active projection may form one action under the current
  Governance-bound tombstone acceptance.
- Exact satisfied state skips the side effect.
- Partial, unknown, conflicting, or unprovable projection state fails closed.

An active-absent registry coordinate is not proof that the version was never
published, is not retained as deleted/restorable state, or will accept
creation. With or without retained operational lineage, active absence is a
legitimate action candidate only under an unexpired Governance-bound
acceptance proving that the pinned operation safely rejects a hidden tombstone.
After qualification and observation, the Destination Adapter uses
non-overwriting exact-version creation when an action remains. A pre-observed
exact active version has no action and may finalize as `exact-satisfied`
success without Environment approval or publication lineage, regardless of
dist-tag state or tag-read availability. Any duplicate, hidden-tombstone,
conflict, non-success, or ambiguous response remains failed in the current
Attempt even when post-failure readback is exact. A new dispatch may reobserve
the exact active version and take `exact-satisfied`. Differing version bytes
fail closed. Release never uses overwrite, delete-and-recreate, or compensation.

Standard npm publication necessarily assigns a tag. For the dedicated
first-slice smoke package, the target-derived tag is declared
non-authoritative routing metadata rather than part of exact destination state.
If the version is absent from the active projection, the tag must be observed
absent and current Governance must bind an unexpired acceptance generation
covering the deleted/restorable same-version case before action formation. A
present or unprovable tag blocks a known overwrite. The approved single
`npm publish --tag` invocation may nevertheless move that declared tag if
another authorized external writer races after Observation. This bounded
last-writer-wins risk is accepted under the sole-writer TCB and smoke-only
package boundary. The tag remains in the action, Authorization lineage,
mutable-resource keys, pre-action Observation, and Result post-action
diagnostics and readback, but no supported consumer uses it for identity,
provenance, exactness, installation, retry, or success.

The one action authorizes the complete pinned `npm publish` request shape, not
only its version and tag fields, and carries the canonical
destination-operation-profile digest. The package container must already have
the expected ownership, repository association, visibility, and access. The
initial Remote-State Observation must establish those package-control
preconditions; on the action path, the publisher must repeat supported native
readback immediately before the mutation marker. Complete access-grant
inventory that the platform does not expose remains a bounded protected-
Governance attestation limitation. Native acceptance records the normalized
outbound request and validates that observed publication does not alter
`latest`, unrelated versions or tags, or those package settings. A resolved
Destination Operation Profile, native-acceptance-suite, disposable-package
precondition, GitHub API version, or relied-on documented contract revision
change reopens that acceptance boundary.
Acceptance uses separately authorized package-admin credentials only for its
disposable deleted/restorable scenario: publish and verify a fresh version,
delete it, prove the tombstone projection, require sequential identical- and
differing-byte republish attempts to fail definitively with no active or
deleted semantic delta, then restore and verify the original bytes and witness.
Those credentials and deleted-state facts never enter runtime Observation.

Reconciliation is exceptional handling for state that cannot safely proceed.
Build and qualification receive no destination credential or publication
capability. Destination Observation may use public APIs or the minimum read-only
destination authority required for exact-state readback, but receives no
destination write authority, PAT, `id-token: write`, Approval Environment, or
publication capability. Repository-controlled publishers serialize by physical
destination and package. That does not constrain an external writer and is not
presented as a registry lock. Live support trusts the destination's documented
exact-version non-overwrite rule and verifies its concrete GitHub Packages
behavior before activation; it does not emulate missing tag CAS through an
application-level lock, retry, or permanent index.

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

When the Finalizer runs, it may recognize this Publication Preparation
Interruption condition only when direct platform execution facts and the record
set consistently prove all of the following:

- the exact Qualification Decision succeeded;
- no durable Publication Snapshot exists;
- no Publication Authorization or mutation marker exists; and
- the publisher did not start.

The resulting Attempt has `failed-before-publication` disposition, is not
possibly mutated, and requires a new manual dispatch. The Finalizer does not
invent a Publication Snapshot or copy platform results into domain Evidence. A
missing Snapshot alone is not proof: contradictory job success, transport, or
downstream lineage remains a contract failure.

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

With zero actions, the manual Release Intent authorizes normal read-only
Observation and no-op finalization. Exact state may finalize as `success` with
`exact-satisfied` disposition without Environment approval, Publication
Authorization, publisher, destination write or publication credential,
Publication Capability, marker, or Publication Result. Observation or
an explicit no-op reobservation may use only the minimum read-only destination
authority. Immediately before success, the zero-action path repeats protected
Governance ancestry, path-touch, blob/content, expiry, and `live_enabled`
validation, repeats supported package-control readback, repeats authoritative
exact-version readback against the Snapshot-bound bytes, digests, and embedded
witness, and binds the zero-action Snapshot and all three fresh checks into one
exact-satisfied finalization proof. Missing, differing, or unprovable fresh
version state leaves the zero-action Snapshot without that proof and therefore
cannot produce `exact-satisfied`.

With one action, the Approval job is the complete post-approval admission
boundary. Its durable Publication Authorization directly binds the Approval
Bundle plus fresh Governance and approval-boundary evidence and reaches the
destination-operation profile, action, artifact, and resource closure through
the immutable Bundle-to-Snapshot chain. The publisher has an ordinary success
dependency on that job, revalidates the chain, repeats the fresh Governance and
supported package-control checks, and compares the actual pinned command
configuration with the admitted operation profile immediately before mutation.

The publisher durably persists the mutation-may-have-started marker before the
first mutating destination operation. The marker directly binds the
Publication Authorization, final publisher-side Governance proof, and final
supported package-control proof, plus canonical evidence that the actual
toolchain and effective command configuration matched the admitted operation
profile; its producer/current-run envelope identifies the publisher, and the
Authorization remains the sole approved mutation closure. Mutation begins only
after durable marker persistence validates. The publisher then performs the
action and, for each controlled post-marker terminal state, forms one logical
`workflow-delivery/v3/publication-result` and initiates one logical persistence
operation. Transport may retry only the same immutable payload. The Finalizer
receives one nullable scalar explicitly propagated immutable publication
terminal reference. It resolves to the Result when one was durably persisted,
otherwise to the marker when one was durably persisted, otherwise null. Result
takes precedence; the transport adds no wrapper schema. Malformed, non-scalar,
misbound, or other-kind input fails admission. A referenced Result directly
binds and resolves the durable marker plus only newly observed post-action
facts. Requested values and pre-action state remain authoritative through
marker, Authorization, and Snapshot. A successful `published` Result contains
authoritative exact post-action readback.

The first-slice npm mutation sets highest-precedence `fetch-retries=0`, so one
CLI invocation cannot automatically resend its registry `PUT`. Read-only
readback may retry within bounded policy. A controlled failed Result after the
marker preserves mutation classification, diagnostics, and any available
post-action remote evidence. A pre-marker failure emits no Result.
Conflict, non-success, or ambiguous command responses never become
same-Attempt `published`, even when readback is exact. A `published` Result
requires definitive command success, `mutation-classification: mutated`, and
successful authoritative exact-version readback.
Uncontrolled termination or Result persistence may leave only the marker as the
terminal reference. Marker without a durable Result is unknown and possibly
mutated; the next dispatch begins with fresh destination observation.

The Finalizer applies the terminal-state matrix above. It does not reconstruct
an Environment rejection reason, invent missing publication records, or infer
publication from platform success. GitHub cancellation or finalizer transport
failure may leave no durable Attempt Outcome.

Multi-action or multi-destination publication is outside the first slice and
requires a concrete scenario and a new reviewed design. This design does not
preselect a generic transaction, compensation, rollback, or Saga protocol.
Break-Glass Remediation remains separately approved, uses expected-state checks
and scoped capability, and records append-only before-and-after state without
rewriting the original Attempt.

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
  Adapter-required keys. First-slice npm publication is one standard publish
  action keyed by both that coordinate and
  destination/package/`buddy-sha-<40-lowercase-target-sha>`. The tag is routing,
  not authoritative state or provenance; no separate normal tag mutation is
  allowed. The Snapshot and action own this key set; Authorization and
  publisher admission validate it transitively rather than copying it.
  Non-package destinations define their exact keys through Adapter contracts.
- GitHub provides equality concurrency groups rather than arbitrary
  set-overlap locks. The first-slice GitHub Packages Adapter therefore maps
  every mutation for one physical destination and normalized npm package name
  to one conservative shared group, including mutations with different
  versions or target-derived tags. This intentionally over-serializes while
  preserving the complete coordinate-plus-tag key set in the Publication
  Snapshot and action. Authorization, publisher admission, and Publication
  Results validate the set transitively through their immutable predecessor
  chain rather than copying it. Future Adapters retain abstract complete-set
  overlap semantics and must block if no safe platform projection can enforce
  them.
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
destinations, observations, actions, Publication Results, authority,
authorization, and allowed operator actions.

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
admission, qualification, approval, exact-satisfied no-op finalization,
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
   Approval Bundle, Publication Authorization, publication, Publication
   Result, new-dispatch retry, and remediation contracts.
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
