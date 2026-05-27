# Workflow Release OIDC Publish Topology Research

## Purpose

This page records the trusted-publishing research result that must be carried
back into the workflow-release design before implementation. It focuses only on
publication topology: which GitHub Actions workflow identity an external
registry trusts when a live publish job exchanges a GitHub OIDC token for a
short-lived registry credential.

The current design must not treat external OIDC publishing as one uniform
reusable-workflow implementation detail. Registry-side trusted-publisher
configuration is part of the release contract because it decides which workflow
file, repository, environment, and reusable-workflow identity can publish.

## Current Design Decision

First delivery must include live PyPI publication. Moving live PyPI publication
to a future non-reusable workflow would leave an unacceptable structural tail:
the first implementation would prove a topology that cannot later be extended to
PyPI without changing the workflow contract.

OIDC publish topology is therefore a first-class design dimension. The design
must classify each live publish node by the trusted-publisher topology required
by its target registry, not only by target family.

## Terminology

| Term                               | Meaning                                                                                                                                                                                                     |
| ---------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| External OIDC trusted publishing   | A package registry issues a short-lived publish credential after validating a GitHub Actions OIDC token.                                                                                                    |
| Publishing workflow identity       | The workflow file and related OIDC claims that the registry accepts as authorized to publish.                                                                                                               |
| Entry-workflow-bound publishing    | The registry trust policy is configured for the top-level workflow that is directly triggered by `workflow_dispatch`, such as `official.yml`.                                                       |
| Caller-workflow-bound publishing   | The registry validates the workflow that directly called a reusable workflow, even if the publish command runs inside the called workflow.                                                                  |
| Reusable-workflow-bound publishing | The registry can explicitly trust the called reusable workflow identity, usually through GitHub's `job_workflow_ref` claim or registry-specific fields.                                                     |
| GitHub-token publishing            | Publication uses `GITHUB_TOKEN` permissions instead of an external OIDC trusted-publisher policy.                                                                                                           |
| Entry-workflow-bound publish node  | A logical publish node whose frozen `publish-topology` is `external-oidc-entry-workflow` and whose OIDC-requesting publish job must be hosted by the top-level entry workflow.                              |
| Caller-workflow-bound publish node | A logical publish node whose frozen `publish-topology` is `external-oidc-caller-workflow`; its publish job may run in a reusable workflow while the registry validates the direct caller workflow identity. |
| Entry-hosted publish job           | The concrete top-level workflow job used for an entry-workflow-bound publish node while preserving the standard `publish-request.json` and `publish-result.json` contracts.                                 |

## GitHub OIDC Claim Baseline

GitHub issues a unique OIDC token per job. The token includes standard workflow
claims such as `environment`, `repository`, `workflow_ref`, and, for jobs using
reusable workflows, `job_workflow_ref` and `job_workflow_sha`.

This matters because external registries do not all validate the same claim set.
Some registry UIs ask only for a workflow filename in the publishing repository;
some document caller-workflow behavior for `workflow_call`; RubyGems documents
extra reusable-workflow repository fields. The release design must therefore
freeze a registry-specific topology binding before implementation rather than
assuming that `.github/workflows/release-publish-node.yml` can satisfy every
external OIDC target.

## Registry Support Matrix

| Target surface  | Credential posture | Official configuration evidence                                                                                                                                            | Reusable workflow support evidence                                                                                                                                                                                                                                          | Required first-delivery topology                                                                                                                                                          |
| --------------- | ------------------ | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| GitHub Release  | `GITHUB_TOKEN`     | GitHub Release publication uses repository token permissions, not external trusted-publisher setup.                                                                        | Not applicable.                                                                                                                                                                                                                                                             | `github-token`; no registry-side workflow filename.                                                                                                                                       |
| GitHub Packages | `GITHUB_TOKEN`     | Current repository design uses package write permissions on `GITHUB_TOKEN`, not external OIDC trusted publishing.                                                          | Not applicable.                                                                                                                                                                                                                                                             | `github-token`; no external trusted-publisher topology.                                                                                                                                   |
| PyPI            | external OIDC      | PyPI GitHub Actions trusted publishers require repository owner, repository name, workflow filename, and optional environment.                                             | No PyPI documentation in the reviewed source exposes reusable-workflow or `job_workflow_ref` configuration fields. Treat live PyPI as entry-workflow-bound unless a successor design proves otherwise.                                                                      | `external-oidc-entry-workflow`; first delivery must live-publish PyPI from the workflow identity configured on PyPI.                                                                      |
| npmjs           | external OIDC      | npm trusted publishers require owner, repository, workflow filename, and optional environment.                                                                             | npm troubleshooting explicitly says that when `workflow_call` invokes another workflow that runs `npm publish`, validation checks the calling workflow's name, and `id-token: write` must be granted to both parent and child workflows.                                    | `external-oidc-entry-workflow` for first delivery, so the npmjs trusted-publisher filename remains the top-level official entry workflow rather than an ambiguous nested reusable caller. |
| NuGet.org       | external OIDC      | NuGet.org trusted publishing requires repository owner, repository, workflow file name, and optional environment; the temporary API key is one-use and valid for one hour. | The reviewed Microsoft Learn page does not document reusable-workflow-specific fields. Treat NuGet.org as entry-workflow-bound by conservative default until a successor verification proves caller-workflow-bound or reusable-workflow-bound publishing.                   | `external-oidc-entry-workflow` by conservative default.                                                                                                                                   |
| RubyGems.org    | external OIDC      | RubyGems trusted publishers require owner, repository, GitHub Actions workflow name, and optional environment.                                                             | RubyGems explicitly documents reusable workflows. When a workflow calls a reusable workflow from another repository, `job_workflow_ref` points to the reusable workflow location, and RubyGems uses optional workflow repository fields to locate the actual workflow file. | `external-oidc-reusable-workflow` is documented as possible; same-repository configuration still must be frozen before live enablement.                                                   |

## Design Consequences

### Publish topology must be explicit

The workflow design needs an explicit publish topology classifier. A target
family alone is insufficient because several target families can share the same
credential posture while requiring different trusted-publisher bindings.

Recommended current-scope topology values:

| Topology value                    | Meaning                                                                                                                  |
| --------------------------------- | ------------------------------------------------------------------------------------------------------------------------ |
| `github-token`                    | The publish job uses `GITHUB_TOKEN`; no external trusted-publisher policy exists.                                        |
| `external-oidc-entry-workflow`    | The publish job that requests the OIDC token must run in the configured top-level entry workflow.                        |
| `external-oidc-caller-workflow`   | The publish job may run in a reusable workflow, but registry validation is against the direct calling workflow identity. |
| `external-oidc-reusable-workflow` | The registry explicitly supports trusting the called reusable workflow identity.                                         |

The design can later collapse `entry-workflow` and `caller-workflow` if the
workflow implementation makes them operationally identical, but the registry
evidence should remain visible because the external setup instructions differ.

### PyPI cannot remain a topology exception

First delivery must schedule live official PyPI publish nodes through a supported
entry-workflow-bound path. `REQ_EXTERNAL_TOPOLOGY_BLOCKED` must not be the normal
first-delivery result for a valid active `pypi/pypi` official publish node.

That diagnostic may still exist for a target whose topology is unsupported, but
PyPI must move out of that category before implementation starts.

### The publish fan-out must split by topology

The existing one-publish-node abstraction can remain, but the workflow
realization cannot assume one reusable publish workflow for every live publish
node.

The orchestration layer should derive execution sets that distinguish at least:

- publish nodes that can run in the reusable publish workflow, including
  caller-workflow-bound nodes where registry validation remains tied to the
  direct caller workflow identity;
- entry-workflow-bound publish nodes that must run in the entry workflow because
  the registry requires the OIDC-requesting job there.

All topology paths should still consume the same `publish-request.json` contract
and emit the same `publish-result.json` contract. The split is a control-plane
scheduling and credential-boundary concern, not a reason to fork planner or
receipt semantics per registry.

### Keep two operator-facing entries if possible

The preferred direction is to keep only the existing `buddy` and `official`
manual entry workflows. For first-delivery PyPI live publication, the PyPI OIDC
publish job should be hosted by the official entry workflow identity configured
on PyPI, while still consuming the frozen plan and materialized publish request
from the shared control plane.

Adding a third operator-facing PyPI workflow would avoid some wiring complexity
but would reopen the already-settled two-profile entry model and should be
treated as a larger design change.

## Out-of-Scope Boundary

This topology decision covers whether current-scope live publish nodes are
scheduled as `github-token`, `external-oidc-entry-workflow`,
`external-oidc-caller-workflow`, or `external-oidc-reusable-workflow`.

It does not expand PyPI artifact cardinality. Current live PyPI support remains
the one-wheel-plus-optional-sdist path described by the descriptor, plan, and
low-level design pages. Broader PyPI multi-wheel or cross-variant wheel sets are
tracked separately in
[Workflow Release Deferred PyPI Multi-Wheel Support](./workflow-release-deferred-pypi-multi-wheel-support.md).

## Middle-Layer Impact

Yes, this affects middle-layer design. It does not overturn the upper-layer
planner-centric architecture, descriptor-gated participation, or the `buddy` /
`official` profile model. It does change cross-component contracts that were
previously treated as settled:

| Design surface                        | Required change                                                                                                                                                                             |
| ------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Target-instance catalog and snapshots | Add or derive a first-class trusted-publisher topology value for each live publish target.                                                                                                  |
| Plan shape                            | Freeze enough topology data for the control plane to schedule publish nodes without registry-specific guesses after planning.                                                               |
| Workflow and executor boundaries      | Replace the single reusable publish fan-out assumption with topology-partitioned publish execution while preserving one logical publish node and one publish receipt per `publish-node-id`. |
| Execution-set file                    | Add topology-partitioned publish selectors, or an equivalent closed shape, so empty and mixed-topology runs are deterministic.                                                              |
| External setup checklist              | Configure PyPI and npmjs for the official entry workflow, NuGet.org conservatively for the entry workflow, and RubyGems according to its reusable-workflow support.                         |
| Low-level workflow filenames          | Freeze every workflow filename that appears in a registry trusted-publisher policy, not just a single reusable publish workflow filename.                                                   |
| Acceptance traceability               | Add live PyPI and npmjs official publication evidence to first-delivery acceptance.                                                                                                         |

The lower-layer implementation can still own helper scripts, internal modules,
and exact matrix mechanics, but it must not choose the topology model on the fly.
The topology partition belongs in the sealed design contracts before coding.

## Source Notes

- GitHub OIDC reference documents `environment`, `workflow_ref`,
  `job_workflow_ref`, and `job_workflow_sha` claims:
  <https://docs.github.com/en/actions/reference/security/oidc>.
- PyPI trusted publisher setup requires repository owner, repository name,
  workflow filename, and optional environment:
  <https://docs.pypi.org/trusted-publishers/adding-a-publisher/>.
- npm trusted publishing requires a workflow filename and environment, and its
  troubleshooting guidance says `workflow_call` validation checks the calling
  workflow's name:
  <https://docs.npmjs.com/trusted-publishers>.
- NuGet.org trusted publishing requires repository owner, repository, workflow
  file name, and optional environment:
  <https://learn.microsoft.com/nuget/nuget-org/trusted-publishing>.
- RubyGems trusted publishing documents reusable workflow handling and
  `job_workflow_ref` implications:
  <https://guides.rubygems.org/trusted-publishing/>.

## Related Pages

- [Workflow Release Descriptor Schema](./workflow-release-descriptor-schema.md)
- [Workflow Release Plan Shape](./workflow-release-plan-shape.md)
- [Workflow Release Workflow and Executor Boundaries](./workflow-release-workflow-executor-boundaries.md)
- [Workflow Release Low-Level Design](./workflow-release-low-level-design.md)
- [Workflow Release Deferred PyPI Multi-Wheel Support](./workflow-release-deferred-pypi-multi-wheel-support.md)
