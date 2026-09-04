# Workflow Delivery v3 Governance Integration MLD

## Status

Architecture version: **v3**.

Review state: **Confirmed; approved normal Live baseline incorporated on
2026-08-31**.

This middle-level design defines how CI Qualification and Release Delivery use
GitHub and destination platforms for review, protected execution, identity, and
publication authority.

It realizes the
[Workflow Delivery v3 Requirements](./requirements.md),
[High-Level Design](./high-level-design.md), and
[Architecture Glossary](./architecture-glossary.md).

The normal Live implementation is delivered but remains disabled through
protected Governance with `live_enabled: false`.

## Scope

This MLD owns:

- the authority boundary between repository code and external platforms;
- same-revision control-code eligibility;
- workflow permission and credential placement;
- the first-slice Buddy accepted-risk boundary;
- protected Governance admission and freshness;
- the one Approval Environment contract;
- Buddy and Official authority isolation;
- platform-denial behavior; and
- normal Live activation governance.

This MLD does not own:

- CI scope or obligation policy;
- Release destination observation or action planning;
- Repository Model discovery;
- registry-specific publication mechanics;
- Release outcome classification;
- a repository mirror of platform governance state;
- a permanent governance service or audit ledger; or
- exhaustive discovery of GitHub Packages grants.

## Governing Principle

GitHub and destination platforms own the guarantees they formally expose.

CI and Release use those guarantees but do not reimplement weaker copies of:

- CODEOWNERS and required review;
- Ruleset and protected-ref enforcement;
- GitHub Environment review;
- workflow permission enforcement;
- GitHub OIDC identity issuance; or
- destination trusted-publisher and authorization policy.

Application code validates the identities and data bindings it creates. Native
configuration readback and attestation establish platform facts that a workflow
job cannot establish from its own resolved context.

## Authority Topology

```text
Repository governance
  CODEOWNERS / required review / Rulesets / protected refs
             |
             +-- CI candidate or Official target becomes eligible
             |
CI Qualification                    Release Delivery
same candidate revision             same selected target revision
no publication capability           plan/build/qualify/observe: no write
                                     |
                                     +-- zero action
                                     |     read-only exact finalization
                                     |
                                     +-- one action
                                           Approval Bundle
                                           literal Approval Environment
                                           Publication Authorization
                                           publisher with just-in-time capability
```

Delivery Governance is the external authority boundary. CI and Release consume
its enforcement outcomes; they do not grant authority to themselves.

## Same-Revision Control

CI planning and finalization use the code in the tested candidate revision.

Live Release planning, finalization, and side-effect orchestration use the code
in the exact selected target revision. Official targets remain protected and
authoritative. The `hcoona-release-smoke-npm` live Buddy slice may use any
same-repository ref selected by `workflow_dispatch`. The selected ref resolves
to one exact SHA that supplies the workflow, Planner, Finalizer, Providers,
Adapters, compiler, clients, catalogs, capability declarations, publisher, and
Release target.

Protected Governance is read independently from `refs/heads/main`. The design
does not substitute protected-main control code for the selected target.

The active protected document uses exact schema
`workflow-delivery/v3/normal-live-governance-attestation-v2`. V2 replaces the
disabled v1 contract because `DestinationPrimitiveAttestation` now has a
different closed field set. Selected-revision control must require v2 exactly;
v1 is not an admission alias. Superseded parsers therefore fail before Release
Execution lookup, Attempt creation, or any Environment job. Arbitrary ref
selection remains available to refs compatible with the active Governance
contract.

V2 keeps the existing closed activation-state discrimination while replacing
the ready-state destination attestation. `blocked` contains only
`state: "blocked"`, requires `live_enabled: false`, and carries no Approval
Environment, retention, or destination evidence. `ready` contains
only `state`, complete pass-only `approval_environment`,
`artifact_retention`, and `destination_primitive` objects. A true live flag
requires `ready`; a false flag may coexist with either state so protected
Governance can disable fresh admission without discarding retained evidence.
The implementation migration uses blocked v2, and the Activation PR changes to
ready evidence plus `live_enabled: true` atomically.

Release simulation uses its selected simulation revision and receives no
approval or live Publication Capability. Its existing run-attempt identity and
rerun semantics remain unchanged.

There is no independently selected authority revision, control-code promotion
protocol, or runtime control-code substitution.

Governance normally requires owner review for changes to:

- CI and Release planning or finalization;
- workflow permissions and authority-critical topology;
- authoritative record shapes;
- minimum qualification policy;
- executable Providers, Adapters, and Repository Model compiler code;
- authenticated clients;
- static Definition and implementation catalogs;
- execution-class and capability declarations;
- cross-revision compatibility code;
- destination identity and trust configuration; and
- activation and remediation controls.

That requirement remains unchanged for CI, Official, future Buddy and
production scopes, protected cross-revision compatibility code, and
Break-Glass Remediation. It is waived only for the selected-ref control and
publisher surfaces in the accepted first-slice Buddy exception. A control-code
fix becomes available only in the revision that contains it.

## Native Platform Configuration

The following native settings are authoritative:

- GitHub Rulesets and protected-ref settings;
- CODEOWNERS and required reviewers;
- GitHub workflow and job permissions;
- GitHub Environments and their reviewers, bypass posture, deployment policy,
  wait timer, variables, and secrets;
- GitHub OIDC token claims;
- repository Actions artifact-retention policy;
- destination trusted-publisher and identity policy; and
- destination permissions and mutability controls.

Repository code may carry stable names and expected bindings needed to invoke
those facilities. Those references do not become a second authority source.

### Literal Approval Environment

The first slice uses exactly one authority-bearing Environment:
`workflow-delivery-v3-buddy-approval`.

Its approved configuration includes:

- required reviewer `hcoona`;
- the confirmed single-maintainer setting
  `prevent_self_review: false`;
- one exact Environment-scoped configuration sentinel; and
- the native reviewer, bypass, branch or tag, wait, variable, and secret
  settings accepted by Delivery Governance.

The Environment is used only by the action-bearing Approval job. An
`exact-satisfied` zero-action Attempt does not create an Environment deployment
or request approval.

The first slice has no second publication Environment and no generic
Environment Profile authority model. A future OIDC-backed channel may introduce
a channel-specific Environment only when the external destination trust policy
validates that Environment's OIDC claims. Reuse or symmetry alone is not a
reason to add one.

### Environment Configuration Sentinel

The Approval job validates the resolved sentinel value as its first
authority-critical executable check. A missing or mismatched value blocks the
job before it forms Publication Authorization.

The sentinel is only a configuration sentinel. The job:

- can observe the resolved value;
- cannot determine whether it came from Environment, repository, or
  organization scope; and
- cannot prove native reviewer, self-review, bypass, deployment-policy, secret,
  credential, or Environment-identity settings.

Authenticated native readback and Governance attestation must establish:

- that the named Environment exists;
- that its native settings match the approved configuration;
- that its secrets and variables match the approved configuration; and
- that no same-name repository or organization variable can shadow the
  Environment-scoped sentinel.

Only under those externally verified conditions does the runtime marker check
detect accidental implicit Environment creation or marker misbinding.

## First-Slice Trust Boundary

### Accepted Writer and Publisher TCB

`hcoona` is the sole accepted writer and publisher trusted-computing-base member
for this slice. External or fork contributors and actors without repository
write remain outside that TCB.

The normal controls remain useful against:

- outsiders;
- accidental operators;
- mistaken publication;
- ordinary process violations; and
- unintended authority propagation between Buddy and Official.

They are not claimed to constrain a malicious accepted writer. Such a writer
can author alternate workflow code or otherwise use repository-granted
authority outside the intended path. Protected `main`, the Approval
Environment, workflow permissions, static-reference checks, and exact action
validation are not a security boundary against that actor.

Any added Write, Maintain, or Admin actor, reviewer change, or relevant
repository, package, or Manage Actions access change requires
`live_enabled: false` and a new Governance decision.

### GitHub Packages Principal and Reach

The GitHub Packages credential principal is repository `hcoona/three`.

Its effective publisher reach includes every package whose package-side GitHub
Actions access grant authorizes that repository. The normal Publication Action
names the dedicated smoke coordinate, but exact coordinate, artifact, action,
and resource validation governs intended operation and reconciliation only. It
does not isolate the token to that package.

Governance records the relevant bounded access inspection and its limitations.
The architecture does not claim exhaustive current package-grant enumeration,
because GitHub does not expose a complete package-grant inventory suitable for
that claim.

Official npmjs PAT, OIDC, secret, and destination boundaries remain separate
and unchanged.

### Intended Action Boundary

Normal first-slice publication permits only the dedicated compound GitHub
Packages action for:

- the exact package version; and
- the target-derived routing tag.

Normal publication does not permit delete, restore, permission, visibility, or
administrative operations. Those operations require separately governed
Break-Glass Remediation.

This authority boundary does not by itself establish that the destination can
execute the compound action within the accepted footprint. Standard
`npm publish --tag` can overwrite a conflicting tag introduced after
Observation. It becomes an admitted normal-Live primitive only when its exact
pinned Destination Operation Profile passes the bounded
documented-and-observable native acceptance and protected Governance binds that
evidence. Runtime Observation proves only active-state absence and never
receives package-admin or PAT authority. The acceptance procedure therefore
also uses a fresh disposable version and separately authorized package-admin
credentials to prove that identical- and differing-byte same-version publishes
against deleted/restorable state fail definitively with no active or deleted
semantic delta, after which the original object is restored and its bytes and
witness are verified. Those privileged credentials authorize only the
acceptance procedure, not normal publication.

The bounded static-reference policy reports prohibited direct references in
its closed supported catalog. A clean result is an eligibility input, not proof
that no runtime consumer exists and not evidence that the repository token can
reach only the smoke package.

## Runtime Permission and Authority Model

### CI Qualification

CI jobs:

- receive no publication credentials;
- receive no destination secrets;
- do not receive `id-token: write`;
- do not bind live publication Environments; and
- cannot request Buddy or Official Publication Capability.

Official dry-run remains a Release simulation responsibility rather than a CI
publication path.

### Release Planning, Build, Qualification, and Observation

Release planning, Repository Model discovery, build, quality execution,
Evidence Admission, qualification finalization, and destination observation:

- receive no publication credential;
- receive no destination write token;
- do not receive `id-token: write`; and
- do not bind the Approval Environment.

Read-only observation may use the minimum destination read authority required
by its Adapter. It cannot convert that access into publication authority.

### Zero-Action Exact Finalization

A manual Release Intent plus valid protected Governance may authorize normal
read-only Observation and no-op finalization.

When the first-slice Publication Snapshot contains zero actions because the
destination is already exact:

- the no-op job repeats protected Governance ancestry, path-touch,
  blob/content, expiry, and `live_enabled` validation, repeats supported
  package-control readback, repeats authoritative exact-version readback
  against the Snapshot-bound bytes, digests, and embedded witness, and persists
  one exact-satisfied finalization proof binding the zero-action Snapshot and
  all three fresh checks for Finalizer admission;
- no Environment approval is requested;
- no Approval Bundle is sent through an Environment wait;
- no Publication Authorization is formed;
- no publisher or Publication Capability is used;
- the current-DAG publisher conclusion is `skipped`;
- no mutation marker, Publication Result, or other action-bearing lineage
  exists; and
- the Attempt may finalize as `success` with disposition
  `exact-satisfied`.

Unknown, partial, conflicting, or unprovable state is not a zero-action success.

### Action-Bearing Approval

When the Publication Snapshot contains the one permitted action, Release
prepares one immutable Approval Bundle before the Environment wait. The bundle
closes:

- the current Attempt, selected ref, and target;
- the Qualification Decision;
- the Publication Snapshot;
- the immutable reviewer summary;
- artifact identities, digests, and manifest;
- lifecycle-script information;
- the exact Publication Action; and
- the complete mutable-resource keys and serialization projection.

The Approval job:

- references `workflow-delivery-v3-buddy-approval`;
- has no publication capability;
- may use `contents: read` for fresh protected Governance;
- performs the sentinel check before other authority-critical executable work;
- validates path-touch anti-rollback and current Governance validity;
- compares the action's destination-operation-profile digest with current
  Governance, verifies native acceptance is unexpired for action-bearing
  admission, and validates the immutable action as a profile instantiation;
- strictly admits the Approval Bundle and transitively resolves its complete
  Snapshot, reviewer, artifact, action, and resource closure; and
- durably emits the sole Publication Authorization.

The Authorization directly binds the Approval Bundle plus approval-boundary
and fresh-Governance evidence. It reaches Snapshot-owned target, artifact,
action, resource, and operation-profile fields through that immutable
predecessor rather than copying them. The publisher embeds the only
post-Observation action-path package-control proof in the mutation marker. That
closed value binds authoritative endpoints, normalized supported facts,
observation time, and response digests; it copies neither expected values nor
the Governance content digest and is not a separate decision or artifact. The
marker jointly validates it against the directly bound final Governance proof.
The marker also records canonical evidence that the actual pinned toolchain and
effective command configuration matched the admitted operation profile.

The reviewer-visible projection includes target SHA and selected ref, exact
package coordinate, artifact digest and manifest, lifecycle scripts, and the
exact action.

There is no separate post-approval finalizer or admission record. The semantic
post-approval admission responsibility is fulfilled by the Approval job's
complete validation and Publication Authorization, followed by publisher
revalidation.

### Publisher

The publisher has an ordinary success dependency on the Approval job. It:

- strictly validates the Publication Authorization and every bound current
  Attempt input;
- repeats the fresh protected Governance check immediately before mutation;
- repeats supported package-control readback immediately before the marker;
- compares current Governance, the action profile, and the actual pinned
  toolchain and command configuration;
- receives short-lived repository `GITHUB_TOKEN` with effective
  `packages: write`;
- receives no PAT fallback and no `id-token: write`;
- executes no target-defined product or build code; and
- persists the required mutation and result records around the action.

The publisher is the only step-running job with effective `packages: write`.
A `uses`-only reusable-workflow caller may declare `packages: write` solely as
a non-elevating ceiling; that caller has no steps and does not use the token.

Target-revision publisher code is accepted by the first-slice TCB. Its binding
checks govern the normal process but do not create an independent malicious-
writer boundary.

## Protected Governance

### Source and Attestation

The immutable Governance source contract is:

- repository: `hcoona/three`;
- ref: `refs/heads/main`; and
- path:
  `.github/workflow-delivery/governance/hcoona-release-smoke-npm.json`.

The non-executable attestation:

- identifies `hcoona` as the sole accepted writer and publisher;
- binds the policy and package;
- records relevant repository and package-access inspection;
- reuses its destination-primitive attestation to bind the canonical
  Destination Operation Profile digest, native-acceptance-suite version,
  approved disposable-package preconditions, GitHub API and cited lower-layer
  contract revisions, capture time, and canonical evidence digest identifying
  the exact successful acceptance generation;
- records authenticated repository Actions retention readback proving the
  effective policy permits at least 45 days;
- records issuer and inspection time;
- acknowledges inspection and platform limitations;
- expires no later than 90 days after inspection; and
- carries top-level `live_enabled`.

It grants no Publication Capability by itself.

The separately authorized acceptance evidence, not runtime Governance, contains
the disposable tombstone scenario's detailed active and deleted inventories,
targeted deleted-version identity and continued restorability, publish
responses, semantic deltas, and restoration readback. Governance binds the
canonical digest of that complete successful evidence. The ready activation
state is the issuer's successful-acceptance attestation. Any missing or
ambiguous evidence element makes the operation profile inadmissible, but those
privileged facts do not become runtime inputs.

Initial activation of a newly admitted operation profile binds a destination-
acceptance generation captured after implementation of that exact profile and
no later than the new attestation's inspection time. Later attestations may
reuse that generation only while every bound input remains identical.
Action-bearing admission must occur no later than 90 days after capture; a
binding change or age expiry requires recapture before action-bearing
publication. Expired acceptance does not block fresh protected Governance from
authorizing zero-action exact-satisfied finalization.

### Eligibility Binding

Before live Execution lookup or Attempt creation, Release freshly reads the
protected source and validates schema, policy, package, writer, expiry, and
`live_enabled: true`.

The Live Eligibility Decision binds:

- repository, fully qualified ref, and path;
- attestation blob and canonical content identity, or an explicit monotonically
  governed attestation generation;
- the protected-path lineage point used for anti-rollback;
- the exact target and static-reference result; and
- the current request and Repository Model bindings.

It does not require the complete resolved `main` commit to remain equal.
Unrelated commits on `main` are allowed.

### Path-Touch Anti-Rollback

After eligibility, any commit that touches the protected Governance path
invalidates the current Attempt, even when a later commit restores identical
bytes.

Approval and publisher freshness checks must prove that no protected-path touch
occurred after the eligibility lineage point. Comparing only current bytes,
blob identity, or a later resolved `main` commit is insufficient.

A newly issued or restored attestation requires a new manual dispatch and a new
Attempt.

### Disablement Semantics

`live_enabled: false` blocks:

- fresh live admission; and
- a publisher that has not yet passed its final fresh Governance check.

It is not package rollback or instantaneous capability revocation. It cannot
stop a publisher that already passed the final fresh check, and it does not
reverse destination state.

Review, merge, and fresh-read latency therefore make disablement a bounded
operational response. The 90-day maximum attestation age independently bounds
normal-flow staleness.

## Buddy and Official Isolation

Buddy and Official retain distinct:

- channel policy;
- destinations and package coordinates;
- credential or OIDC trust boundaries;
- protected-target requirements;
- approval policy; and
- remediation authority.

The first-slice Buddy repository token cannot obtain Official npmjs authority.
Official retains protected authoritative targets and owner-reviewed control
code. Simulation receives no live authority.

The first-slice exception does not transfer to another Buddy destination,
production package, or Official publication path.

## Platform Enforcement Outcomes

CI and Release consume platform outcomes rather than recreating platform
adjudication.

- If required review or a Ruleset condition is not met, CI or Official does not
  become eligible through the protected path.
- If the Approval Environment is not approved, the Approval job does not run
  and no Publication Authorization exists.
- If required OIDC identity or destination capability cannot be obtained, the
  affected side effect fails.
- If destination trust rejects an identity or operation, the action fails.
- No failure falls back to a PAT, weaker Environment, alternate identity,
  alternate destination, or overwrite mode.

The read-only Release Finalizer may use current-DAG facts to classify the
Attempt. It does not reinterpret a platform denial as authorization, and it
need not reconstruct an exact Environment rejection reason.

## Normal Live Activation

Implementation and activation are separate deliveries. The implementation is
delivered first and remains disabled with `live_enabled: false`.

Activation uses one small protected Activation PR that enables the approved
Governance document. There is:

- no separate Preparation PR;
- no freeze of other `main` writes or normal dispatch;
- no pre-pinned Activation SHA; and
- no activation tag.

After the Activation PR merges, the first proving run is dispatched from
then-current protected `main`. The operator uses an explicitly supported REST
API version whose successful response contains `workflow_run_id`, validates the
response schema, and reads back the returned:

- workflow and run identity;
- actor;
- `workflow_dispatch` event;
- actual head SHA;
- `refs/heads/main`; and
- `github.run_attempt == 1`.

A lost response or ambiguous correlation triggers read-only operator
investigation and native run lookup. It creates no formal Reconciliation Record
and does not invoke a standalone Release Reconciliation workflow. The operator
never blindly redispatches.

Later normal Buddy runs retain the approved ability to select arbitrary
same-repository refs whose selected-revision control strictly admits the active
Governance schema.

Before activation, authenticated repository inspection and compatibility
fixtures prove that each retained dispatchable ref either implements the
one-Environment contract or rejects the active schema before any Environment
job or deployment. This prevents obsolete refs from implicitly recreating
unprotected Environment names after cleanup.

Fresh authenticated preactivation evidence must also prove that repository
artifact retention permits at least 45 days and that the selected destination
primitive and exact Destination Operation Profile have passed the bounded
documented-and-observable native acceptance. The command is not admitted by
name alone.

Every authoritative normal-Live job independently fails closed unless
`github.run_attempt == 1`. The value is a platform invariant and diagnostic,
not a normal-Live domain identity, record field, artifact binding, or
Publication Authorization input. Simulation retains its separate run-attempt
contract.

## No Runtime Governance Shadow

CI and Release do not:

- query reviews and decide whether they are sufficient;
- re-evaluate Rulesets;
- infer native Environment configuration from the sentinel;
- compare live Environment settings with a repository policy mirror on every
  run;
- preflight OIDC merely to prove that a later publisher might receive a token;
- maintain an internal approver database;
- infer authorization from a branch name; or
- claim exhaustive current package-grant enumeration.

Native readback and attestation are Governance evidence. Runtime validation is
limited to the bindings and freshness decisions required by the current
Attempt.

## Threat and Cost Balance

| Threat                                                | Primary Control or Accepted Boundary                                 |
| ----------------------------------------------------- | -------------------------------------------------------------------- |
| Unreviewed change weakens CI or Official control code | CODEOWNERS, required review, and protected merge                     |
| Target-controlled build code attempts publication     | No write capability in build or qualification                        |
| Accidental first-slice publication                    | Immutable reviewer context and one Approval Environment              |
| Outsider attempts to publish                          | Repository and platform access controls                              |
| Accepted writer creates alternate write workflow      | Inside the explicit first-slice TCB; not claimed controlled          |
| Repository token reaches another package grant        | Inside repository-principal blast radius; no package-isolation claim |
| Buddy attempts Official publication                   | Separate Official credentials, trust, destination, and policy        |
| Publisher uses stale or rolled-back Governance        | Fresh checks plus protected-path anti-rollback                       |
| Credential acquisition fails                          | Fail closed without fallback                                         |
| Environment is missing or marker is misbound          | Native readback plus first executable sentinel check                 |

The current design does not add an external policy service, continuous
governance reconciler, duplicate approval database, or permanent grant ledger.

## Failure Conditions

Governance integration fails closed when:

- an owner-reviewed surface outside the named Buddy exception lacks required
  review;
- a CI, build, qualification, or observation job can publish;
- the Approval Environment is missing or its native configuration is not
  attested as approved;
- the resolved sentinel is absent or mismatched;
- same-name broader variables have not been excluded by authenticated native
  readback;
- repository Actions retention has not been authenticated as permitting at
  least 45 days;
- the selected destination primitive, exact operation profile, or bound
  lower-layer/API contract has not passed the bounded documented-and-observable
  native acceptance;
- the acceptance lacks the deleted/restorable same-version scenario, a
  definitive empty active-plus-deleted delta for either republish attempt, or
  exact restoration readback;
- publisher-boundary supported package-control readback no longer matches
  accepted owner, repository association, visibility, or exposed access facts;
- native acceptance is older than 90 days for an action-bearing admission;
- the action's operation-profile digest differs from current Governance, the
  immutable action is not a valid profile instantiation, or the publisher's
  actual pinned runtime configuration differs from the profile;
- the action-bearing Approval Bundle or any transitively referenced Snapshot,
  reviewer, artifact, action, or resource binding is missing or inconsistent;
- the Approval job can publish or the publisher can start without its successful
  Publication Authorization;
- any step-running job other than the publisher has effective
  `packages: write`;
- the publisher receives a PAT or `id-token: write`;
- protected Governance is missing, unreadable, malformed, expired, disabled,
  binding-mismatched, or touched after eligibility;
- Governance freshness requires equality of unrelated `main` commits rather
  than protected-path continuity;
- a writer, reviewer, role, team, or relevant access change has not forced a
  new Governance decision;
- a normal action includes delete, restore, permission, visibility, or
  administrative behavior;
- Buddy can obtain Official authority;
- a lost activation response causes blind redispatch; or
- a non-first normal-Live run attempt can create or consume authority.

## Deferred LLD Decisions

Lower-layer design may define:

- exact strict record and canonicalization schemas;
- exact sentinel name and value;
- exact authenticated readback and attestation evidence format;
- exact protected-path history query and anti-rollback proof;
- exact REST API version and response/readback validation commands;
- exact job permission declarations and reusable-workflow ceiling;
- exact reviewer-summary rendering;
- destination-specific capability acquisition;
- exact failure and diagnostic codes;
- Break-Glass package deletion or restoration procedure; and
- tests for every failure condition above.

Lower-layer design must not introduce a first-slice second Environment,
Environment Profile abstraction, separate post-approval admission authority,
history-based authority, or exhaustive package-grant claim.
