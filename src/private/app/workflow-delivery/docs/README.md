# Workflow Delivery Records

This portal routes the internal application's requirements, design, native
integration evidence and historical audit readers. It is navigation, not a
second authority or a support/operation grant. The
[application README](../README.md) owns current commands and limitations;
[agent handoff](agent-handoff.md) routes work and retained operation bounds.

## Normative Hierarchy

| Concern                                                                                           | Current authority                                                                                                                                                          |
| ------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Required behavior and non-goals                                                                   | [Requirements](requirements.md)                                                                                                                                            |
| Architecture and trust boundaries                                                                 | [High-level design](high-level-design.md)                                                                                                                                  |
| Selection, execution, quality, transfers, outcomes and shared project/caller inventories          | [Middle-level design](middle-level-design.md)                                                                                                                              |
| Ecosystem-specific facts, callers and destinations; Official channel scope and CLI payload tables | [.NET/NuGet](integration/dotnet.md), [Node/npm](integration/node.md), [Python](integration/python.md), [Ruby](integration/ruby.md), [common tables](integration/common.md) |
| Current positive work authorization                                                               | [Repository Delivery Wave](../../../../../docs/delivery-wave.md)                                                                                                           |
| Repository review and record lifecycle                                                            | [Governance](../../../../../docs/governance/governance-system.md), [record policy](../../../../../docs/governance/record-system.md)                                        |

Requirements govern current design. Native source findings and observations
retain their actual evidence level; merge does not expand their environments or
turn them into operation permission. Unsupported necessary native capabilities
remain unsupported. Internal historical formats need no compatibility reader.

## Native Integration Evidence

Read a record when changing or reviewing the integration it supports, or when
its recheck trigger fires.

| Integration                     | Source and bounded observations                                                                                                                                                                          | Read when                                                                                                               |
| ------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------- |
| Git and managed inputs          | [Git facts](research/git-native-facts.md), [MSBuild items](research/msbuild-native-items.md), [NBGV inputs](research/nbgv-native-inputs.md)                                                              | Changing Git comparison/path reading, the control-test resource query or NBGV input mapping                             |
| PNPM and Node                   | [PNPM 12 planning](research/pnpm-native-planning.md), [native Node execution](research/node-ci-native-execution.md)                                                                                      | Changing the [Node integration](integration/node.md) or the PNPM pin                                                    |
| Python planning and preparation | [Native planning](research/python-native-planning.md), [graph qualification](research/python-native-graph-qualification.md), [planning enrichment](research/python-planning-enrichment-qualification.md) | Changing Python planning facts, the UV/PyPA pins or the pytest query in the [Python integration](integration/python.md) |
| Python package execution        | [Packaging assessment](research/python-native-packaging.md), [finite qualification](research/python-native-package-qualification.md)                                                                     | Changing Python package preparation, build or consumers                                                                 |
| Native publishers               | [NuGet](research/nuget-native-publisher.md), [Python](research/python-native-publisher.md), [Ruby](research/ruby-native-publisher.md)                                                                    | Changing that ecosystem's Release publisher, observation or completion                                                  |
| Contraction assumptions         | [Feasibility](research/contraction-feasibility.md)                                                                                                                                                       | Questioning the owner's Windows assumption or native NBGV build execution                                               |

The Python package record preserves failed output adoption and spent lifetimes
beside later bounded success. Neither tool readiness nor corrected source
retroactively qualifies a failed generation. Ordinary changed-source/native
runner qualification and independent hosted reconciliation remain separate
from local observations and from completed publication campaigns.

## Retained Campaign Evidence

Read these only to interpret or audit a closed campaign, or when the
[handoff](agent-handoff.md#authorization-and-closed-campaigns) limits route to
them; they are not current behavior.

| Concern                                             | Original outcomes, controls and limits                                                                                                                         | Read when                                                                                                  |
| --------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------- |
| npm native and normal publication                   | [Evidence](validation/native-and-normal-live-evidence.md), [registry support research](research/github-packages-supported-registries.md)                       | Auditing npm publication or credential reach, or a destination decision relying on GitHub Packages support |
| NuGet profile, native and normal publication        | [Research/evidence](research/nuget-smoke-evidence.md), [normal-Live evidence](validation/nuget-normal-live-evidence.md)                                        | Auditing NuGet Native R4 or normal-Live results                                                            |
| Python preparation and native failure               | [Retired native acceptance](validation/historical-readers.md#retired-python-native-acceptance), [source evidence](research/python-smoke-evidence.md)           | Interpreting the failed Python native generation                                                           |
| Python bootstrap, publication and ordinary recovery | [Bootstrap](validation/python-bootstrap.md), [normal publication](validation/python-normal-live-evidence.md), [recovery](validation/python-hosted-recovery.md) | Auditing Python bootstrap, publication or recovery outcomes                                                |
| Ruby original publication                           | [Normal-Live evidence](validation/ruby-normal-live-evidence.md), [research](research/ruby-smoke-evidence.md)                                                   | Auditing Ruby publication outcomes                                                                         |
| Historical implementation and record recovery       | [Producer-qualified readers and retired records](validation/historical-readers.md)                                                                             | Recovering an original producer or a retired record's text                                                 |
| Legacy workflow retirement                          | [Retained v1 boundary](research/legacy-workflow-boundary.md)                                                                                                   | Reading old designs or touching the retained v1 Official/reusable workflows or their validator diagnostics |

## Historical Source Rule

All campaigns remain closed, with original failures, spent ceilings and separate
producer/control/fixture identities. Current audit readers recover their actual
historical source; they do not invoke a current compatibility/replay service.
Unavailable required originals remain a limitation. Current source acceptance
supplies no publication, dispatch, authentication or administrative authority.

Retired Workflow Delivery records, including the pre-contraction glossary,
slice LLDs, former designs, requirements, migration policy and campaign
protocols, are indexed with commit-pinned links in the
[historical record index](validation/historical-readers.md#retired-records).

These records cannot fill a current design gap or revive a retired requirement.
Use an explicitly admitted V3 extraction/revalidation before reusing any v1/v2
mechanism. Git and PR/Issue carriers retain chronology rather than current-page
retry histories or a second work ledger.
