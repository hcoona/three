# Workflow Delivery v3 Governance Integration MLD

## Status

Architecture version: **v3**.

Review state: **Confirmed on 2026-07-30**.

This middle-level design defines how CI Qualification and Release Delivery rely
on GitHub and destination platforms for review, protected execution, identity,
and publication authority.

`Workflow Delivery` is an architecture-domain umbrella, not a runtime actor.
Runtime responsibilities in this document belong explicitly to CI
Qualification, Release Delivery, Shared Foundation, or Delivery Governance.

## Scope

This MLD owns:

- the authority boundary between repository code and external platforms;
- same-revision control-code eligibility;
- workflow permission and credential placement;
- Buddy and Official capability isolation;
- platform-denial behavior; and
- v3 governance rollout acceptance.

This MLD does not own:

- CI scope or obligation policy;
- Release destination and action planning;
- Repository Model discovery;
- Registry-specific publication algorithms;
- a repository mirror of platform governance state; or
- a permanent governance service or audit ledger.

## Governing Principle

GitHub and destination platforms own the guarantees they formally expose.

CI and Release must use those abstractions correctly, but they must not
reimplement weaker copies of:

- CODEOWNERS and required review;
- Ruleset and protected-ref enforcement;
- GitHub Environment approval;
- workflow permission enforcement;
- GitHub OIDC identity issuance; or
- destination trusted-publisher and authorization policy.

Application code validates identities and data bindings that it creates. It
does not query platform state merely to reproduce a decision already enforced
by the platform.

## Authority Topology

```text
Repository governance
  CODEOWNERS / required review / Rulesets / protected refs
             |
             +-- CI candidate or Official target becomes eligible
             |
CI Qualification                    Release Delivery
same candidate revision             same selected target revision
no publication capability           planning/build/qualification: no capability
                                     Official: protected authoritative ref
                                     first Buddy slice: any same-repository ref
                                     |
                                     +-- channel approval gate
                                           protected Environment
                                           Authorization Record
                                     |
                                     +-- destination-specific capability groups
                                           destination Environment
                                           minimal job permissions
                                           OIDC identity
                                           destination trust policy
```

Delivery Governance is the external authority boundary. CI and Release consume
its enforcement outcomes; they do not grant authority to themselves.

## Same-Revision Control

CI planning and finalization use the code contained in the tested candidate
revision.

Live Release planning, finalization, and side-effect orchestration use the code
contained in the exact selected target revision. Official targets remain
protected and authoritative. The `hcoona-release-smoke-npm` live Buddy GitHub
Packages slice may use any same-repository ref selected by `workflow_dispatch`;
its selected-ref workflow, Planner, Finalizer, Providers, Adapters, compiler,
authenticated clients, static catalogs, capability declarations, and publisher
require neither protected-ref eligibility nor a CODEOWNERS-approved merge.

Release exclusively owns complete Official dry-run simulation. It uses planning
and finalization code from the exact selected simulation revision and receives
no approval, publication Environment, OIDC permission, or live publication
Capability.

There is no independently selected authority revision, control-code promotion
protocol, or runtime control-code substitution.

Governance normally requires owner review for changes to:

- CI and Release planning or finalization;
- workflow permissions and topology;
- authoritative record shapes;
- minimum qualification policy;
- executable Providers, Build and Quality Adapters, and Repository Model
  compiler code;
- generic authenticated clients and Release Destination Adapters;
- static Definition and implementation catalogs;
- execution-class and capability declarations;
- cross-revision contract compatibility and migration code;
- destination identity or Environment references; and
- rollout and remediation controls.

That review requirement remains unchanged for CI and Official, future Buddy and
production scopes, protected cross-revision compatibility code, and Break-Glass
Remediation. Solely for the named Buddy live Attempt, it is waived for the
selected-ref workflow, Planner, Finalizer, Providers, Adapters, compiler,
authenticated clients, static catalogs, capability declarations, and publisher.
It does not move control to protected main; same-revision execution remains
mandatory.

A merged control-code fix becomes available only in the new revision that
contains it. Ordinary replay of an older Release target continues to use the
older control code. External state left by an older target is handled through
reconciliation or separately authorized remediation.

## Native Platform Configuration

The following native configurations are authoritative:

- GitHub Rulesets and protected-ref settings;
- CODEOWNERS and required reviewers;
- GitHub workflow and job permissions;
- GitHub Environments and required reviewers;
- GitHub OIDC token claims;
- destination trusted-publisher and identity policy; and
- destination permissions and mutability controls.

v3 does not introduce a central `governance` descriptor that mirrors those
settings.

Repository code may reference stable platform interfaces needed for execution,
such as:

- Environment names;
- expected OIDC audience and subject conventions;
- destination identity; and
- required GitHub job permissions.

Those references do not become a second authority source. The actual platform
configuration remains authoritative.

## Runtime Permission Model

### CI Qualification

CI jobs:

- receive no publication credentials;
- receive no destination secrets;
- do not receive `id-token: write`;
- do not bind live publication Environments; and
- cannot request Buddy or Official Publication Capability.

CI may perform artifact-shape or other validation-only work, but it does not own
or execute complete Official dry-run planning. Official dry-run belongs
exclusively to Release simulation under the Release permission boundary.

### Release Planning and Qualification

Release planning, Repository Model discovery, build, quality, Evidence
Admission, and qualification finalization:

- receive no publication credentials;
- receive no destination secrets;
- do not receive `id-token: write`; and
- do not bind live publication Environments.

Target-controlled build execution and publication authority therefore remain
separate.

Qualification may declare Capability requirements but cannot request, approve,
or create live Capability. The normal v3 live path may request destination
Capability only in a side-effect capability group with a valid Authorization
Record and successful credential-free Capability Admission Decision after
validating the exact Publication Snapshot and action bindings and revalidating
the protected attestation's `live_enabled` field and fixed-source Governance
freshness.

### Channel Approval and Authorization Record

A Release requests one channel-level approval after the exact Publication
Snapshot is sealed. The approval gate:

- binds the Buddy or Official channel Environment;
- receives no destination credentials or OIDC permission;
- verifies the Publication Snapshot digest; and
- exposes the immutable digest-bound reviewer-summary artifact through the
  deployment URL and completed producer-job summary; and
- emits an append-only Authorization Record binding approval to the Snapshot
  digest and reviewer-summary artifact ID/digest only after successful approval.

The Authorization Record is not a credential and cannot authorize a different
snapshot or reviewer-summary artifact. Binding mismatch fails closed.

Terminal denial Evidence is admissible only where a platform supplies
documented exact current-attempt and approval-job proof. GitHub Environment
`DeploymentReview` lacks authoritative `run_attempt` and job binding and has no
documented append-only/consistency contract suitable for review-ID delta proof.
The first slice therefore admits no Approval Outcome Evidence for rejection or
denial. Rejection is unknown approval-contract failure, leaves a replayable
incomplete Attempt, starts no Capability, and may retain observable review data
only as a non-authoritative human diagnostic.

GitHub cancellation or platform expiry while approval remains pending may
terminate the run before a separate record or Finalizer outcome exists. If no
capability group started, the platform run/job conclusion is sufficient
no-side-effect terminal evidence and leaves a replayable incomplete Attempt. If
any capability job may have started, cancellation is not no-side-effect proof;
the Attempt is incomplete and possibly mutated, and replay reobserves. The
system need not distinguish manual cancellation from expiry unless GitHub
exposes it.

The first-slice workflow relies on GitHub's platform run and Environment
conclusions. Platform gate expiry is currently up to 30 days. Release control
and artifact retention is 45 days and activation blocks if repository policy
cannot provide that supported margin. Neither retention nor an approval already granted or still pending extends
Governance validity. Capability admission must still freshly observe the
protected attestation's `live_enabled` field as true and the admitted
at-most-90-day document as unexpired.

### Destination Capability Groups

Before any destination job is scheduled, the credential-free capability
admission job validates Authorization Record, Publication Snapshot,
reviewer-summary artifact, actions, artifacts, complete resource keys, and the
group manifest. Immediately before deciding, it uses `contents: read` to freshly
resolve the policy-fixed protected ref and read the attestation document,
verifies ref protection, schema, canonical content, policy/package bindings,
current expiry, and `live_enabled: true`, and compares repository/ref/path plus
commit/blob/content provenance and content identity to the current Attempt's
admitted Live Eligibility Decision. A false `live_enabled` value, expiry,
changed source or provenance, content or binding mismatch, or policy
invalidation blocks publication. Governance restoration requires a new Attempt;
the approved current Attempt cannot resume. Only success permits a
credential-bearing job to start.

Each destination-specific capability group executes in a dedicated job.

That job:

- binds the exact channel and destination Environment;
- receives only the GitHub permissions required by that destination;
- receives `id-token: write` only when OIDC is required;
- obtains destination capability just in time;
- consumes verified immutable artifacts and the materialized publication
  description;
- does not check out or execute target-defined product/build source code; and
- emits per-action Receipts and one capability-group result bundle.

The normal v3 workflow keeps workflow-level permissions empty or read-only. For
a reusable live Attempt, `packages: write` appears only on the `uses`-only
caller job as the reusable-workflow permission ceiling and on the called
Environment-referencing publisher job as effective capability. The caller job
has no steps or direct token use. `evaluate-live-eligibility` receives only
`contents: read`; effective `actions: read` is confined to the called
history-admission job, and explicit `packages: read` to the observer. Every
other job explicitly remains least-privilege, and no job receives Actions
history or package permission by inherited omission. The called workflow cannot
elevate beyond the caller-job ceiling. Independent capability groups may run in
parallel only after their credential-free admissions succeed. Actions within a
group remain ordered and fail-stop.

The human-gated approval job and destination capability job are separate. A
destination Environment need not repeat the channel reviewer; it may act only
as the capability-delivery boundary after the separate admission gate succeeds.
The publisher may repeat the same `contents: read` admitted-binding and
Governance-freshness checks immediately before mutation as defense in depth.
That repeat uses no new credential or service and does not turn the
branch-controlled publisher into a malicious-writer boundary. Additional
destination approval remains optional policy.

### First-Slice Buddy GitHub Packages Exception

For live Buddy publication of the dedicated `hcoona-release-smoke-npm`
disposable smoke package:

- the exact selected same-repository target revision supplies workflow, Planner,
  Finalizer, Providers, Adapters, compiler, authenticated clients, static
  catalogs, capability declarations, and publisher code;
- no protected-ref or CODEOWNERS-approved eligibility is required;
- the exact Publication Snapshot must exist before the dedicated protected
  Buddy Environment requests human approval;
- the normal v3 live path requests no package-write Capability before successful
  approval and successful credential-free capability admission;
- the approved target-revision side-effect job receives short-lived
  `GITHUB_TOKEN` with minimum `packages: write`;
- the job receives no PAT fallback and no `id-token: write`; and
- self-review prevention is enabled where GitHub supports it.

This bounded risk exception was reopened and reconfirmed before LLD on
2026-08-06.

Environment approval is the explicit trust elevation for branch-controlled
publisher code. It is not cryptographic or independent semantic validation.
Because the publisher code comes from the target revision, Governance does not
claim that a separate protected executor enforces the authorized Snapshot or
constrains malicious target code after approval.

Every actor with repository Write, Maintain, or Admin access is inside the
trusted publisher TCB for this bounded slice. External/fork contributors and
actors without repository write are outside it and cannot manually dispatch the
live path under normal GitHub permissions. GitHub Environment approval is a
mandatory control against mistakes, accidental publication, and ordinary
process violations; it is not a non-bypassable permission ceiling against a
malicious repository writer. A trusted writer can create alternate workflow
YAML or jobs with `packages: write`.

Optional GitHub workflow-execution protections may reduce who can execute
workflows, but this design neither requires that preview feature nor treats it
as a per-job permission ceiling. If repository membership changes so that any
Write/Maintain/Admin actor is not trusted to publish, live Buddy becomes blocked
until either that actor's repository access is reduced below
Write/Maintain/Admin or package-write Capability and destination access are
placed behind an independently enforced publisher boundary unavailable to
writer-authored workflows. Ref narrowing, Environment branch restrictions,
CODEOWNERS, and workflow-execution protections may remain defense in depth but
are insufficient remediation by themselves while an untrusted writer can
author alternate workflows with `packages: write`.

The approval surface must show target SHA, selected branch or ref, exact package
coordinate, artifact digest and manifest, package lifecycle scripts, and exact
action summary. Rollout must verify the dedicated package and destination,
separate Buddy Environment, smallest package/repository permission boundary,
absence of normal developer/CI/production consumers, and absence of planned or
ordinary delete, restore, permission, visibility, or admin actions. Package
deletion or restore uses Break-Glass Governance. Rollout records latent
repository/package admin authority as accepted trusted-writer misuse risk; it
does not require proving that authority is absent.

A permanent repository-wide HK dependency-policy gate scans dependency
manifests, lockfiles, workflows, install scripts, and dependency configuration
for normal developer, CI, or production consumption of the disposable smoke
package. Dependency-surface changes trigger it, and `slice-validation` runs it
unconditionally. Any consumer fails the gate, disables live use, and reopens
the exception for Governance review.

The accepted residual risk is that an approved malicious or mistaken branch can
publish arbitrary or malicious bytes, squat reachable names or versions, create
registry clutter or cost, or abuse package operations allowed by the
repository/package token. Rollout inspects and records actual token permissions
and package/repository grants, verifies that Official and known production
assets are unreachable, and performs safe denial probes only against enumerated
unrelated assets. It does not claim universal negative reach proof. Other
reachable package operations under the smallest configured grants remain
accepted writer-TCB risk. Future Buddy destinations require a new threat and
cost decision, and production packages do not inherit the exception.

The implementation PR merge is the direct repository-wide v1 Buddy-to-v3 Buddy
cutover. It lands v3 disabled and creates no replacement compatibility
workflow. Governance freezes Buddy dispatch, disables both legacy workflow
identities (`buddy.yml` and `release-buddy.yml`), cancels or drains queued,
waiting, approval-pending, and running executions, and verifies both disabled
state and old-ref dispatch rejection. A rejection added only to new YAML is
insufficient. All legacy Buddy publication routes retire; former Buddy projects
remain unsupported until explicitly migrated into future v3 slices. v1 Official
and CI remain unchanged unless separately covered.

### Buddy and Official Isolation

Buddy and Official use distinct:

- GitHub Environments;
- OIDC trust subjects or equivalent identity constraints;
- destination namespaces, identities, or prerelease channels; and
- destination permissions.

The first-slice Buddy job uses its dedicated approval tier, but it cannot obtain
Official capability. Official retains protected authoritative refs,
owner-reviewed control code, its isolated Environment, and destination trust.

## Platform Enforcement Outcomes

CI and Release react to platform outcomes rather than reproducing platform
adjudication.

- If a required review or Ruleset condition is not met, CI or Official does not
  become eligible through the protected platform path. The named Buddy slice
  does not require that path.
- If a protected Environment is not approved, the side-effect job does not
  proceed.
- If OIDC identity cannot be obtained, the destination action fails.
- If destination trust rejects the identity or requested operation, the action
  fails.
- No failure falls back to a long-lived token, personal token, weaker
  Environment, alternate workflow, or alternate identity.

The Release Finalizer treats missing or failed required side effects according
to the Release state model. It does not reinterpret a platform denial as
authorization.

## No Runtime Governance Shadow

CI and Release do not:

- query reviews and decide whether they are sufficient;
- re-evaluate Rulesets;
- compare live Environment configuration with a repository mirror;
- preflight OIDC merely to prove that a later side-effect job might receive a
  token;
- maintain an internal list of people authorized to approve publication; or
- infer authorization from branch names when the platform gate has not granted
  capability.

This avoids two policy engines with different behavior and ownership.

## Threat and Cost Balance

The design addresses these concrete threats:

| Threat                                                         | Primary Control or Accepted Boundary                                                  |
| -------------------------------------------------------------- | ------------------------------------------------------------------------------------- |
| Unreviewed change weakens CI or Official decision code         | CODEOWNERS, required review, and protected merge                                      |
| Target-controlled build code attempts to publish outside slice | No credentials, Environment, or OIDC permission in execution jobs                     |
| Approved first-slice Buddy target publishes malicious bytes    | Accepted bounded risk; reviewer context, disposable package, minimal token scope      |
| Trusted repository writer creates an alternate write workflow  | Inside first-slice TCB; accepted risk, attested membership/grants, Official isolation |
| First-slice Buddy target abuses reachable package operations   | Accepted bounded risk; isolated package/repository scope and Break-Glass admin        |
| Buddy attempts to publish Official identity                    | Separate Environment, destination identity, permissions, and no Official token        |
| Side-effect job publishes a different artifact or action       | Normally trusted executor; not guaranteed against approved slice target code          |
| Credential acquisition fails                                   | Fail the side effect without fallback                                                 |
| Platform governance is configured incorrectly during rollout   | Agent-guided rollout inspection and safe acceptance probes                            |

The initial design does not add an external policy service, continuous
governance reconciler, or duplicate approval database. Those controls would
add deployment and maintenance cost without a current threat that justifies
them.

## Governance Rollout Acceptance

Governance activation uses a repeatable, Agent-guided procedure rather than a
fully automated permanent test system.

### Agent Static Inspection

The Agent inspects repository-controlled surfaces, including:

- workflow-level and job-level permissions;
- Environment references;
- absence of publication capability in CI and qualification jobs;
- separation of Buddy and Official workflow identities;
- CODEOWNERS final-match resolution to `@hcoona` for the v3 package,
  `eng/workflow-delivery/v3/**`, Release Unit and quality descriptors, HK
  configuration/project, root Python workspace/lock inputs, workflows, actions,
  scripts, and the exact protected Governance document
  `/.github/workflow-delivery/governance/hcoona-release-smoke-npm.json`,
  including discovery of newly added descriptors and detection of missing or
  overridden patterns; this merge-time coverage does not constrain arbitrary-ref
  first-slice Buddy eligibility;
- exact first-slice Buddy package, GitHub Packages destination, Environment,
  `GITHUB_TOKEN` permissions, absence of PAT and OIDC, and consumer isolation;
- actual token permissions and package/repository grants, Official and known
  production isolation, and the bounded set of unrelated assets safe to probe;
- repository Write/Maintain/Admin membership and explicit trusted-publisher TCB
  acceptance;
- protected-ref non-executable writer-TCB/access attestation, explicit accepted
  writer and package/repository/Manage Actions access inventory or evidence
  digest, issuer, inspection time/expiry, acknowledged limitations, and fixed
  source provenance;
- disabled `buddy.yml` and `release-buddy.yml` workflow identities, drained or
  canceled executions, old-ref dispatch rejection, and absence of any legacy
  Buddy compatibility route;
- permanent HK dependency-policy coverage and current no-consumer result;
- planned action catalogs excluding delete, restore, permission, visibility, and
  admin operations, while recording latent trusted-writer admin authority as
  accepted risk;
  and
- absence of credential fallback paths.

### Agent Platform Inspection

Using approved platform tools such as `gh` and destination APIs, the Agent
collects the actual configuration that the operator is permitted to inspect:

- relevant Rulesets and protected refs;
- Environment configuration and reviewers;
- workflow identity and OIDC claim expectations;
- trusted-publisher registrations;
- destination identities and permissions; and
- existing v1 or v2 publication identities that can conflict with v3.

The Agent reports mismatches and blocks rollout on unresolved required items.
This inspection is rollout evidence, not a runtime authority mirror.

### Human Gates

A human performs actions that the platform intentionally reserves for human
judgment, including:

- protected Environment approval;
- explicit acceptance of the first-slice branch-controlled publisher risk after
  inspecting target/ref, package coordinate, artifact manifest/digest,
  lifecycle scripts, and action summary;
- acceptance of destination or organizational risk;
- confirmation of settings unavailable through approved APIs; and
- authorization of any production-impacting smoke action.

The Agent records the result but does not simulate or bypass the gate.

### Controlled Smoke Scenarios

Where safe test identities or destinations already exist, the rollout may
exercise a small set of scenarios:

- an allowed Buddy publication using Buddy identity;
- approved first-slice publication from a non-protected same-repository ref with
  reviewer-visible target, coordinate, artifact, lifecycle-script, and action
  details;
- an allowed Official test publication using the Official Environment;
- safe denial of Buddy identity at each enumerated Official, production, or
  unrelated probe asset;
- absence of OIDC permission in a qualification job; and
- first-slice rejected approval producing unknown replayable incomplete state,
  diagnostic-only review information, and no Authorization Record or Capability;
- approval-pending cancellation or expiry proving no side effect from the
  platform conclusion when no capability group started, without requiring a
  separate Release outcome; and
- successful Receipt capture for an authorized side effect.

The rollout does not create risky production changes merely to prove a negative
case. Unsupported or unsafe probes remain explicit human inspection items.

### Temporary Destination-Acceptance Bootstrap

Normal v3 live remains disabled while a temporary protected one-time workflow
runs destination probes. Its purpose is distinct from normal Release dispatch.
It:

- runs only from an approved protected ref;
- validates exact hard-bound target SHA, fixed acceptance-only coordinate in the
  same disposable package, and explicit confirmation;
- accepts no normal Release target, channel, version, destination, or force
  inputs;
- uses a dedicated reviewer-protected acceptance Environment;
- grants `packages: write` only to probe jobs; and
- emits Governance acceptance evidence, never live Product, Execution, Attempt,
  Authorization, Receipt, or Release history.

Every probe job independently fails closed unless
`github.run_attempt == 1`. The terminal evidence-capture job uses
`if: ${{ always() && github.run_attempt == 1 }}` or an exact equivalent. On the
first attempt it therefore persists each dependency result, available response
and diagnostic, and failed, skipped, canceled, incomplete, or ambiguously
mutating probe disposition even when an upstream dependency fails. It
classifies incomplete or unknown destination state for reconciliation. The
evidence job still rejects non-first attempts. This prevents a partial rerun
from reusing an earlier Environment review or disposable coordinate. A retry
requires a new reviewed workflow invocation and a new fixed disposable
coordinate/version.

The fixed coordinates are disposable Governance probe fixtures, not NBGV
product versions or Release projections.

The ordered cutover is: merge v3 code with the protected attestation's
`live_enabled` field false and both legacy Buddy workflow files removed; freeze
Buddy dispatch; disable both legacy workflow identities; cancel or drain
queued, waiting, approval-pending, and running executions; verify disabled
state, removal, and old-ref dispatch rejection; run and capture acceptance
probes; remove the acceptance workflow, temporary bypass, and Environment;
verify their removal; then use an authorized protected commit to set
`live_enabled` true for only the named smoke package. v1 Official and CI assets
remain unchanged; legacy Buddy workflows, Buddy-specific tests and matrices,
and Buddy documentation are excluded from that preservation and are retired or
rewritten. The sequence has an intentional brief Buddy outage. If acceptance
fails, all Buddy publication stays disabled, the temporary path is removed,
legacy Buddy remains retired, and any probe state is handled through
reconciliation or Break-Glass. A later retry requires a newly reviewed one-time
bootstrap invocation and a new fixed disposable coordinate/version. No reusable
bypass or compatibility rollback remains; restoring legacy Buddy requires a
separate user-approved rollback PR.

### Revalidation Triggers

Relevant rollout checks are repeated when changes affect:

- Rulesets or protected refs;
- CODEOWNERS or required review;
- workflow permissions or Environment bindings;
- repository Write/Maintain/Admin membership or team trust;
- OIDC trust;
- destination identity or permissions;
- Buddy/Official isolation; or
- Break-Glass Remediation governance.

Human Governance also re-attests repository Write/Maintain/Admin trust and
package/repository/Manage Actions access at least every 90 days. Any listed
change requires an authorized human to promptly commit `live_enabled: false` to
the policy-fixed protected document pending inspection and explicit
reacceptance. Protection, review, merge, and fresh-read latency mean this is a
bounded operational response, not instantaneous platform disablement; expiry
still blocks stale normal flows if the operational response is missed.

The accepted result is a canonical, non-executable Governance attestation. It
binds schema, a required top-level boolean `live_enabled`, explicit accepted
writer inventory, explicit
package/repository/Manage Actions access inventory or evidence digest,
policy/package identity, issuer, inspection time, expiry no later than 90 days,
and acknowledged limitations. The immutable first-slice Governance-source
contract carried by the concrete Release policy or its selected static
Governance-policy catalog is exactly:

- repository: `hcoona/three`;
- ref: `refs/heads/main`; and
- path:
  `.github/workflow-delivery/governance/hcoona-release-smoke-npm.json`.

The attestation document is the authoritative normal-flow live-enable source,
but it grants no Capability by itself.

Before every first-slice live Attempt, Release uses `contents: read` to freshly
verify ref protection, resolve the fixed ref to a full commit SHA, and read the
attestation blob at that commit. It validates `live_enabled: true` and binds the
boolean plus repository/ref/commit/path/Git blob OID/canonical content SHA-256
into the exact-target Live Eligibility Decision. The payload need not
self-reference this provenance. Any mismatch, expiry, malformed content, or
false `live_enabled` state blocks.

Immediately before Capability Admission, Release performs the same source read
again rather than relying on the pre-Attempt observation or a cached blob. It
uses `contents: read` to freshly resolve the protected ref and requires
`live_enabled: true`, an unexpired valid document, unchanged policy source
fields, and newly resolved commit/blob/content provenance and content identity
identical to the Live Eligibility Decision. A newly issued valid attestation is
intentionally a mismatch for the existing Attempt: after Governance
restoration, a new Attempt must establish new eligibility and obtain new
approval.

Runtime does not enumerate current repository writers or GitHub Packages grants:
the current `GITHUB_TOKEN` cannot provide the former and GitHub Packages exposes
no complete grants API. Human inspection is therefore a bounded-staleness
snapshot. Relevant role, grant, or Manage Actions changes require immediate
protected-source disablement and a new attestation; the at-most-90-day expiry
bounds normal-flow staleness. After inspection, an authorized human updates and
re-attests the document before a later protected commit may restore
`live_enabled: true`. Governance adds no repository variable, PAT, GitHub App,
service identity, OIDC permission, ledger, or additional token permission. This
process does not constrain a malicious actor already accepted into the writer
TCB or stop a capability job that passed its final check before the disabling
commit became visible.

Ordinary CI and Release runs do not continuously compare platform configuration
against a stored snapshot.

## Failure Conditions

Governance integration is not ready for activation when:

- control surfaces that are not part of the named Buddy target-code exception
  lack required owner review;
- a build or qualification job can obtain publication capability;
- CI owns or executes complete Official dry-run planning;
- the first-slice Buddy package, destination, Environment, token scope, or
  no-consumer constraint is not exact and isolated;
- any repository actor with Write, Maintain, or Admin access is not trusted as a
  Buddy publisher;
- either `buddy.yml` or `release-buddy.yml` remains enabled, an old-ref dispatch
  is accepted, a queued/waiting/approval-pending/running legacy execution is not
  drained or canceled, or any compatibility Buddy route remains;
- a former v1 Buddy project is accepted by v3 without an explicitly migrated
  slice;
- the permanent HK dependency-policy gate is absent, does not cover dependency
  surfaces, or finds a normal smoke-package consumer;
- writer-TCB or package/repository grant re-attestation is overdue or pending
  after a relevant change;
- the fixed-source attestation is missing, executable, unreadable, expired,
  malformed, provenance-mismatched, or inconsistent with policy/package
  bindings;
- the temporary acceptance workflow accepts normal Release inputs, runs from an
  unprotected ref, can emit live Release identity/history, leaves package-write
  outside probe jobs, or remains present after acceptance;
- the first-slice approval surface omits target/ref, package coordinate,
  artifact digest/manifest, lifecycle scripts, or action summary;
- the approval deployment URL, completed job summary, or Authorization Record
  does not bind the same immutable reviewer-summary artifact ID/digest and
  Publication Snapshot digest;
- the normal first-slice capability job receives a PAT or `id-token: write`, or
  inspection/probes establish reach to known Official or production assets;
- the planned or ordinary first-slice action set includes delete, restore,
  permission, visibility, or admin operations;
- Buddy and Official share an identity capable of reaching Official state;
- an Official side-effect job is not protected by the intended Environment;
- destination trust accepts broader workflow identities than intended;
- required platform configuration cannot be inspected or confirmed; or
- a required acceptance item remains unresolved.

## Deferred LLD Decisions

- exact Environment names;
- exact CODEOWNERS patterns and final-match owner resolution tests;
- exact Ruleset configuration;
- exact GitHub job permissions by destination;
- exact first-slice `GITHUB_TOKEN` permission and package/repository access
  configuration;
- repository-writer TCB and package/repository grant inventory, change-triggered
  and periodic re-attestation procedure, and live-disable/reacceptance control;
- optional workflow-execution protection evaluation without treating it as a
  required dependency or permission ceiling;
- reviewer-visible Buddy approval summary and lifecycle-script inspection
  contract;
- self-review prevention behavior and fallback when the platform cannot enforce
  it;
- Break-Glass package deletion and restore procedure;
- exact OIDC audience and subject claims;
- destination-specific trusted-publisher setup;
- first-slice rejection diagnostics and tests proving GitHub Deployment Review
  data and review-ID deltas are not authoritative Approval Outcome Evidence;
- CODEOWNERS tests resolving final-match ownership to `@hcoona` for every
  governed file, including
  `/.github/workflow-delivery/governance/hcoona-release-smoke-npm.json`,
  discovering new descriptors, and failing missing or overridden patterns
  without adding runtime eligibility;
- tests proving first-slice rejection grants no Capability and remains
  replayable incomplete, approval-pending pre-capability cancellation/expiry may
  lack a context-owned outcome, and possible post-capability cancellation
  requires reobservation;
- credential-free capability-admission validation and proof that no
  package-write job can be scheduled before success;
- exact 45-day first-slice Release retention configuration and policy check;
- protected-ref non-executable attestation schema, exact source-field contract
  for `hcoona/three`, `refs/heads/main`, and
  `.github/workflow-delivery/governance/hcoona-release-smoke-npm.json`,
  fixed-source provenance, canonical digest tests, exact-target Live
  Eligibility Decision binding, and fail-closed missing/unreadable/expired/
  changed/consumer-positive scenarios before Attempt creation;
- post-approval Governance-freshness tests proving capability admission uses
  `contents: read` to freshly resolve and read the fixed source, rejects
  `live_enabled: false`, expiry during an approval wait,
  ref/commit/blob/content or binding changes, and invalidation, requires a new
  Attempt after restoration, and permits publisher-side repeat validation only
  as defense in depth;
- direct retirement of both legacy Buddy workflow identities, execution
  drain/cancellation, old-ref rejection, outage communication, and
  no-compatibility rollback checks;
- permanent dependency-policy gate and no-consumer checks;
- temporary protected acceptance-bootstrap workflow, fixed coordinates,
  per-probe `github.run_attempt == 1` guards, terminal evidence capture with
  `always() && github.run_attempt == 1`, non-first-attempt rejection,
  dependency-failure and ambiguous-mutation evidence persistence,
  incomplete/unknown reconciliation classification, partial-rerun rejection,
  new-reviewed-invocation and new-coordinate retry, failure handling with legacy
  Buddy retired, removal, and removal verification;
- rollout checklist command sequence; and
- safe smoke identities and cleanup procedures.
