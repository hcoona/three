# Workflow Delivery v3 Migration and Document Policy

## Decision

Build v3 on a clean implementation line and selectively port proven v2
mechanisms. Do not refactor the v2 control architecture in place.

v1 remains the production compatibility baseline for CI and Official. Its
Buddy routes retire when the first-slice implementation PR merges, before v3
live activation. v2 never becomes an intermediate production architecture.

## Why v2 Is Not an Incremental Base

v2 and v3 differ at their architectural roots:

- v2 authority and promotion machinery versus GitHub-governed same-revision
  context-owned planning and finalization;
- project/profile-centric control types versus an explicit Release Unit domain
  over normalized project and build facts;
- one pre-build Release Plan versus a two-snapshot Plan lineage;
- v2 shared publication identity versus v3 channel- and destination-isolated
  publication coordinates over one NBGV-authoritative product version;
- mixed control and execution boundaries versus three runtime trust zones; and
- GitHub job rerun semantics versus whole-release replay.

Changing these in place would create long-lived intermediate states that mix
incompatible authority, identity, Evidence, and replay contracts.

## Implementation-Line Strategy

1. Preserve the v2 commit as the full archive and mechanism source.
2. Create the v3 branch from the current repository mainline for a clean diff;
   this is a Git baseline choice, not architectural reuse of v1.
3. Port this versioned v3 documentation first.
4. Create new v3 CI, Release, and Shared Foundation namespaces with no imports
   from v2 Plan, project, profile, proof, report, or control-plane types.
5. Port mechanisms through anti-corruption adapters.
6. Implement one vertical slice before expanding across ecosystems.
7. Keep v3 live disabled until acceptance completes. For the confirmed first
   slice, the implementation PR merge directly retires both legacy Buddy
   workflow identities before destination acceptance, creating a controlled
   outage; required CI checks, v1 Official, and v1 CI do not switch.

## Documentation Selection

### Port

- v3 requirements, HLD, glossary, and migration decisions;
- current repository facts required to discover Project Nodes, dependencies,
  build capabilities, and Release Units;
- revalidated GitHub Actions and Registry platform observations;
- mechanism behavior needed to specify adapter contracts; and
- new v3 acceptance evidence.

Ported version mechanisms must be adapted to the v3 target-bound Repository
Model projection contract. In-build NBGV recomputation, alternative version
derivation, and fallback fields are implementation facts to replace, not
semantics to preserve.

### Rewrite

- product and system requirements when the accepted baseline changes;
- Project Node discovery and Release Unit authoring;
- CI Qualification MLD and brief LLD;
- Release Delivery MLD and brief LLD;
- authority and governance MLD and brief LLD;
- operator runbooks; and
- implementation and rollout plans.

### Do Not Port

- v2 normative requirements and design pages;
- v2 implementation completion records;
- v2 rollout readiness claims;
- v2 wiki overview and index;
- v2 workflow documentation as active guidance; and
- the v2 wiki log as the v3 active chronology.

### Extract and Revalidate

Platform experiment pages must be rewritten as version-neutral observations.
The extracted page must distinguish:

- observed platform behavior;
- observation date and workflow/run evidence;
- assumptions that may expire;
- v2-specific interpretation; and
- the new v3 consequence.

## Code and Test Selection

Mechanism code may be ported when it can be expressed behind a v3 adapter
without importing v2 domain types.

Mechanism-level tests and fixtures may be ported with the code. Tests that assert
v2 workflow topology, schema shape, project identity, Buddy promotion, or
candidate-owned authority must remain in the v2 archive.

## External-State Inventory

Before v3 activation, inventory:

- GitHub Rulesets and required-check names;
- v1 required CI and the first-slice v3 shadow pull-request and
  non-authoritative manual `slice-validation` check names;
- protected Environments and reviewers;
- OIDC workflow identities and claims;
- Registry trusted-publisher registrations;
- GitHub Packages permissions;
- the exact dedicated `hcoona-release-smoke-npm` package and GitHub Packages
  destination;
- the separate protected Buddy Environment, reviewers, and self-review
  prevention behavior;
- maximum `GITHUB_TOKEN` package/repository reach, with proof of minimum
  `packages: write`, no PAT fallback, and no `id-token: write`;
- actual package/repository grants, known Official and production assets, and
  the bounded set of unrelated assets safe for denial probes, without claiming
  universal negative reach proof;
- every repository actor with Write, Maintain, or Admin access and explicit
  confirmation that each is trusted as a Buddy publisher;
- protected-ref non-executable TCB attestation with explicit accepted writer and
  package/repository/Manage Actions access inventory or evidence digest,
  policy/package bindings, issuer, inspection time, expiry no later than 90
  days, acknowledged limitations, exact source contract
  `hcoona/three` + `refs/heads/main` +
  `.github/workflow-delivery/governance/hcoona-release-smoke-npm.json`, and
  fixed-source ref/commit/blob/content verification;
- optional workflow-execution protections, documented only as defense in depth
  rather than a required dependency or per-job permission ceiling;
- reviewer-visible target/ref, coordinate, artifact manifest/digest, lifecycle
  scripts, and action-summary surfaces;
- all repository, CI, developer, and production dependency graphs to prove the
  disposable smoke package has no normal consumer;
- exact-target Release-owned eligibility scan surfaces, policy/catalog digests,
  exceptions, immutable Decision transport, and pre-Attempt blocking behavior;
- immediate pre-Capability `contents: read` re-resolution and re-read of the
  fixed protected Governance source, including `live_enabled`, mandatory
  new-Attempt recovery after disablement, expiry,
  source/provenance/content change, or invalidation, and optional
  publisher-side repeat validation as defense in depth only;
- permanent HK dependency-policy coverage over manifests, lockfiles, workflows,
  install scripts, and dependency configuration, including dependency-surface
  triggers and unconditional `slice-validation`;
- planned action catalogs proving no ordinary delete, restore, permission,
  visibility, or admin action;
- latent repository/package admin authority accepted within the writer TCB and
  the Break-Glass deletion/restore path;
- concurrency identities; and
- any live v1 or experimental v2 publication state;
- repository-level status of both legacy Buddy workflow identities,
  `buddy.yml` and `release-buddy.yml`, all queued/waiting/approval-pending/running
  executions, and old-ref dispatch behavior;
- temporary destination-acceptance workflow/ref/Environment, hard-bound target
  SHA and fixed probe coordinates, probe evidence, and verified removal state;
  and
- repository Actions retention policy sufficient for 45-day first-slice Release
  control and artifact retention.

Acceptance also inventories the in-package npm target witness schema and proves
build, qualification, and remote observation require coordinate, ownership,
matching immutable target witness, and bytes. Detached sidecars are not accepted
as destination provenance. It also proves explicit target-specific npm tag
syntax/length, compound publish/tag behavior, identical and differing races,
Receipt capture, and exact tag-to-version observation. Implicit `latest` and
shared moving Buddy tags are forbidden; unsupported combined behavior blocks
activation.

Activation requires explicit human Governance inspection and acceptance of the
bounded branch-controlled publisher risk. This exception is recorded only for
the first live Buddy GitHub Packages slice; Official and future Buddy
destinations or production packages remain blocked until their own governance
and threat decisions are confirmed. Any membership change that leaves an
untrusted actor with Write, Maintain, or Admin access blocks the live slice
until either that actor's repository access is reduced below
Write/Maintain/Admin or package-write Capability and destination access are
placed behind an independently enforced publisher boundary unavailable to
writer-authored workflows. Ref narrowing, Environment branch restrictions,
CODEOWNERS, and workflow-execution protections may remain defense in depth but
are insufficient remediation by themselves while an untrusted writer can
author alternate workflows with `packages: write`.

After activation, human Governance re-attests the writer TCB and
package/repository/Manage Actions access after relevant role, team, or
permission changes and at least every 90 days. Operators must immediately
respond by having an authorized human promptly commit `live_enabled: false` to
the policy-fixed protected attestation pending inspection and explicit
reacceptance; attestation expiry blocks stale normal flows independently.
Protection, review, merge, and fresh-read latency make this bounded operational
response rather than instantaneous platform disablement. A pending or completed
approval does not preserve stale Governance state: capability admission uses
`contents: read` to freshly resolve and re-read the exact source, and any change
requires a new Attempt after restoration.

The implementation PR merge is the direct v1 Buddy-to-v3 Buddy cutover and
preserves no compatibility route. It lands with `live_enabled: false` and
removes both legacy Buddy workflow files. The ordered activation procedure
freezes Buddy dispatch; disables both `buddy.yml` and `release-buddy.yml`
repository-wide; cancels or drains queued, waiting, approval-pending, and
running executions; verifies disabled state, removal, and old-ref dispatch
rejection before acceptance; runs and captures the temporary protected
acceptance probes only when every probe independently observes
`github.run_attempt == 1`; runs terminal evidence capture with
`always() && github.run_attempt == 1` so first-attempt dependency failures and
ambiguous mutation state are retained and classified incomplete or unknown for
reconciliation; rejects evidence capture on non-first attempts; removes the
acceptance workflow, bypass, and Environment and verifies removal; and only
then uses an authorized protected commit to set `live_enabled` true for the
named smoke package. Partial reruns cannot reuse the earlier review or
coordinate. A retry requires a new reviewed workflow invocation and a new fixed
disposable coordinate/version. All other former Buddy projects are unsupported
until explicitly migrated. Failed acceptance leaves all Buddy publication
disabled, removes the temporary path, keeps legacy Buddy retired, and sends any
probe state to reconciliation or Break-Glass. Restoring legacy Buddy requires a
separate user-approved rollback PR. v1 Official and CI assets remain unchanged.
That preservation explicitly excludes legacy Buddy workflows, Buddy-specific
tests and matrices, and Buddy documentation, which the direct cutover retires
or rewrites. The sequence has an intentional brief Buddy outage.

The same cutover change removes both Buddy workflow files and removes or
rewrites Buddy-only acceptance rows, node IDs, and tests. Mixed Buddy/Official
assertions are split so Official coverage remains. Negative tests prove that no
legacy Buddy route exists. Active v1 topology and rollout documentation plus
`MEMORY.md` are updated to describe retired Buddy routes. Official workflows,
CI, and shared Official/CI tests remain intact; Buddy-specific tests, matrices,
and documentation do not. Root HK must pass before merge.

During CI coexistence, the first-slice v3 pull-request check remains shadow-only
and manual `slice-validation` remains non-authoritative and slice-scoped. v1
retains the required CI decision. Canonical v3 full validation and Ruleset
cutover wait for complete repository modeling.

Parallel implementation is allowed. Parallel authoritative CI decisions or
parallel live publishers are not.
