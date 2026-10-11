# NuGet Native Publisher Integration

## Scope and Accepted Boundary

This source research informs the replacement NuGet Release integration. It is not
runtime qualification, publication authorization or evidence of a newly executed
Release request. Existing NuGet native and publication evidence retains its
original client, source, protocol and run identities.

The owner accepted one application invocation of a supported native publisher,
including its built-in bounded transport retries. This decision is expressed in
[WD-OPS-002](../requirements.md#observation-retry-and-recovery) and the
[MLD destination contract](../middle-level-design.md#destination-interface).
It does not promise one network write, authorize application automatic resend or
remove fresh manual same-revision recovery. After a native invocation returns
failure or ambiguity, or is interrupted, subsequent application writes stop;
bounded observations can still establish business completion.

Delegate upload construction and transport to the official client. Do not use a
testing-only retry override, reflection, a custom retry guard or an application
implementation of NuGet multipart publication. Required destination
non-overwrite, qualification and complete verification remain unchanged.

## Pinned Source Findings

The repository uses `NuGet.Protocol` 7.9.0. Its package repository metadata pins
[NuGet.Client commit `977537e19c6be57fead1411e6cf05f936bf1baf4`][commit].
These are source findings for that version, not observations of live behavior.

| Source                                                                                                    | Finding                                                                                                                                                              |
| --------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| [PackageUpdateResource.cs, ordinary `PushPackageToServer`, lines 505–520][push]                           | Creates `HttpSourceRequest` with a timeout and no `MaxTries` override. The symbol temporary-key branch is a different path.                                          |
| [HttpSourceRequest.cs, lines 60–61][request] and [HttpRetryHandlerRequest.cs, lines 16–19][retry-request] | The default maximum is three attempts, including the initial attempt.                                                                                                |
| [HttpSource.cs, lines 339–357][http]                                                                      | Passes request retry settings to the native handler. Its `RetryHandler` setter, lines 32–34, explicitly excludes product use.                                        |
| [HttpRetryHandler.cs, lines 230–280][retry]                                                               | May retry server errors and transport exceptions/timeouts. The 408/429 setting is narrower than disabling all retries.                                               |
| [EnhancedHttpRetryHelper.cs][enhanced] and [HttpRetryHandler.cs][retry]                                   | An enhanced maximum-try environment value exists, but the inspected ordinary handler does not consume it. Delay and Retry-After settings are not retry-off controls. |
| [PushRunner.cs, lines 139–152][runner] and [XPlat PushCommand.cs][cli]                                    | The official CLI calls the same publisher and exposes no retry-count argument.                                                                                       |

The ordinary publisher constructs its request privately. Public lower-level
`MaxTries` properties therefore do not establish a supported configuration for
that publisher. No retry-off configuration was found in this selected path;
this is a constrained conclusion, not proof that every NuGet integration lacks
such a capability.

Official [dotnet push documentation][push-doc] and [NuGet environment-variable
documentation][env-doc], consulted on 2026-10-11, do not expose a retry-off option
for this operation. The [removed enhanced-retry switch][removed] changed backoff
behavior; it did not select zero retries.

## Client Distribution Selection

The selected SDK 10.0.401's NuGet.Protocol and NuGet.Commands assemblies report
informational version
`7.9.0-rc.42413+e34a38d2ae1fc26406a317517196e55c68ff83ab`, which differs from the
packaged 7.9.0 source pin above. Read-only file inspection on 2026-10-11
established that identity difference. Exact public source-file retrievals returned
404 and the public commit lookup returned 422; those failed acquisitions do not
establish SDK incompatibility or a matching source basis.

Select the coherent official packaged 7.9.0 NuGet group instead of assuming
equivalence with the SDK copy. Its NuGet.Packaging `net8.0` dependency group
requires Newtonsoft.Json 13.0.3 or later. Package metadata establishes dependency
shape, not integration compatibility. The
[concrete integration contract](../migration-strategy.md#concrete-nuget-buddy-integration)
requires resolved assembly identities, NBGV/SDK binding and ordinary native
restore/consumer validation. It retains SDK Microsoft.Build references and the
official NBGV assembly while removing competing JSON copy sources. No native
experiment, private-source access or additional publication is inferred.

The delivery work carrier retains original SDK identity/digest inspection,
failed public source/commit acquisitions and official package metadata. These
are local source and file observations, not registry-publication evidence.

## Integration Consequences and Evidence Limits

Invoke the official publisher for one exact qualified `.nupkg`, with no symbol
publication, no wildcard package expansion and no application retry loop.
Preserve its actual terminal outcome, then independently verify complete native
state and required feed-qualified consumption. A lost response followed by a
native duplicate rejection can still produce business satisfaction when every
required check succeeds; report the command abnormality without claiming that
this invocation created the package.

The destination supplies active-coordinate non-overwrite. This does not promise
lifetime-global immutability after administrative deletion. Application locks or
extra trust records cannot strengthen that native contract.

Client selection and credential/configuration integration still require their
concrete implementation contract. Source inspection does not prove an installed
CLI's behavior, actual GitHub Packages response or live publication. Controlled
integration tests and ordinary CI qualify only their stated scope; any live
validation remains separately authorized.

Recheck the relevant source when the selected publisher/NuGet version changes.
The implementation reviewer evaluates whether retries remain built-in and
bounded, whether new options alter the accepted integration, and whether affected
contracts and validation need adjustment. The next publisher implementation or
dependency update is the fallback review event. Missing evidence blocks the
changed capability rather than adding an application retry mechanism.

[commit]: https://github.com/NuGet/NuGet.Client/tree/977537e19c6be57fead1411e6cf05f936bf1baf4
[push]: https://github.com/NuGet/NuGet.Client/blob/977537e19c6be57fead1411e6cf05f936bf1baf4/src/NuGet.Core/NuGet.Protocol/Resources/PackageUpdateResource.cs#L505
[request]: https://github.com/NuGet/NuGet.Client/blob/977537e19c6be57fead1411e6cf05f936bf1baf4/src/NuGet.Core/NuGet.Protocol/HttpSource/HttpSourceRequest.cs#L60
[retry-request]: https://github.com/NuGet/NuGet.Client/blob/977537e19c6be57fead1411e6cf05f936bf1baf4/src/NuGet.Core/NuGet.Protocol/HttpSource/HttpRetryHandlerRequest.cs#L16
[http]: https://github.com/NuGet/NuGet.Client/blob/977537e19c6be57fead1411e6cf05f936bf1baf4/src/NuGet.Core/NuGet.Protocol/HttpSource/HttpSource.cs#L339
[retry]: https://github.com/NuGet/NuGet.Client/blob/977537e19c6be57fead1411e6cf05f936bf1baf4/src/NuGet.Core/NuGet.Protocol/HttpSource/HttpRetryHandler.cs#L230
[enhanced]: https://github.com/NuGet/NuGet.Client/blob/977537e19c6be57fead1411e6cf05f936bf1baf4/src/NuGet.Core/NuGet.Protocol/EnhancedHttpRetryHelper.cs
[runner]: https://github.com/NuGet/NuGet.Client/blob/977537e19c6be57fead1411e6cf05f936bf1baf4/src/NuGet.Core/NuGet.Commands/CommandRunners/PushRunner.cs#L139
[cli]: https://github.com/NuGet/NuGet.Client/blob/977537e19c6be57fead1411e6cf05f936bf1baf4/src/NuGet.Core/NuGet.CommandLine.XPlat/Commands/PushCommand.cs
[push-doc]: https://learn.microsoft.com/dotnet/core/tools/dotnet-nuget-push
[env-doc]: https://learn.microsoft.com/nuget/reference/cli-reference/cli-ref-environment-variables
[removed]: https://learn.microsoft.com/dotnet/core/compatibility/sdk/10.0/nuget-enhanced-http-retry-removed
