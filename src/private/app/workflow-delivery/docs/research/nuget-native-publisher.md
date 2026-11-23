# NuGet Native Publisher Integration

## Scope and Accepted Boundary

This source research informs the NuGet Release integration. It is not
runtime qualification, publication authorization or evidence of a newly executed
Release request. Existing NuGet native and publication evidence retains its
original client, source, protocol and run identities.

The owner accepted one application invocation of a supported native publisher,
including its built-in bounded transport retries. This decision is expressed in
[WD-OPS-002](../requirements.md#observation-retry-and-recovery) and the
[MLD destination contract](../design/release.md#destination-interface).
It does not promise one network write, authorize application automatic resend or
remove fresh manual same-revision recovery. After a native invocation returns
failure or ambiguity, or is interrupted, subsequent application writes stop;
bounded observations can still establish business completion.

Delegate upload construction and transport to the official client. Do not use a
testing-only retry override, reflection, a custom retry guard or an application
implementation of NuGet multipart publication. Required destination
non-overwrite, qualification and complete verification remain unchanged.

## Pinned Source Findings

This source basis inspects `NuGet.Protocol` 8.0.0. Its published
[package repository metadata][protocol-metadata] pins
[NuGet.Client commit `7719b2079546f52df28ecb639ce114ca8ca82440`][commit].
These are source findings for that version, not runtime qualification of the
cohort. The [earlier 7.9.0 source basis][previous-source-basis] and historical
publication evidence retain their original subjects.

| Source                                                                                                    | Finding                                                                                                                                                              |
| --------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| [PackageUpdateResource.cs, ordinary `PushPackageToServer`, lines 505–520][push]                           | Creates `HttpSourceRequest` with a timeout and no `MaxTries` override. The symbol temporary-key branch is a different path.                                          |
| [HttpSourceRequest.cs, lines 57–58][request] and [HttpRetryHandlerRequest.cs, lines 16–19][retry-request] | The default maximum is three attempts, including the initial attempt.                                                                                                |
| [HttpSource.cs, lines 339–357][http]                                                                      | Passes request retry settings to the native handler. Its `RetryHandler` setter, lines 32–34, explicitly excludes product use.                                        |
| [HttpRetryHandler.cs, lines 226–281][retry]                                                               | May retry server errors and transport exceptions/timeouts. The 408/429 setting is narrower than disabling all retries.                                               |
| [EnhancedHttpRetryHelper.cs][enhanced] and [HttpRetryHandler.cs][retry]                                   | An enhanced maximum-try environment value exists, but the inspected ordinary handler does not consume it. Delay and Retry-After settings are not retry-off controls. |
| [PushRunner.cs, lines 139–152][runner] and [XPlat PushCommand.cs][cli]                                    | The official CLI calls the same publisher and exposes no retry-count argument.                                                                                       |

The ordinary publisher constructs its request privately. Public lower-level
`MaxTries` properties therefore do not establish a supported configuration for
that publisher. No retry-off configuration was found in this selected path;
this is a constrained conclusion, not proof that every NuGet integration lacks
such a capability.

## Client Distribution Selection

The [original SDK inspection][sdk-inspection] retains the exact SDK-shipped and
packaged client identities, unsuccessful public-source acquisitions and package
metadata. The 2026-10-11 inspection found different assembly identities; failed
lookups established neither SDK incompatibility nor a matching source basis.
That recorded SDK is an observation subject, not the current SDK-selection
authority.

The [concrete integration contract](../integration/dotnet.md#concrete-nuget-release-integration)
owns selection of the packaged client group rather than assuming SDK-copy
equivalence. The inspected 8.0.0 [NuGet.Packaging `net10.0` dependency
group][packaging-metadata] requires Newtonsoft.Json 13.0.3 or later.
[NuGet.Commands 8.0.0][commands-metadata] requires NuGet.ProjectModel 8.0.0 or
later; retaining a direct 7.9.0 selection does not satisfy that minimum.
Package metadata establishes dependency shape, not integration compatibility.
The contract requires resolved assembly identities, NBGV/SDK binding and ordinary native
restore/consumer validation and owns Microsoft.Build/NBGV integration and JSON
copy sources. Accepting this source basis does not report implementation or
runtime qualification of the new cohort. No native experiment, private-source
access or additional publication is inferred.

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

`NuGetPublisherTests` guards these findings against the selected
NuGet.Protocol, which CI selects when `Directory.Packages.props` changes. A
controlled transport replaces only the network. Its existing assertions cover
the default maximum of three sends for 500, 503, 408 and 429 responses and for
`HttpRequestException`, `IOException` and the client's request `TimeoutException`;
handled terminal failures with unchanged original bytes; no resend for a 409
duplicate or 400, 401, 403 and 404 responses; no credential forwarding on a
redirect; and parent cancellation reaching the send and response stream.
These cases are qualification obligations, not new 8.0.0 runtime observations.
A bare `SocketException` is not covered because the real
`SocketsHttpHandler` wraps socket errors in `HttpRequestException`. The
`NUGET_RETRY_HTTP_429` and `NUGET_ENHANCED_*` environment settings are outside
the guard: CI sets none of them, and the guard asserts the defaults. Live GitHub
Packages behavior remains bound to the
[historical native/publication records](../validation/historical-readers.md#retired-campaign-records).
A change that alters the
bound fails CI; it does not add an application retry mechanism.

When the selected protocol or direct client cohort changes, the integration
author rechecks its published source identity, dependency metadata and these
version-bound findings, keeping them self-consistent with dependent
implementation at merge. Independent Release
integration review is the fallback review. The unchanged publisher guards and
native qualification requirements must pass on the actual new dependency graph
before its implementation is accepted; earlier passes do not transfer.

[commit]: https://github.com/NuGet/NuGet.Client/tree/7719b2079546f52df28ecb639ce114ca8ca82440
[push]: https://github.com/NuGet/NuGet.Client/blob/7719b2079546f52df28ecb639ce114ca8ca82440/src/NuGet.Core/NuGet.Protocol/Resources/PackageUpdateResource.cs#L505
[request]: https://github.com/NuGet/NuGet.Client/blob/7719b2079546f52df28ecb639ce114ca8ca82440/src/NuGet.Core/NuGet.Protocol/HttpSource/HttpSourceRequest.cs#L57
[retry-request]: https://github.com/NuGet/NuGet.Client/blob/7719b2079546f52df28ecb639ce114ca8ca82440/src/NuGet.Core/NuGet.Protocol/HttpSource/HttpRetryHandlerRequest.cs#L16
[http]: https://github.com/NuGet/NuGet.Client/blob/7719b2079546f52df28ecb639ce114ca8ca82440/src/NuGet.Core/NuGet.Protocol/HttpSource/HttpSource.cs#L339
[retry]: https://github.com/NuGet/NuGet.Client/blob/7719b2079546f52df28ecb639ce114ca8ca82440/src/NuGet.Core/NuGet.Protocol/HttpSource/HttpRetryHandler.cs#L226
[enhanced]: https://github.com/NuGet/NuGet.Client/blob/7719b2079546f52df28ecb639ce114ca8ca82440/src/NuGet.Core/NuGet.Protocol/EnhancedHttpRetryHelper.cs
[runner]: https://github.com/NuGet/NuGet.Client/blob/7719b2079546f52df28ecb639ce114ca8ca82440/src/NuGet.Core/NuGet.Commands/CommandRunners/PushRunner.cs#L139
[cli]: https://github.com/NuGet/NuGet.Client/blob/7719b2079546f52df28ecb639ce114ca8ca82440/src/NuGet.Core/NuGet.CommandLine.XPlat/Commands/PushCommand.cs
[protocol-metadata]: https://api.nuget.org/v3-flatcontainer/nuget.protocol/8.0.0/nuget.protocol.nuspec
[commands-metadata]: https://api.nuget.org/v3-flatcontainer/nuget.commands/8.0.0/nuget.commands.nuspec
[packaging-metadata]: https://api.nuget.org/v3-flatcontainer/nuget.packaging/8.0.0/nuget.packaging.nuspec
[previous-source-basis]: https://github.com/hcoona/three/blob/b4ad4adbc8a313fdb714fc43f64ed422a21461dc/src/private/app/workflow-delivery/docs/research/nuget-native-publisher.md#pinned-source-findings
[sdk-inspection]: https://github.com/hcoona/three/blob/dcea74ff7c2cfb9e3a1540467346b0167715b213/src/private/app/workflow-delivery/docs/research/nuget-native-publisher.md#client-distribution-selection
