# Workflow Delivery Records

This portal routes the internal application's requirements, design, native
integration evidence and historical audit readers. It is navigation, not a
second authority or a support/operation grant. The
[application README](../README.md) owns current commands and limitations;
[agent handoff](agent-handoff.md) routes work and retained operation bounds.

## Normative Hierarchy

| Concern                                                           | Current authority                                                                                                                   |
| ----------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------- |
| Required behavior and non-goals                                   | [Requirements](requirements.md)                                                                                                     |
| Architecture and trust boundaries                                 | [High-level design](high-level-design.md)                                                                                           |
| Selection, execution, quality, transfers and outcomes             | [Middle-level design](middle-level-design.md)                                                                                       |
| Concrete ecosystem interfaces, callers and Official channel scope | [Integration contract](migration-strategy.md)                                                                                       |
| Current positive work authorization                               | [Repository Delivery Wave](../../../../../docs/delivery-wave.md)                                                                    |
| Repository review and record lifecycle                            | [Governance](../../../../../docs/governance/governance-system.md), [record policy](../../../../../docs/governance/record-system.md) |

Requirements govern current design. Native source findings and observations
retain their actual evidence level; merge does not expand their environments or
turn them into operation permission. Unsupported necessary native capabilities
remain unsupported. Internal historical formats need no compatibility reader.

## Native Integration Evidence

| Integration                     | Source and bounded observations                                                                                                                                                                          |
| ------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Git and managed inputs          | [Git facts](research/git-native-facts.md), [MSBuild items](research/msbuild-native-items.md), [NBGV inputs](research/nbgv-native-inputs.md)                                                              |
| PNPM and Node                   | [PNPM 12 planning](research/pnpm-native-planning.md), [native Node execution](research/node-ci-native-execution.md)                                                                                      |
| Python planning and preparation | [Native planning](research/python-native-planning.md), [graph qualification](research/python-native-graph-qualification.md), [planning enrichment](research/python-planning-enrichment-qualification.md) |
| Python package execution        | [Packaging assessment](research/python-native-packaging.md), [finite qualification](research/python-native-package-qualification.md)                                                                     |
| Native publishers               | [NuGet](research/nuget-native-publisher.md), [Python](research/python-native-publisher.md), [Ruby](research/ruby-native-publisher.md)                                                                    |
| Contraction assumptions         | [Feasibility](research/contraction-feasibility.md)                                                                                                                                                       |

The Python package record preserves failed output adoption and spent lifetimes
beside later bounded success. Neither tool readiness nor corrected source
retroactively qualifies a failed generation. Ordinary changed-source/native
runner qualification and independent hosted reconciliation remain separate
from local observations and from completed publication campaigns.

## Retained Campaign Evidence

| Concern                                             | Original outcomes, controls and limits                                                                                                                                                      |
| --------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| npm native and normal publication                   | [Evidence](validation/native-and-normal-live-evidence.md), [registry support research](research/github-packages-supported-registries.md)                                                    |
| NuGet profile, native and normal publication        | [Research/evidence](research/nuget-smoke-evidence.md), [normal-Live evidence](validation/nuget-normal-live-evidence.md), [delivery retrospective](research/nuget-delivery-retrospective.md) |
| Python preparation and native failure               | [Readiness](validation/python-native-readiness.md), [native acceptance](validation/python-native-acceptance.md), [source evidence](research/python-smoke-evidence.md)                       |
| Python bootstrap, publication and ordinary recovery | [Bootstrap](validation/python-bootstrap.md), [normal publication](validation/python-normal-live-evidence.md), [recovery](validation/python-hosted-recovery.md)                              |
| Ruby operations and original publication            | [Operation protocol](ruby-operation-protocol.md), [normal-Live evidence](validation/ruby-normal-live-evidence.md), [research](research/ruby-smoke-evidence.md)                              |
| Historical implementation recovery                  | [Actual producer-qualified readers](validation/historical-readers.md)                                                                                                                       |
| Earlier CI and legacy boundary                      | [Shadow evidence](validation/ci-shadow-evidence.md), [legacy boundary](research/legacy-workflow-boundary.md)                                                                                |

## Historical Source Rule

All campaigns remain closed, with original failures, spent ceilings and separate
producer/control/fixture identities. Current audit readers recover their actual
historical source; they do not invoke a current compatibility/replay service.
Unavailable required originals remain a limitation. Current source acceptance
supplies no publication, dispatch, authentication or administrative authority.

The [pre-contraction glossary](architecture-glossary.md) and slice LLDs
([npm](hcoona-release-smoke-npm-lld.md),
[NuGet](hcoona-release-smoke-github-packages-lld.md),
[Python](hcoona-release-smoke-python-lld.md),
[Ruby](hcoona-release-smoke-ruby-lld.md)) retain concrete historical-evidence
interpretation consumers. Their former admission, approval-proof, witness and
campaign machinery does not prescribe the current application. The
[NuGet research handoff](nuget-smoke-research-handoff.md) remains an original
research/effects boundary, not an executable next step.

The former middle-level and migration records are immutable historical sources:

- Former middle-level designs, pinned at `72939154ab9ebfb8d313908c551fb66eeefae499`
  for existing runtime and evidence readers:
    - [Repository Model and Release Unit](https://github.com/hcoona/three/blob/72939154ab9ebfb8d313908c551fb66eeefae499/src/public/lib/three-workflow-delivery-v3/docs/repository-model-release-unit-mld.md)
    - [Governance Integration](https://github.com/hcoona/three/blob/72939154ab9ebfb8d313908c551fb66eeefae499/src/public/lib/three-workflow-delivery-v3/docs/governance-integration-mld.md)
    - [CI Qualification](https://github.com/hcoona/three/blob/72939154ab9ebfb8d313908c551fb66eeefae499/src/public/lib/three-workflow-delivery-v3/docs/ci-qualification-mld.md)
    - [Release Delivery](https://github.com/hcoona/three/blob/72939154ab9ebfb8d313908c551fb66eeefae499/src/public/lib/three-workflow-delivery-v3/docs/release-delivery-mld.md)
    - [Shared Foundation](https://github.com/hcoona/three/blob/72939154ab9ebfb8d313908c551fb66eeefae499/src/public/lib/three-workflow-delivery-v3/docs/shared-foundation-mld.md)
- [Pre-contraction migration policy](https://github.com/hcoona/three/blob/67f294cfd864a6376c14bee4a9f2ed0b92af9552/src/public/lib/three-workflow-delivery-v3/docs/migration-strategy.md)

These records cannot fill a current design gap or revive a retired requirement.
Use an explicitly admitted V3 extraction/revalidation before reusing any v1/v2
mechanism. Git and PR/Issue carriers retain chronology rather than current-page
retry histories or a second work ledger.
