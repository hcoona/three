# Workflow Delivery v3 Governance Integration MLD

## Status

Architecture version: **v3**.

Review state: **Draft synthesized from confirmed decisions**.

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
             +-- candidate or target revision becomes eligible
             |
CI Qualification                    Release Delivery
same candidate revision             same protected target revision
no publication capability           planning/build/qualification: no capability
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
contained in the exact protected target revision being released.

Dry-run simulation uses planning and finalization code from the exact selected
simulation revision. It receives no approval, publication Environment, OIDC
permission, or live publication Capability.

There is no independently selected authority revision, control-code promotion
protocol, or runtime control-code substitution.

Governance therefore requires owner review for changes to:

- CI and Release planning or finalization;
- workflow permissions and topology;
- authoritative record shapes;
- minimum qualification policy;
- destination identity or Environment references; and
- rollout and remediation controls.

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

This applies even when CI executes complete Official dry-run planning or
artifact-shape validation.

### Release Planning and Qualification

Release planning, Repository Model discovery, build, quality, Evidence
Admission, and qualification finalization:

- receive no publication credentials;
- receive no destination secrets;
- do not receive `id-token: write`; and
- do not bind live publication Environments.

Target-controlled build execution and publication authority therefore remain
separate.

### Channel Approval and Authorization Record

A Release requests one channel-level approval after the exact Publication
Snapshot is sealed. The approval gate:

- binds the Buddy or Official channel Environment;
- receives no destination credentials or OIDC permission;
- verifies the Publication Snapshot digest; and
- emits an append-only Authorization Record binding approval to that digest.

The Authorization Record permits destination-specific capability groups to
start. It is not a credential and cannot authorize a different snapshot.

### Destination Capability Groups

Each destination-specific capability group executes in a dedicated job.

That job:

- binds the exact channel and destination Environment;
- receives only the GitHub permissions required by that destination;
- receives `id-token: write` only when OIDC is required;
- obtains destination capability just in time;
- consumes verified immutable artifacts and the materialized publication
  description;
- does not check out or execute target source code; and
- emits per-action Receipts and one capability-group result bundle.

Workflow-level permissions remain empty or read-only. Publication authority
must not be granted broadly and then constrained only by application logic.
Independent capability groups may run in parallel after the Authorization
Record exists. Actions within a group remain ordered and fail-stop.

### Buddy and Official Isolation

Buddy and Official use distinct:

- GitHub Environments;
- OIDC trust subjects or equivalent identity constraints;
- destination namespaces, identities, or prerelease channels; and
- destination permissions.

Buddy may use a lower approval tier when platform policy permits it, but a Buddy
job cannot obtain Official capability.

## Platform Enforcement Outcomes

CI and Release react to platform outcomes rather than reproducing platform
adjudication.

- If a required review or Ruleset condition is not met, the revision does not
  become eligible through the protected platform path.
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

| Threat                                                       | Primary Control                                                         |
| ------------------------------------------------------------ | ----------------------------------------------------------------------- |
| Unreviewed change weakens CI or Release decision code        | CODEOWNERS, required review, and protected merge                        |
| Target-controlled build code attempts to publish             | No credentials, Environment, or OIDC permission in execution jobs       |
| Buddy attempts to publish Official identity                  | Separate Environment, OIDC trust, destination identity, and permissions |
| Side-effect job publishes a different artifact or action     | Verified immutable artifacts and materialized publication description   |
| Credential acquisition fails                                 | Fail the side effect without fallback                                   |
| Platform governance is configured incorrectly during rollout | Agent-guided rollout inspection and safe acceptance probes              |

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
- CODEOWNERS coverage for planning, finalization, workflow, and policy files;
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
- acceptance of destination or organizational risk;
- confirmation of settings unavailable through approved APIs; and
- authorization of any production-impacting smoke action.

The Agent records the result but does not simulate or bypass the gate.

### Controlled Smoke Scenarios

Where safe test identities or destinations already exist, the rollout may
exercise a small set of scenarios:

- an allowed Buddy publication using Buddy identity;
- an allowed Official test publication using the Official Environment;
- denial of Buddy identity at an Official destination;
- absence of OIDC permission in a qualification job; and
- successful Receipt capture for an authorized side effect.

The rollout does not create risky production changes merely to prove a negative
case. Unsupported or unsafe probes remain explicit human inspection items.

### Revalidation Triggers

Relevant rollout checks are repeated when changes affect:

- Rulesets or protected refs;
- CODEOWNERS or required review;
- workflow permissions or Environment bindings;
- OIDC trust;
- destination identity or permissions;
- Buddy/Official isolation; or
- Break-Glass Remediation governance.

Ordinary CI and Release runs do not continuously compare platform configuration
against a stored snapshot.

## Failure Conditions

Governance integration is not ready for activation when:

- required control surfaces lack owner review;
- a build or qualification job can obtain publication capability;
- Buddy and Official share an identity capable of reaching Official state;
- an Official side-effect job is not protected by the intended Environment;
- destination trust accepts broader workflow identities than intended;
- required platform configuration cannot be inspected or confirmed; or
- a required acceptance item remains unresolved.

## Deferred LLD Decisions

- exact Environment names;
- exact CODEOWNERS patterns;
- exact Ruleset configuration;
- exact GitHub job permissions by destination;
- exact OIDC audience and subject claims;
- destination-specific trusted-publisher setup;
- rollout checklist command sequence; and
- safe smoke identities and cleanup procedures.
