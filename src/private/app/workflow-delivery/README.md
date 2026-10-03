# Workflow Delivery

This private application implements the replacement Workflow V3 core. The
[project records](../../../public/lib/three-workflow-delivery-v3/docs/README.md),
[middle-level contracts](../../../public/lib/three-workflow-delivery-v3/docs/middle-level-design.md)
and [migration plan](../../../public/lib/three-workflow-delivery-v3/docs/migration-strategy.md)
remain in their current namespace until the old Python application's last
consumer retires. This entry describes the implemented interface, not a second
architecture authority.

## Current Scope

The application selects CI checks from supplied, resolved repository facts and
collects candidate-bound check results. It has no native fact reader, Git event
resolver, quality YAML resolver, check executor or Release command yet. Existing
workflows still use their current implementations. Synthetic graph tests establish
selection and collection behavior, not native ecosystem or repository-wide coverage.

Native integrations must supply complete ownership and reverse-consumer facts for
their declared scope, including base and candidate relations, selected quality
presets and all required variants. They must report incomplete evaluation or
unsupported relevant shapes in `errors`. An empty error array is a producer's
result, not an admission certificate or proof that a reader is implemented.
The scope label must agree across revisions; it is not a coverage inference.

## Commands and Transfers

Build with the repository-pinned SDK. From the repository root:

```powershell
dotnet build src/private/app/workflow-delivery/WorkflowDelivery.csproj
dotnet src/private/app/workflow-delivery/bin/Debug/net10.0/WorkflowDelivery.dll ci plan request.json
dotnet src/private/app/workflow-delivery/bin/Debug/net10.0/WorkflowDelivery.dll ci result plan.json results.json
```

Commands emit one JSON value on stdout. Exit code `0` means a valid plan or
satisfied CI result; `1` means an unsatisfied result (including incompatible
result subjects) with its JSON explanation; `2` means malformed input, a planning
failure or unsupported usage, with a diagnostic on stderr. Redirect stdout
when a receiving process needs a file. In-process consumers call the same typed
operations without serializing their inputs.

The source-generated JSON contracts use camel-case property names, required
constructor fields and named check statuses. Unknown fields and missing or null
required values fail. These internal formats have no historical compatibility
promise. The concrete types are in [Contracts.cs](CI/Contracts.cs).

| Input                 | Required content and producer responsibility                                                                                                                                |
| --------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Planning request      | `basis`, `candidate`, `changedPaths`, `full`; the caller resolves the event's actual comparison/tested revisions and includes both rename paths and deleted paths           |
| Each revision's facts | `revision`, `scope`, `projects`, `sharedInputs`, `unaffectedPaths`, `errors`; native readers establish completeness for this declared scope                                 |
| Project               | Stable `id`, repository-relative `directory`, build `dependencies`, distinct `qualityConsumers`, nullable `releaseUnit` and resolved `qualityPreset`, and concrete `checks` |
| Shared input          | Exact repository-relative `path` and project `consumers`, including actual version/configuration/lock inputs                                                                |
| Unaffected path       | Exact path the producer established has no consumers in this scope; no wildcard or unknown-path fallback                                                                    |
| Check                 | `key` (`target`, `check`, `variant`), actual `dimensions`, `runner`, `required`, and prerequisite keys; variant names identify resolved dimension combinations              |
| Result array          | Each entry carries the exact `candidate`, check `key` and `status`: `Passed`, `Failed`, `Cancelled`, `TimedOut` or `Skipped`                                                |

Paths use Git's case-sensitive forward-slash spelling, without a leading slash,
empty components or traversal. A project directory owns its subtree, with the
nearest project owning nested paths. Shared-input consumers are added separately.
Root-level inputs and inputs outside project directories need explicit consumers;
they do not become implicit dependencies of every project. Readers must resolve
native imports, workspace references, extra inputs and quality declarations before
constructing these values; the core does not parse build-system expressions.

Selection follows the union of base/candidate reverse dependencies and quality
consumers. Projects in an affected Release Unit are included with all their
candidate checks/variants. Deleted projects contribute relationships and reasons
but cannot become runnable targets. Known unaffected changes may produce an empty
plan. Unknown paths and incomplete facts fail even in explicit full mode.

Identical check keys deduplicate only when runner, dimensions and prerequisites
agree. Required status combines the selected contracts. Required checks promote
their prerequisite closure to required; missing or cyclic prerequisites fail.
These prerequisites order checks, not individual native compilation nodes. Plans
retain quality presets and all initiating path/revision/project reasons.

The collector requires every selected required result at the candidate revision.
Missing, failed, cancelled, timed-out, skipped and conflicting required results
prevent satisfaction. Repeated identical results are harmless; unexpected subjects
or unknown statuses fail. Advisory outcomes remain visible without failing the
required result. A native cancellation that prevents collection is not a success.
The eventual workflow must preserve that native conclusion; this command cannot
observe jobs that its caller did not report.

## Validation and CI Ownership

The single test project is
[`WorkflowDelivery.Tests.csproj`](../../../../tests/private/app/workflow-delivery/WorkflowDelivery.Tests.csproj).
The existing `dirs.proj` traversal and root `ci.yml` .NET test job discover it;
`ci_scope.py` discovers the new native project roots. No second test workflow or
new job owner is introduced. Run the bounded suite with:

```powershell
dotnet test --project tests/private/app/workflow-delivery/WorkflowDelivery.Tests.csproj
```

Tests cover selection, failure behavior, result completeness and the actual JSON
CLI boundary. They do not qualify the old or future native fact collectors. Each
native integration still needs the migration plan's concrete coverage and caller
validation before switching a workflow.
