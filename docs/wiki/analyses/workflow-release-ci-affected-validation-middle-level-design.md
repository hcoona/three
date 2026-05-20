# Workflow Release CI Affected Validation Middle-Level Design

## Purpose

This page records the middle-level design for CI affected validation as a
workflow-release entry point. It refines
[Workflow Release CI Affected Validation Requirements](./workflow-release-ci-affected-validation-requirements.md)
and
[Workflow Release CI Affected Validation High-Level Design](./workflow-release-ci-affected-validation-high-level-design.md)
into frozen cross-component contracts.

This page intentionally avoids lower-level implementation detail. It does not
define concrete GitHub Actions YAML, exact path glob tables, script or helper
module names, private executor APIs, command lines, storage paths, or complete
JSON Schema syntax.

## Design Inputs

The middle-level design preserves these signed-off high-level decisions:

- CI affected validation is part of workflow-release, not an independent CI
  system.
- CI emits a sibling validation plan, not a release plan with publication
  disabled.
- The validation plan is fully materialized and execution-authoritative.
- Planning owns classification, selected subjects, downstream expansion,
  descriptor-validation scope, validation obligations, and fail-closed outcomes.
- Execution consumes the validation plan and must not recompute planning policy.
- Descriptor-backed projects validate release-shaped artifacts and receipts for
  the union of all declared profiles without publication side effects.
- Validation-only subjects participate in CI validation without becoming publish
  subjects.
- CI evidence and release immutable proof remain strictly separated.
- Policy-bearing CI planning changes may be planned by the validation-tree policy
  being reviewed, but still receive no publication authority or release
  credentials.

## Middle-Level Design Summary

The CI affected validation entry point is split into these contracts:

1. **Control-plane CI request contract** — normalizes event inputs into a
   planner-facing request containing the CI mode, validation tree, and confirmed
   affected range or scheduled-full marker.
2. **Fact-provider contract** — ecosystem providers expose bounded discovery and
   dependency facts without owning cross-ecosystem policy.
3. **Classification and scope contract** — the planner classifies changed files,
   resolves affected subjects, expands broad scopes, and emits fail-closed
   outcomes when scope cannot be confirmed.
4. **Validation-plan contract** — the planner emits an inspectable,
   execution-authoritative plan with provenance, subject snapshots, validation
   obligations, work groups, diagnostics, and expected evidence.
5. **Execution handoff contract** — control-plane jobs and executors consume plan
   selectors and produce validation-only evidence without replanning or
   publication side effects.
6. **HK left-shift contract** — local checks may mirror lightweight planning
   decisions, but HK success never replaces CI validation evidence.

## Control-Plane CI Request Contract

The control plane owns event normalization before planning. It produces one
planner-facing CI request for each run.

The request has exactly one mode:

- `pull_request` affected validation;
- `push` affected validation;
- `scheduled_full` validation.

For affected modes, the request includes:

- the validation-tree commit snapshot;
- the confirmed base and head commits, or pushed range endpoints;
- the normalized changed-file list derived from that range;
- the event identity needed for diagnostics and evidence correlation.

For scheduled full mode, the request includes:

- the scheduled validation-tree commit snapshot;
- an explicit scheduled-full marker;
- no changed-file set and no affected range.

If the control plane cannot establish a confirmed affected range for
`pull_request` or `push`, it must request a fail-closed planning result rather
than fabricating a partial changed-file set.

The request must not carry publication credentials, release approval state, OIDC
publish permission, or publish-environment secrets. Read-only repository metadata
needed for event normalization may remain control-plane owned.

## Validation Tree and Policy Changes

The validation tree is the repository snapshot used by the planner for
classification policy, fact collection, descriptor interpretation, and validation
subject discovery.

When a change modifies policy-bearing CI planning code, such as planner,
classifier, or fact-provider implementation, the request still uses the
validation tree under review. This validates the changed policy by its resulting
planning behavior. The plan must make that provenance inspectable so operators can
see that the validation-tree policy produced the authoritative plan.

This contract does not add a separate baseline planning pass. It also does not
relax safety constraints: the changed policy must produce a confirmed validation
scope or a fail-closed outcome, and the run must not gain publication credentials
or release authority.

## Fact-Provider Contract

Ecosystem fact providers contribute bounded facts to the planner. They do not
decide CI scope, broad-impact expansion, descriptor-validation policy, or
fail-closed behavior.

Each fact provider may report:

- active validation subjects discovered from ecosystem workspace or solution
  metadata under active monorepo roots;
- descriptor-backed release-capable subjects in that ecosystem;
- validation-only subjects and their explicit inclusion or exclusion basis;
- project roots and ownership boundaries;
- dependency edges that support downstream expansion;
- ecosystem-level configuration files that can affect multiple subjects;
- available validation capabilities, such as build, test, lint, format, type
  check, and release-shaped artifact validation support;
- runner-family expectations when they are ecosystem-level facts.

The provider output is a fact snapshot. The planner records the snapshot identity
or equivalent provenance in the validation plan. If a provider cannot produce
facts sufficient for the requested scope, planning fails closed.

Providers report available ecosystem facts and tooling capabilities. The planner
remains the sole owner of normalized validation-subject capability-class
assignment and final validation obligations.

Fact providers must not perform build, test, packaging, release-shaped artifact
validation, publication, or remote publish-state observation as part of fact
collection.

## Validation Subject Universe Contract

The planner owns the normalized validation subject universe for each plan.

Each validation subject belongs to one of these capability classes:

- **descriptor-backed release-capable subject** — has a release descriptor and may
  carry release-shaped artifact obligations;
- **validation-only subject** — participates in CI validation but is not eligible
  for publication.

Every subject snapshot in the plan records:

- stable subject identity;
- ecosystem identity;
- project root or owning path set;
- capability class;
- whether a release descriptor is attached;
- applicable validation capabilities;
- inclusion source, such as descriptor discovery or ecosystem workspace metadata;
- explicit exclusion status when a discovered candidate is not active.

Release descriptors grant release capability, but they do not define the whole CI
validation universe. Validation-only subjects remain first-class validation
subjects and must never be transformed into publish subjects by planning or
execution.

## Change Classification Contract

The classifier produces a set of impact records for the normalized changed-file
list. It does not have to collapse the run into a single category before scope
resolution.

The supported impact categories are:

- project-scoped;
- ecosystem-scoped;
- workflow-release infrastructure;
- global;
- known non-impacting;
- unknown.

Each impact record includes:

- category;
- matched path or path group;
- affected ecosystem, subject, tooling surface, or global marker when known;
- rationale suitable for plan inspection;
- whether the impact requires descriptor validation;
- whether the impact requires downstream expansion or broad expansion.

Scope resolution applies these precedence rules:

1. Any unknown or unclassifiable impact produces a fail-closed planning outcome.
2. Global impacts select scheduled-full-equivalent validation scope.
3. Workflow-release infrastructure impacts select the affected tooling surface,
   related ecosystems, affected validation subjects, and required descriptor
   validation obligations.
4. Ecosystem impacts select all active validation subjects in the affected
   ecosystem.
5. Project impacts select directly affected subjects plus downstream dependents
   when safe.
6. A lightweight-only known non-impacting plan is allowed only when every changed
   path is classified as known non-impacting.

Mixed-impact changes use the union of all selected scopes and obligations, unless
an unknown or unclassifiable impact forces fail-closed planning. Lightweight
checks may be added to a mixed plan, but they must not replace project,
ecosystem, global, or workflow-release infrastructure validation. Broader
selected scopes subsume narrower selected scopes when they cover the same subject
or ecosystem, so the planner does not need to duplicate equivalent obligations.

## Scope Resolution Contract

### Project-Scoped Changes

For project-scoped changes, the planner selects:

- directly changed validation subjects;
- downstream dependent subjects when dependency facts can compute downstream
  impact safely;
- descriptor validation for affected descriptor-backed subjects;
- ecosystem gates that apply to every selected subject.

If downstream computation is unavailable because an ecosystem lacks an approved
dependency fact provider, the planner may expand to the requirement-approved
ecosystem scope for that ecosystem. If an expected dependency fact provider
cannot read or parse required metadata, the planner emits a fail-closed planning
outcome rather than expanding from incomplete facts. It must not silently
validate only the direct subject when downstream impact may exist.

### Ecosystem-Scoped Changes

For ecosystem-scoped changes, the planner selects:

- all active validation subjects in the affected ecosystem;
- descriptors for descriptor-backed projects in that ecosystem;
- ecosystem gates applicable to the selected subjects.

Examples include workspace configuration, lock files, shared build configuration,
package-management configuration, and ecosystem tool configuration. The exact path
table remains lower-level design.

### Workflow-Release Infrastructure Changes

For workflow-release infrastructure changes, the planner selects:

- the affected workflow-release tooling surface;
- affected validation subjects from the unified validation subject universe;
- related ecosystems and affected subjects when multiple ecosystems or artifact
  kinds can be affected;
- all discovered release descriptors when the change can affect descriptor
  semantics, authoring validation, planning, contracts, build execution, publish
  execution, or smoke validation.

Descriptor schema documentation changes are workflow-release infrastructure
changes. They validate the affected tooling surface and participate in the same
classification rules as other infrastructure changes.

Representative smoke coverage may be used as additional evidence, but it does not
substitute for broader validation of related ecosystems and affected validation
subjects. If the affected tooling surface or validation subject set cannot be
classified safely, planning fails closed.

### Global Changes

Known global changes select the same validation scope as scheduled full
validation:

- all active validation subjects in all ecosystems;
- all discovered release descriptors;
- all applicable ecosystem gates;
- release-shaped artifact and receipt validation for descriptor-backed projects;
- relevant workflow-release tooling validation.

The plan preserves provenance that distinguishes global affected validation from
scheduled execution, even though the selected validation scope is
scheduled-full-equivalent.

### Scheduled Full Validation

Scheduled full validation bypasses changed-file classification. It selects the
full repository validation scope at the scheduled validation-tree snapshot:

- all active validation subjects in all ecosystems;
- all discovered release descriptors;
- all applicable ecosystem gates;
- release-shaped artifact and receipt validation for descriptor-backed projects;
- relevant workflow-release tooling validation.

### Known Non-Impacting Changes

Known non-impacting changes produce an inspectable lightweight plan. The plan
records:

- the matched non-impacting rules;
- any lightweight checks that still apply;
- that no heavy validation subject was selected;
- why the run was not silently skipped.

If any changed path is classified as project-scoped, ecosystem-scoped,
workflow-release infrastructure, global, or unknown, the run cannot use the
lightweight-only plan path. Lightweight checks may still appear as additional
obligations in a mixed plan.

### Fail-Closed Outcomes

Fail-closed is a planning result, not an execution result. A fail-closed plan
records:

- CI mode and validation-tree provenance;
- affected range provenance when applicable;
- the classification or fact-collection failure;
- diagnostics explaining why no executable validation plan was authorized.

Execution must not run validation work from a fail-closed plan.

A fail-closed plan produces a failing CI validation outcome. The control plane may
still publish the fail-closed plan and diagnostics for inspection, but it must not
convert that diagnostic artifact into a passing validation result.

## Validation Plan Contract

The validation plan is the single authoritative handoff from planning to
execution.

The plan has these logical sections:

1. **Envelope**
    - plan identity;
    - CI mode;
    - validation-tree commit;
    - affected range or scheduled-full marker;
    - planner version or policy provenance;
    - validation subject universe snapshot identity.
2. **Classification**
    - changed-file impact records for affected modes;
    - scheduled-full selection marker for scheduled mode;
    - known non-impacting selections;
    - broad-scope expansion reasons;
    - fail-closed diagnostics when applicable.
3. **Subject snapshots**
    - discovered validation subjects;
    - selected or excluded status;
    - descriptor-backed capability;
    - validation-only capability;
    - ecosystem ownership;
    - inclusion or exclusion basis;
    - applicable validation capabilities.
4. **Descriptor obligations**
    - descriptor-backed subjects whose descriptors must validate;
    - all-discovered descriptor obligations for global or infrastructure scopes;
    - invalid descriptor handling expectations.
5. **Validation obligations**
    - ecosystem gate obligations;
    - release-shaped artifact obligations;
    - workflow-release tooling-surface obligations;
    - lightweight-only obligations when applicable.
6. **Work groups**
    - stable selectors for execution fan-out;
    - runner-family expectations;
    - dependency ordering between work groups.
7. **Evidence expectations**
    - receipt or evidence categories expected from each executable work group;
    - validation-only provenance fields;
    - aggregation expectations.
8. **Diagnostics**
    - planner-owned human-readable reasons;
    - stable diagnostic families sufficient for reporting and tests;
    - non-authorizing fail-closed reasons where applicable.

The exact JSON representation, field spelling, and schema file location are
lower-level design. The logical sections and ownership boundaries above are
middle-level contracts.

## Work Group Contract

The planner groups validation work into execution selectors without deciding the
exact job topology.

Supported work group kinds are:

- **lightweight preflight** — policy, formatting, or documentation checks that are
  safe to run without heavy build/test work;
- **ecosystem gate** — build, test, lint, format, or type-check obligations for
  selected subjects;
- **release-shaped build validation** — artifact-shape and receipt obligations
  for descriptor-backed subjects;
- **descriptor validation** — descriptor authoring and semantics checks;
- **workflow-release tooling validation** — checks for planner, contracts,
  descriptor schema documentation, target catalog behavior, workflow
  orchestration, build execution, publish execution, or smoke validation surfaces;
- **evidence aggregation** — final collection and reporting of validation-only
  evidence.

Each executable validation work group has:

- stable selector identity;
- coverage target, such as subject, descriptor, tooling surface, or artifact
  obligation;
- required capability family;
- runner-family expectation where known;
- input plan identity;
- expected evidence category.

Work group selectors are not command lines, GitHub Actions job names, matrix
rows, or runner allocations. The control plane may map selectors to concrete
jobs in lower-level design, including by batching multiple compatible selectors
into one concrete job, but it must preserve selector semantics and plan
authority.

Evidence aggregation is a terminal control-plane work group. It collects and
reports validation-only evidence, emits the aggregate verdict artifact, and is
not a normal executable validation work group.

## Release-Shaped Artifact Validation Contract

For each selected descriptor-backed subject, the planner derives release-shaped
artifact obligations from the same descriptor and artifact model used by `buddy`
and `official`.

If a descriptor is invalid enough that the planner cannot derive subject,
descriptor-validation, or release-shaped artifact obligations, planning fails
closed. If the planner can still materialize explicit descriptor-validation work,
then descriptor-validation failure is a blocking validation failure. Execution
must not claim release-shaped artifact coverage for an invalid descriptor.

CI artifact validation covers the union of artifacts required by all declared
profiles. It does not select publish nodes, does not run publication, and does not
observe remote publish state.

Each release-shaped artifact obligation records:

- owning validation subject;
- descriptor identity;
- artifact slot identity compatible with release plan semantics;
- kind family, concrete kind, and logical artifact role;
- profile coverage basis;
- expected validation evidence.

Execution may produce unsigned or credential-free validation artifacts when
release-only credentials or side effects would otherwise be required. If artifact
shape cannot be confirmed without release-only credentials or side effects, the
corresponding work group records a blocking validation failure rather than
claiming release equivalence.

CI artifact receipts are validation-only evidence and are ineligible for
immutable publish-proof lookup or publication admissibility.

## Execution Handoff Contract

Execution consumes only the validation plan and referenced repository snapshot.

Executors and post-planning jobs must not:

- recompute changed-file classification;
- rediscover selected validation subjects;
- change downstream expansion;
- alter descriptor-validation scope;
- add or remove validation obligations;
- derive publish intent;
- use CI evidence as release immutable proof;
- query remote publish destinations to decide validation scope.

The control plane may fan out or batch work groups by ecosystem, runner family,
dependency layer, capability, or artifact obligation. That mapping is
lower-level design, but it must preserve:

- .NET validation on Windows runners in GitHub Actions;
- Python and JavaScript/TypeScript validation may use Ubuntu runners when
  applicable;
- `mise` as the preferred toolchain provisioning path;
- no publication credentials, release privileges, or OIDC publish permissions for
  CI planning, fact collection, or validation execution;
- the ability to report a distinct outcome and expected evidence for every
  required logical work group;
- dependency ordering between logical work groups, whether enforced by separate
  jobs, batched executor ordering, or another lower-level mechanism.

When provider-reported runner facts conflict with these repository-level runner
expectations, the repository-level expectations take precedence.

Publication nodes, remote registry pushes, GitHub Release creation, release tag
creation or movement, and official release approvals are outside CI validation
execution.

## Validation Evidence Contract

CI validation evidence is scoped to one validation plan.

Each evidence item records:

- validation plan identity;
- CI mode;
- validation-tree commit;
- affected range or scheduled-full marker when applicable;
- work group selector;
- subject, descriptor, or tooling surface covered;
- outcome;
- produced validation artifact reference when applicable;
- diagnostic reference on failure.

Evidence may be used by later CI jobs and operators to understand validation
results. It must not be accepted by release proof lookup, `buddy` publication, or
`official` publication.

One concrete execution job may produce evidence for multiple logical work
groups, provided each evidence item remains bound to its work group selector and
the aggregate step can still detect missing evidence, unexpected evidence, and
blocking failures per required work group.

Evidence aggregation reports:

- all required work groups completed, failed, skipped by lightweight plan, or were
  not authorized due to fail-closed planning;
- any blocking validation failures;
- missing evidence for required work groups;
- plan provenance sufficient to reproduce the selected scope.

The aggregated CI validation outcome fails when planning fails closed, required
evidence is missing, or any work group records a blocking validation failure. It
passes only when every required work group completes successfully with the
expected validation evidence, or when a lightweight-only plan has no executable
lightweight obligations. If a lightweight-only plan includes executable
lightweight work groups, those work groups must complete successfully with the
expected validation evidence.

The exact receipt file format and artifact upload naming are lower-level design.

## HK Left-Shift Contract

HK integration is limited to planner-aligned lightweight preflight.

HK may use:

- the same high-level classification categories;
- lightweight non-impacting decisions;
- local formatting, linting, or policy checks that are fast enough for ordinary
  development;
- explicit user-selected heavy checks when supported by HK profiles.

HK must not:

- replace CI validation evidence;
- claim scheduled-full-equivalent validation;
- run release-shaped artifact production by default;
- require local publication credentials;
- turn ordinary local hooks into the full CI execution layer.

The exact HK profile names, step lists, and command mapping remain lower-level
design.

## Diagnostic Contract

Planner diagnostics are part of the plan contract because fail-closed and
lightweight outcomes must be inspectable.

The middle-level fail-closed or blocking-failure diagnostic families are:

- `range-unconfirmed`;
- `unknown-change`;
- `subject-unresolved`;
- `dependency-impact-insufficient`;
- `fact-provider-insufficient`;
- `infrastructure-surface-unclassified`;
- `descriptor-invalid`;
- `artifact-shape-unconfirmed`.

The middle-level inspectable non-failure diagnostic family is:

- `known-non-impacting`.

Lower-level design may define exact code spelling and subcodes, but it must keep
diagnostics stable enough for tests, reporting, and operator review.

## Lower-Level Deferrals

The following remain deliberately outside this middle-level design:

- exact validation-plan JSON Schema;
- exact path classification table and glob patterns;
- exact dependency-closure algorithms;
- exact GitHub Actions workflow files, job names, matrices, and concurrency
  groups;
- exact executor APIs and helper module layout;
- exact command lines for .NET, Python, JavaScript/TypeScript, HK, and descriptor
  validation;
- exact evidence file names, artifact storage layout, and retention settings;
- exact HK profiles and hook-step ordering.

An experienced implementer may choose these details as long as the contracts in
this page, the requirements, and the HLD remain unchanged.

## Requirements and HLD Traceability

| Topic                           | Requirement / HLD source                                                                | Middle-level contract                                                                                                     |
| ------------------------------- | --------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------- |
| Workflow-release ownership      | CI belongs to workflow-release, not a separate CI system                                | Shared control-plane, planner, fact-provider, plan, execution, and evidence contracts                                     |
| Affected and scheduled modes    | PR, push, and scheduled full validation are required                                    | Single CI request contract with three modes                                                                               |
| Conservative classification     | Unknown changes fail planning closed                                                    | Classification precedence and fail-closed plan contract                                                                   |
| Active project participation    | All active build/test projects participate, including non-releasable subjects           | Unified validation subject universe with descriptor-backed and validation-only capabilities                               |
| Project downstream impact       | Downstream dependents included when safely computable                                   | Project-scoped scope resolution with fail-closed behavior when downstream impact is unavailable                           |
| Ecosystem scope                 | Ecosystem changes validate all active projects in that ecosystem                        | Ecosystem-scoped scope resolution                                                                                         |
| Workflow-release infrastructure | Infrastructure changes validate affected tooling surface and descriptors where required | Infrastructure scope resolution across tooling surface, related ecosystems, affected subjects, and descriptor obligations |
| Release-shaped validation       | Descriptor-backed projects validate union of all profile artifacts without publication  | Release-shaped artifact validation obligations and blocking validation failure for unconfirmed shape                      |
| Evidence separation             | CI evidence is not release proof                                                        | Validation-only evidence contract and executor prohibitions                                                               |
| HK left-shift                   | HK provides lightweight local feedback only                                             | HK left-shift contract                                                                                                    |

## Outcome

This middle-level design freezes the cross-component contracts needed before
lower-level design: CI request normalization, fact-provider boundaries,
classification and scope resolution, validation subject snapshots, validation
plan sections, work group selectors, release-shaped validation obligations,
execution handoff, evidence separation, HK left-shift, and diagnostic families.
It remains aligned with the locked requirements and HLD while leaving concrete
workflow files, scripts, schemas, command mappings, and internal implementation
structure to lower-level design.
