# MSBuild Native Resource Input Projection

## Question and Consumer

Can the replacement tool obtain the control test project's embedded npm fixture
version input through MSBuild's supported native interface, without maintaining
another resource declaration or interpreting project XML?
The internal [resource reader](../../../../../private/app/workflow-delivery/Repository/MsBuildResourceReader.cs)
consumes the native answer under the HLD's [native boundary](../high-level-design.md#native-integrations)
and MLD's [minimum facts](../middle-level-design.md#minimum-facts). The first actual
consumer is `tests/private/app/workflow-delivery/WorkflowDelivery.Tests.csproj`:
its resource input crosses the npm fixture and existing .NET test-owner boundary.
Getting this relation is a prerequisite to preserving that owner during group
cutover, not a reason to migrate every .NET project first.

## Public Source Findings

Microsoft documentation is pinned to
`MicrosoftDocs/visualstudio-docs@653cd6e4ffe7e1ff69ba2c4eade3c29ed49a4336`, retrieved
on 2026-10-05. These are documented interfaces, not runtime or universal support
claims.

| Finding                                                                                                                    | Immutable supporting source                               |
| -------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------- |
| MSBuild 17.8+ supports `-getProperty` and `-getItem`; requesting items or multiple properties emits JSON                   | [Evaluate items and properties, lines 31–45][evaluation]  |
| With no target or target-result switch, results come from evaluation and no targets are built                              | [Evaluate items and properties, lines 33–39][evaluation]  |
| JSON groups properties and items, with associated native metadata including `FullPath`                                     | [Evaluate items and properties, lines 57–78][evaluation]  |
| `MSBuildProjectFullPath` is the reserved absolute path of the evaluated project                                            | [Reserved and well-known properties, line 66][properties] |
| `-noAutoResponse` omits automatic `MSBuild.rsp` and `Directory.Build.rsp`; property-list separators are semicolon or comma | [Command-line reference, lines 59–65][cli]                |
| Evaluation processes properties, imports and items outside targets, and can execute property functions                     | [Build process overview, evaluation sections][process]    |

## Adaptation and Alternatives

Use `dotnet msbuild` to request `MSBuildProjectFullPath`, `Configuration`,
`TargetFramework` and `RuntimeIdentifier` together with `EmbeddedResource`.
The caller chooses a native project and concrete required dimension; this
operation does not discover membership or dimensions. It disables automatic
response files so a response-file target cannot change the requested read into
target execution. Do not request a target, target result, restore or repair.

Use ordinary `System.Text.Json` to consume the documented groups and required
fields. Check native project/dimension agreement, then convert each absolute
`FullPath` against the same bound checkout to a committed regular-file path with
original Git spelling. This uses the native result, not unevaluated `Identity`,
package naming or an application implementation of include/remove/glob rules.
Reject unavailable/outside/uncommitted/nonregular required resources; ignore
unrelated native metadata. Duplicate native appearances yield one input relation.
Empty items mean no evaluated resources in this requested projection, not no
other project inputs. An empty unspecified RID differs from a missing/null value.
Initial dimension values exclude property-list separators, escape syntax, quotes
and control characters rather than introducing an MSBuild escaping engine.

Read both revisions separately and retain their project/input relations through
later selection. Native failure and malformed/conflicting required output remain
errors. The existing native process bounds apply; raw native diagnostics do not
become portable product errors. Evaluation still runs project logic and belongs
in the unprivileged context.

The maintained `Microsoft.Build.Evaluation.Project` API remains a candidate for
consumers needing actual imports/graph information. Hosting that API requires
correct SDK assembly/resolver setup and supplies no necessary additional answer
for this narrow resource read. This choice does not select the CLI for all future
.NET responsibilities. `MSBuildAllProjects` is not treated as a proved complete
import interface, and evaluated resource items are not all dynamic target inputs.

## Validation and Limits

Ordinary application tests use controlled documented native results and isolated
Git/MSBuild fixtures. They assert exact revision/project/dimension binding,
resources with spaces/Unicode and executable regular files, duplicate appearances,
empty resources/RID, required JSON groups/types/uniqueness, conflicting identities,
unavailable sources, both-revision removal/move and native failures/cancellation.
Actual CLI tests exercise native imports, glob/include/remove and conditional
items; a failing default target and response-file target must not run. Actual
native evaluation failure exposes a bounded exit error without raw diagnostics.
Shared native-process tests own runtime deadline/output/cleanup coverage rather
than repeating that implementation's full matrix in this reader. These ordinary
tests are not a new native observation campaign or external experiment.

Exact reviewed source, command results, TRX results and independent reviews belong
in the delivery PR. The repository tool pin is SDK 10.0.401; separate installed
version inspection reports MSBuild `18.9.11.42413`. That inspection alone does not
qualify project evaluation. Windows feasibility remains the owner's accepted
assumption; actual changed-code CI failures still require correction.

This component is not complete .NET inputs, roster/import/reference discovery,
owner routing, cross-runner transfer or CI group cutover. Retained Python consumers
and wider facts remain integration prerequisites. No registry operation, release
dispatch, new dependency declaration, credentials, native buffer generation,
generic fact service or application trust protocol follows from this projection.
Reassess the native switches/output when the selected SDK or required input shape
changes; the evaluator is the integration author and the fallback is its next
native-integration review.

[evaluation]: https://github.com/MicrosoftDocs/visualstudio-docs/blob/653cd6e4ffe7e1ff69ba2c4eade3c29ed49a4336/docs/msbuild/evaluate-items-and-properties.md#L31
[properties]: https://github.com/MicrosoftDocs/visualstudio-docs/blob/653cd6e4ffe7e1ff69ba2c4eade3c29ed49a4336/docs/msbuild/msbuild-reserved-and-well-known-properties.md#L66
[cli]: https://github.com/MicrosoftDocs/visualstudio-docs/blob/653cd6e4ffe7e1ff69ba2c4eade3c29ed49a4336/docs/msbuild/msbuild-command-line-reference.md#L59
[process]: https://github.com/MicrosoftDocs/visualstudio-docs/blob/653cd6e4ffe7e1ff69ba2c4eade3c29ed49a4336/docs/msbuild/build-process-overview.md
