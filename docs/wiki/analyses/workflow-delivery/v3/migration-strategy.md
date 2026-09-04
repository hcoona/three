# Workflow Delivery v3 Migration and Document Policy

## Decision

Workflow Delivery v3 remains the only normative design line. Proven v2
mechanisms may be extracted and revalidated behind v3 boundaries, but v2
authority, identity, profile, promotion, and replay semantics are not an
incremental implementation base.

This page defines the current transition policy. Git history and the append-only
[`docs/wiki/log.md`](../../../log.md) carry completed chronology. Current-state
documents describe current truth rather than replaying prior provisioning,
retry, or rollout sequences.

## Current State

The normal-Live implementation is merged and disabled with
`live_enabled: false`.

Both permanent Environments created during prior provisioning currently exist:

- `workflow-delivery-v3-buddy-approval`; and
- `workflow-delivery-v3-buddy-github-packages`.

The replacement design retains only
`workflow-delivery-v3-buddy-approval` as an authority-bearing Environment.
`workflow-delivery-v3-buddy-github-packages` is inert under that design, but it
must not be deleted until:

1. the replacement implementation is merged and exact repository inspection
   proves that no workflow, executable source, schema, policy, formatter,
   validator, or test treats it as an input or authority; current-state and
   migration text may still name it solely to inventory and remove it safely;
   and
2. separate authorization permits external-resource cleanup.

The direct v1 Buddy-to-v3 Buddy cutover and destination acceptance are complete
historical facts. They do not authorize normal Live activation.

Normal Live also remains activation-blocked until the pinned standard
`npm publish --tag ... --fetch-retries=0` operation profile passes the
documented-and-observable native acceptance suite and fresh protected
Governance binds that acceptance generation. The gate proves non-overwriting
creation of the authoritative exact version and characterizes the accepted
non-authoritative tag race. It also proves, with separately authorized
acceptance-only package-admin credentials, that the pinned operation cannot
reuse or alter a deleted/restorable same-version slot and that the original
object can be restored with exact bytes and witness. It does not require
unavailable atomic version-plus-tag CAS or grant those credentials to runtime.

## Why v2 Is Not an Incremental Base

v2 and v3 differ at architectural boundaries:

- external GitHub Governance and same-revision, context-owned planning replace
  v2 promotion authority;
- explicit Release Units over normalized repository facts replace
  project/profile-centric control types;
- Qualification and Publication Snapshots replace one mutable pre-build plan;
- NBGV remains sole product-version authority while channels retain separate
  destination and capability boundaries; and
- current-Attempt records plus fresh destination observation replace
  history-derived admission and aggregate replay state.

Mixing those contracts would create an intermediate architecture with
ambiguous authority and recovery semantics.

## Replacement Delivery Order

The replacement is delivered in this order:

1. Merge the coherent design-document changes only.
2. Implement the runtime and static-policy contraction while
   `live_enabled: false`, including migration to exact Governance schema
   `workflow-delivery/v3/normal-live-governance-attestation-v2`. V2 replaces
   the disabled v1 contract because native destination acceptance has a
   different closed field set; no v1 admission alias is retained. The migrated
   document uses the closed object `{"state":"blocked"}` with no fabricated
   native evidence.
3. Run the complete affected tests and HK gates, then complete independent
   multi-reviewer review and atomic adjudication. Compatibility fixtures must
   prove superseded selected-ref parsers reject the new schema before any
   Environment job.
4. Merge the validated implementation while it remains disabled.
5. Separately authorize removal of
   `workflow-delivery-v3-buddy-github-packages` only after exact no-authority-
   reference proof, authenticated Environment readback, and repository
   inspection proving every retained dispatchable ref either implements the
   one-Environment contract or rejects the new Governance schema before any
   Environment job or deployment.
6. Before activation, execute the separately authorized native acceptance
   suite against a pre-existing disposable package and prove the pinned
   standard `npm publish --tag ... --fetch-retries=0` profile satisfies the
   authoritative exact-version non-overwrite contract and the bounded
   non-authoritative tag-race model. The suite must also publish, delete,
   republish-test, and restore a fresh disposable version to prove hidden
   deleted/restorable state cannot be reused or altered and that original bytes
   and witness survive restoration. A replacement primitive is required only
   if that acceptance fails.
7. Gather fresh at-most-90-day Governance and native-platform evidence,
   explicitly covering the one Approval Environment, the accepted residual
   package reach, and authenticated repository Actions retention of at least
   45 days, without merging a separate preparation change.
8. Merge one small protected Activation PR that applies the refreshed
   attestation and sets `live_enabled: true`.
9. Perform authenticated post-merge readback of the protected Governance,
   repository retention, and native platform state.
10. Dispatch exactly once from then-current protected `main` through an
    explicitly supported REST API version whose success response returns the
    run ID. Validate the returned workflow and run identity, actor,
    `workflow_dispatch` event, actual head SHA, `refs/heads/main`, and
    `github.run_attempt == 1`. A lost response or ambiguous correlation requires
    read-only operator investigation and native run lookup and never blind
    redispatch.
11. Request human approval only when the Publication Snapshot contains an
    action. Complete read-only best-effort finalization and destination
    readback. Any activation failure remains fail closed.

There is no separate Preparation PR, repository-wide `main` freeze,
pre-pinned Activation SHA, or activation tag.

The first proving run starts from then-current protected `main`. Later normal
Buddy operation retains the approved ability to select arbitrary
same-repository refs under the accepted writer trusted-computing base when
their selected-revision control strictly admits the active Governance schema.

## Implementation-Line Strategy

### Static-Reference Policy Contraction

The implementation phase introduces a new schema and policy ID. It must:

- use exact `git-target` enumeration and blob reads for Release Live
  Eligibility;
- keep `index` stage-0 and `worktree` tracked-plus-eligible-untracked modes as
  separate HK feedback sources;
- run the lightweight policy whenever root HK runs in the caller-selected
  feedback mode;
- preserve the expensive v3 pytest suite as path-selected, except that manual
  `slice-validation` runs it unconditionally;
- remove Tree-sitter and every handwritten ecosystem grammar, lexer, locator
  splitter, and competing-authority hardening layer;
- introduce one exact Ecosystem Authority Graph per retained selector,
  composed only from authoritative source artifacts, stable official libraries
  or CLIs, and published standards, and bind its manifest into the policy
  digest;
- remove npm, uv, and Yarn locks, unevaluated MSBuild project/central manifests,
  standalone Python manifests, shell and PowerShell scripts, GitHub
  workflow/composite-action files, and Node import-subpath claims from the first
  slice rather than filling missing authority with local grammars, adding a
  command-string classifier, or adding a cross-platform filesystem sandbox;
- use official pnpm lock/workspace readers only against their declared isolated
  snapshots, followed by public pure dependency-path, lockfile-resolution,
  workspace-specifier, and registry-specifier helpers; fail closed on
  unsupported non-workspace link/path-local forms instead of invoking the
  filesystem-reading local resolver;
- before enabling the root gate, change the tracked
  `src/public/lib/hexo-renderer-asciidoc/examples/hexo-site/package.json`
  dependency on `hexo-renderer-asciidoc` from unsupported `link:../..` to
  admitted `file:../..` and regenerate that example's `pnpm-lock.yaml` with the
  repository-pinned pnpm so both selected artifacts use the typed file-directory
  projection; do not add a selector exception for the example;
- replace the tracked
  `src/public/lib/three-workflow-delivery-v3/tests/fixtures/acceptance/npm-publish-request/package/package.json`
  fixture source with a non-candidate basename and materialize its exact bytes
  as `package/package.json` only under test-owned temporary storage; remove the
  superseded fixture-path whole-file exception rather than carrying it into the
  new policy;
- remove selectors that lack a stable, proportionate, exact-source authority
  projection rather than retaining a local compatibility grammar;
- keep evaluation, dataflow, package installation, network access, fallback
  file reads, and candidate execution outside authority adapters;
- remove whole-file digest exceptions, fixed inventory counts,
  scanned-surface digest authority, and trigger-catalog authority; and
- validate exact target, policy, source-kind, normalized adapter facts, and
  finding behavior through semantic tests rather than foreign-parser-branch or
  fixed-count assertions.

Those are implementation-phase changes. This documentation-only change does
not modify the scanner, HK configuration, workflows, or tests.

### Normal-Live Runtime Contraction

The implementation phase removes these normal-Live mechanisms:

- custom GitHub Actions history discovery and admission;
- prior-Attempt reconstruction and history-derived aggregate Execution state;
- `github.run_attempt` fields from normal-Live Provider Request Manifests, Fact
  Bundles, Repository Model, Qualification, and Publication Snapshots,
  current-Attempt records, Artifact References, and Publication Authorization;
- the Capability Environment and Environment Profile abstraction;
- `approval-finalizer` and Capability Admission;
- capability groups, group manifests, group bundles, and group result bundles;
  and
- mandatory approval for a zero-action exact-satisfied Attempt.

It also replaces the current disabled
`workflow-delivery/v3/normal-live-governance-attestation-v1` schema with the
exact incompatible schema
`workflow-delivery/v3/normal-live-governance-attestation-v2`.
Selected-revision control must reject v1 and every other schema before Release
Execution lookup, Attempt creation, or any Environment job. A retained fixture
of known stale dispatchable control must prove this negative path.

Every authoritative normal-Live job still independently requires
`github.run_attempt == 1`. Simulation retains its run-attempt binding and rerun
identity. CI retains its existing candidate and run-attempt contract.

The contraction preserves:

- purpose-first request branching and same-revision request-local Repository
  Model compilation;
- Qualification and Publication Snapshots;
- destination Observation and zero-or-one action formation;
- the semantic Publication Authorization closure for an action-bearing
  Attempt;
- Release Execution and mutable-resource concurrency boundaries;
- the mutation-may-have-started marker;
- Publication Result, including authoritative exact post-action readback on
  successful publication;
- read-only best-effort finalization, including the possibility that no durable
  Attempt Outcome survives cancellation or transport failure; and
- read-only operator investigation using native run lookup after ambiguous
  dispatch, without blind redispatch, plus the architectural boundaries for
  future formal Release Reconciliation and separately authorized Break-Glass
  Remediation. The first slice implements neither runtime workflow nor a
  Reconciliation Record.

## External-State Inventory

### Environment State

The replacement authority model has one Environment:
`workflow-delivery-v3-buddy-approval`, with the approved reviewer,
self-review, bypass, deployment-policy, wait, variable, secret, and exact
Environment-scoped sentinel settings.

Authenticated evidence must prove that no same-name broader variable can
satisfy the lookup in place of the Environment-scoped sentinel. The runtime
sentinel remains a narrow accidental-creation and misbinding check; it does not
replace native configuration readback.

The obsolete Capability Environment remains untouched until the ordered cleanup
gate above is separately authorized.

### GitHub Packages Access

This transition does not change package access.

The GitHub Packages credential principal is repository `hcoona/three`. Its
known reach includes the real `hexo-renderer-asciidoc` package and disposable
smoke packages. That reach is an accepted repository-principal blast radius,
not package isolation and not an exhaustive package inventory.

Exact smoke coordinate, artifact, action, and mutable-resource validation
governs intended operation and reconciliation only. It does not constrain a
malicious accepted writer or narrow the repository token to one package.
Official npmjs PAT, OIDC, secret, destination, CI, and simulation boundaries
remain unchanged.

### Governance Freshness

The protected Governance source remains repository `hcoona/three`, ref
`refs/heads/main`, and path
`.github/workflow-delivery/governance/hcoona-release-smoke-npm.json`.

Eligibility and fresh checks bind repository, ref, path, and attestation
blob/content identity or an explicit monotonically governed generation. They
do not require equality of the complete resolved `main` commit.

Path-touch anti-rollback is mandatory: any commit touching the protected path
after eligibility invalidates the Attempt, including a change followed by a
byte-for-byte revert. Restoration requires a new dispatch and Attempt.

## Code and Test Selection

Mechanism code may be retained only when it conforms to the current v3
contracts. The implementation contraction must preserve Repository Model
Providers, Fact Bundles, Build Definitions, Release Units, NBGV authority,
purpose isolation, CI and Official behavior, simulation identity, and
destination and remediation boundaries.

Tests should assert semantic outcomes and exact binding failures. They must not
freeze non-authoritative job topology, shell choreography, parser branches,
trigger inventory, or file/surface/finding counts.

## Documentation Selection

Current v3 requirements, HLD, glossary, MLDs, and concise transition policy are
normative. Archived v1 and v2 material may supply mechanism evidence only when
the v3 documents explicitly require extraction and revalidation.

`docs/wiki/log.md` remains append-only. Historical provisioning and retry facts
belong in Git and that log; current-state pages stay focused on current
architecture, external state, residual risk, and next authorized boundary.

## Explicit Non-Authorization

This design-document change does not authorize:

- workflow, source, scanner, HK, or test edits;
- deletion or modification of either Environment;
- package-access or package-permission changes;
- Governance refresh or attestation mutation;
- `live_enabled: true`;
- activation or dispatch;
- Environment approval; or
- registry, tag, package, or other external mutation.

Each later boundary requires the separate authorization identified in the
replacement delivery order.
