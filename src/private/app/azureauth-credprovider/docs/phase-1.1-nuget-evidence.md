# Phase 1.1 NuGet Evidence Gate

Status: **Accepted**

Date: **2026-06-05**

Decision ID: **phase-1.1-nuget-evidence**

Gate name: **Phase 1.1 NuGet evidence gate**

Owner: **ADAPTER-NUGET**

## Gate Status and Decision

| Field                      | Decision                                                                                                                                                                         |
| -------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Gate status                | Passed for Phase 1.1 evidence gathering.                                                                                                                                         |
| Decision                   | Implement a NuGet plugin-shaped adapter that supports NuGet protocol plugin mode and delegates credential acquisition to core.                                                   |
| Evidence scope             | Local launch probe covers `dotnet restore` only. Upstream source and documentation cover the reference provider's handler shape and documented `netcore`/`netfx` install layout. |
| Implementation may proceed | Yes, for NuGet-dependent design and later Phase 10 implementation after normal upstream contract sequencing.                                                                     |
| Phase 1R routing           | Not entered. If later mandatory evidence disproves this launch or runtime model, dependent NuGet work must stop and enter 1R.                                                    |

## Upstream Snapshot

Reference source inspected from the local mirror of
[microsoft/artifacts-credprovider](https://github.com/microsoft/artifacts-credprovider).
The local mirror was clean and resolved to commit
[`9c3840be1c97594708331b1797b0a2d9dce480b3`](https://github.com/microsoft/artifacts-credprovider/commit/9c3840be1c97594708331b1797b0a2d9dce480b3),
described as `v2.0.1-9-g9c3840b`.

Commands used to identify the snapshot:

```bash
git -C /workspace/public/artifacts-credprovider --no-pager rev-parse HEAD
git -C /workspace/public/artifacts-credprovider --no-pager remote -v
git -C /workspace/public/artifacts-credprovider --no-pager describe --tags --always --dirty
git -C /workspace/public/artifacts-credprovider --no-pager status --short
```

Results:

```text
HEAD: 9c3840be1c97594708331b1797b0a2d9dce480b3
origin: https://github.com/microsoft/artifacts-credprovider.git
version description: v2.0.1-9-g9c3840b
status --short: no output
```

## Evidence Sources

Upstream source and documentation inspected:

- [README install requirements][readme-install]
    - `dotnet` requires the `netcore` plugin layout.
    - NuGet.exe and MSBuild require the `netfx` plugin layout.
    - Conventional installation under `.nuget/plugins` is recommended.
    - `NUGET_PLUGIN_PATHS` is documented as an alternative, with a warning against
      mixed `nuget.exe` and `dotnet` use on Windows.
- [README Linux/macOS install requirements][readme-linux-install]
    - The shell script installs only the `netcore` plugin.
    - Manual Linux/macOS installation copies `netcore`, and `netfx` only for
      `msbuild /t:restore`, under `$HOME/.nuget/plugins`.
    - `NUGET_PLUGIN_PATHS` can point at the `netcore` `.dll` as an alternative.
- [README standalone help][readme-plugin-help]
    - Documents `Plugin (-P)` as "Used by nuget to run the credential helper in
      plugin mode."
- [CredentialProviderArgs.cs][args-plugin]
    - Defines `Plugin`, `Uri`, `NonInteractive`, `IsRetry`, and `CanShowDialog`.
- [Program.cs request handler registration][program-handlers]
    - Registers handlers for `GetAuthenticationCredentials`, `GetOperationClaims`,
      `Initialize`, `SetLogLevel`, and `SetCredentials`.
- [Program.cs plugin mode][program-plugin]
    - Enters plugin mode only when parsed arguments contain `Plugin`.
    - Uses `PluginFactory.CreateFromCurrentProcessAsync(...)` in plugin mode.
- [Program.cs standalone path][program-standalone]
    - Uses a separate standalone credential-acquisition path for `Uri`, `IsRetry`,
      `NonInteractive`, and `CanShowDialog`.
- [InitializeRequestHandler.cs][initialize-handler]
    - Responds to NuGet `Initialize` with `MessageResponseCode.Success`.
- [GetOperationClaimsRequestHandler.cs][claims-handler]
    - Returns `OperationClaim.Authentication` only when both
      `PackageSourceRepository` and `ServiceIndex` are null.
    - Returns an empty claim list when either field is non-null.
- [GetAuthenticationCredentialsRequestHandler.cs][auth-handler]
    - Handles `GetAuthenticationCredentialsRequest`, checks the request URI,
      consults registered credential providers, returns Basic credentials on
      success, and otherwise returns NotFound or Error responses.
- [VstsCredentialProvider.cs][vsts-provider]
    - Maps NuGet request flags into token acquisition policy using `IsRetry`,
      `IsNonInteractive`, and `CanShowDialog`.
    - Returns Basic credentials using either `EntraToken` or `VssSessionToken`.
- [VstsSessionTokenFromBearerTokenProvider.cs][session-token-provider]
    - Exchanges acquired bearer tokens for Azure DevOps session tokens.
- [VstsSessionTokenClient.cs][session-token-client]
    - Restricts SPS token exchange to allowed Azure DevOps SPS hostnames before
      posting a bearer token.

## Reproducible Local Probe

A disposable local .NET console plugin was created under
`.copilot-scratch/nuget-evidence-probe`, passed through `NUGET_PLUGIN_PATHS`, and
removed after the run. It logged its arguments and exited without speaking the full
NuGet plugin protocol.

Environment and tool versions:

```text
OS: Ubuntu 24.04, linux-x64
.NET SDK: 10.0.300, commit caa81fa497
MSBuild: 18.6.3+caa81fa49
.NET host: 10.0.8, x64, commit 94ea82652c
dotnet nuget --version: NuGet Command Line 7.6.0.0
```

Probe plugin project, `.copilot-scratch/nuget-evidence-probe/plugin-app/PluginProbe.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
  </PropertyGroup>
</Project>
```

Probe plugin source, `.copilot-scratch/nuget-evidence-probe/plugin-app/Program.cs`:

```csharp
using System.Text;

var logPath = Environment.GetEnvironmentVariable("PLUGIN_PROBE_LOG");
if (!string.IsNullOrWhiteSpace(logPath))
{
    var lines = new[]
    {
        $"argv: [{string.Join(", ", args.Select(arg => arg))}]",
        $"cwd: {Environment.CurrentDirectory}",
        $"NUGET_PLUGIN_PATHS: {Environment.GetEnvironmentVariable("NUGET_PLUGIN_PATHS")}",
    };
    File.AppendAllText(logPath, string.Join(Environment.NewLine, lines) + Environment.NewLine, Encoding.UTF8);
}
```

Restore probe project, `.copilot-scratch/nuget-evidence-probe/probe/Probe.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.VisualStudio.Threading" Version="17.8.14" />
  </ItemGroup>
</Project>
```

Command shape:

```bash
DOTNET_CLI_HOME="$PWD/.copilot-scratch/nuget-evidence-probe/dotnet-home" \
DOTNET_CLI_TELEMETRY_OPTOUT=1 \
DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
dotnet publish .copilot-scratch/nuget-evidence-probe/plugin-app/PluginProbe.csproj \
  -c Release \
  -o .copilot-scratch/nuget-evidence-probe/plugin-out \
  --nologo \
  --verbosity quiet

PLUGIN_PROBE_LOG="$PWD/.copilot-scratch/nuget-evidence-probe/probe/plugin-args.log" \
NUGET_PLUGIN_PATHS="$PWD/.copilot-scratch/nuget-evidence-probe/plugin-out/PluginProbe.dll" \
DOTNET_CLI_HOME="$PWD/.copilot-scratch/nuget-evidence-probe/dotnet-home" \
DOTNET_CLI_TELEMETRY_OPTOUT=1 \
DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
dotnet restore .copilot-scratch/nuget-evidence-probe/probe/Probe.csproj \
  --source "https://pkgs.dev.azure.com/mseng/_packaging/VS-Core-Editor/nuget/v3/index.json" \
  --no-cache \
  --interactive \
  --verbosity quiet

PLUGIN_PROBE_LOG="$PWD/.copilot-scratch/nuget-evidence-probe/probe/plugin-args.log" \
NUGET_PLUGIN_PATHS="$PWD/.copilot-scratch/nuget-evidence-probe/plugin-out/PluginProbe.dll nuget plugin" \
DOTNET_CLI_HOME="$PWD/.copilot-scratch/nuget-evidence-probe/dotnet-home" \
DOTNET_CLI_TELEMETRY_OPTOUT=1 \
DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 \
dotnet restore .copilot-scratch/nuget-evidence-probe/probe/Probe.csproj \
  --source "https://pkgs.dev.azure.com/mseng/_packaging/VS-Core-Editor/nuget/v3/index.json" \
  --no-cache \
  --interactive \
  --verbosity quiet

rm -rf .copilot-scratch/nuget-evidence-probe
```

Observed result:

```text
publish_status=0
plain_restore_status=1
spaced_restore_status=1
cleanup_exists=no
```

Observed plain `NUGET_PLUGIN_PATHS` log:

```text
argv: [-Plugin]
cwd: /workspace/three-workspaces/azureauth-credprovider/.copilot-scratch/nuget-evidence-probe/probe
NUGET_PLUGIN_PATHS: /workspace/three-workspaces/azureauth-credprovider/.copilot-scratch/nuget-evidence-probe/plugin-out/PluginProbe.dll
```

Observed plain restore diagnostics:

```text
Problem starting the plugin '.../plugin-out/PluginProbe.dll'.
Plugin 'PluginProbe' failed within 0.057 seconds with exit code 0.
NU1301: Unable to load the service index for source https://pkgs.dev.azure.com/mseng/_packaging/VS-Core-Editor/nuget/v3/index.json.
NU1301:   Response status code does not indicate success: 401 (Unauthorized).
```

Observed spaced `NUGET_PLUGIN_PATHS` log:

```text
no plugin log
```

Conclusion from the local probe: for `dotnet restore`, direct `NUGET_PLUGIN_PATHS`
launch passes fixed `-Plugin` to the plugin path. Encoding a human-facing subcommand
after the plugin path, such as `<primary-cli> nuget plugin`, was not treated as a
plugin executable path by this probe and did not launch the disposable plugin.

## Plugin Launch Constraints

Evidence supports these constraints:

1. For locally probed `dotnet restore`, NuGet plugin launch invokes the plugin path
   with fixed `-Plugin` behavior.
2. For locally probed `dotnet restore`, `NUGET_PLUGIN_PATHS` points to plugin files,
   not to arbitrary commands with subcommands appended after the path.
3. A normal human-facing command such as `<primary-cli> nuget plugin` is not sufficient
   for the probed `dotnet restore` path unless the configured executable itself treats
   `-Plugin` as NuGet plugin mode.
4. Default setup should use conventional NuGet plugin discovery locations.
   `NUGET_PLUGIN_PATHS` should remain an advanced diagnostic or explicit override
   because upstream documentation warns about mixed `dotnet` and NuGet.exe use.
5. The local probe does not prove identical launch behavior for NuGet.exe, MSBuild,
   or Visual Studio. Those clients require later client-specific validation.

## Handshake and Authentication Flow Evidence

Source inspection confirms the reference provider's NuGet plugin flow:

1. `Program.cs` builds a `RequestHandlerCollection` with `Initialize`,
   `GetOperationClaims`, `GetAuthenticationCredentials`, `SetLogLevel`, and
   `SetCredentials`.
2. `Program.cs` enters plugin mode when `CredentialProviderArgs.Plugin` is set and
   creates a NuGet protocol plugin with `PluginFactory.CreateFromCurrentProcessAsync(...)`.
3. `InitializeRequestHandler` returns success for NuGet initialization.
4. `GetOperationClaimsRequestHandler` advertises `Authentication` only when both
   `PackageSourceRepository` and `ServiceIndex` are null. If either field is present,
   it returns an empty claim list.
5. `GetAuthenticationCredentialsRequestHandler` receives the NuGet authentication
   request, including package source URI, retry, non-interactive, and dialog capability
   inputs.
6. `VstsCredentialProvider` maps those inputs into token acquisition policy, tries
   bearer-token providers, and returns Basic credentials using either `VssSessionToken`
   or `EntraToken`.
7. The default session-token path exchanges a bearer token with Azure DevOps SPS
   through `VstsSessionTokenFromBearerTokenProvider` and `VstsSessionTokenClient`.
8. `VstsSessionTokenClient` restricts token exchange to allowed SPS hostnames before
   sending the bearer token.

## Runtime Packaging Constraints

Accepted constraints for later implementation:

1. A NuGet adapter artifact must include a plugin-shaped entry point that supports
   `-Plugin`.
2. `dotnet restore` scenarios require a `netcore` plugin layout.
3. NuGet.exe and MSBuild scenarios require the `netfx` plugin layout according to
   upstream documentation, but still require later execution validation for this
   product.
4. Conventional user plugin locations are the preferred default:
    - Windows: `%UserProfile%\.nuget\plugins\netcore\...` and
      `%UserProfile%\.nuget\plugins\netfx\...`.
    - Linux/macOS: `$HOME/.nuget/plugins/netcore/...`, with `netfx` only where the
      target client can consume it.
5. `NUGET_PLUGIN_PATHS` must be treated as an override or diagnostic input, not as the
   normal persistent setup path.
6. Linux self-contained artifacts may require native dependencies. Runtime-dependent
   and RID-specific artifact choices remain a later packaging decision, but the adapter
   design must preserve runtime-family separation.

## Validation and Checks

Commands run from repository root:

```bash
dotnet --version
dotnet --info | sed -n '1,45p'
dotnet nuget --version
git -C /workspace/public/artifacts-credprovider --no-pager rev-parse HEAD
git -C /workspace/public/artifacts-credprovider --no-pager remote -v
git -C /workspace/public/artifacts-credprovider --no-pager describe --tags --always --dirty
git -C /workspace/public/artifacts-credprovider --no-pager status --short
```

Results:

```text
dotnet --version: 10.0.300
dotnet nuget --version: NuGet Command Line 7.6.0.0
upstream mirror HEAD: 9c3840be1c97594708331b1797b0a2d9dce480b3
upstream mirror remote: https://github.com/microsoft/artifacts-credprovider.git
upstream mirror description: v2.0.1-9-g9c3840b
upstream mirror status --short: no output
```

Disposable probe validation:

```text
dotnet publish PluginProbe.csproj: exit 0
plain NUGET_PLUGIN_PATHS dotnet restore: exit 1, plugin log recorded argv [-Plugin]
spaced NUGET_PLUGIN_PATHS dotnet restore: exit 1, no plugin log recorded
cleanup check: .copilot-scratch/nuget-evidence-probe did not exist after cleanup
```

Markdown validation:

```bash
pnpm exec prettier --check src/private/app/azureauth-credprovider/docs/phase-1.1-nuget-evidence.md
pnpm exec markdownlint-cli2 src/private/app/azureauth-credprovider/docs/phase-1.1-nuget-evidence.md
```

Results: both commands exited 0.

## Risks and Follow-ups

- NuGet.exe, MSBuild, and Visual Studio plugin execution were not locally executed in
  this Linux environment. Keep Windows-first runtime validation in the later packaging
  and Phase 10/15 validation plan.
- Exact NuGet protocol version negotiation details should be implemented from
  `NuGet.Protocol.Plugins` API contracts during Phase 10. This gate confirms the
  required handler shape and message flow, not every protocol field.
- The final product name and executable names remain placeholders. Do not bake literal
  `<primary-cli>` or `<name>` values into plugin paths.
- Doctor checks should detect conventional plugin layout, runtime family compatibility,
  direct `NUGET_PLUGIN_PATHS` overrides, and cases where an override shadows
  conventional discovery.

## Affected Requirements and Designs

- `requirements.md`: NuGet integration requirements 1 through 6 are evidence-supported
  for the scoped evidence above.
- `high-level-design.md`: NuGet adapter entry point and discovery model are
  evidence-supported for the scoped evidence above.
- `mid-level-design.md`: NuGet adapter plugin modes, request mapping, and runtime layout
  are evidence-supported for the scoped evidence above.
- `project-breakdown.md`: Phase 1.1 exit criterion is satisfied with a pass decision.

[args-plugin]: https://github.com/microsoft/artifacts-credprovider/blob/9c3840be1c97594708331b1797b0a2d9dce480b3/CredentialProvider.Microsoft/CredentialProviderArgs.cs#L17-L44
[auth-handler]: https://github.com/microsoft/artifacts-credprovider/blob/9c3840be1c97594708331b1797b0a2d9dce480b3/CredentialProvider.Microsoft/RequestHandlers/GetAuthenticationCredentialsRequestHandler.cs#L44-L118
[claims-handler]: https://github.com/microsoft/artifacts-credprovider/blob/9c3840be1c97594708331b1797b0a2d9dce480b3/CredentialProvider.Microsoft/RequestHandlers/GetOperationClaimsRequestHandler.cs#L22-L52
[initialize-handler]: https://github.com/microsoft/artifacts-credprovider/blob/9c3840be1c97594708331b1797b0a2d9dce480b3/CredentialProvider.Microsoft/RequestHandlers/InitializeRequestHandler.cs#L25-L28
[program-handlers]: https://github.com/microsoft/artifacts-credprovider/blob/9c3840be1c97594708331b1797b0a2d9dce480b3/CredentialProvider.Microsoft/Program.cs#L98-L105
[program-plugin]: https://github.com/microsoft/artifacts-credprovider/blob/9c3840be1c97594708331b1797b0a2d9dce480b3/CredentialProvider.Microsoft/Program.cs#L150-L162
[program-standalone]: https://github.com/microsoft/artifacts-credprovider/blob/9c3840be1c97594708331b1797b0a2d9dce480b3/CredentialProvider.Microsoft/Program.cs#L179-L202
[readme-install]: https://github.com/microsoft/artifacts-credprovider/blob/9c3840be1c97594708331b1797b0a2d9dce480b3/README.md#L64-L93
[readme-linux-install]: https://github.com/microsoft/artifacts-credprovider/blob/9c3840be1c97594708331b1797b0a2d9dce480b3/README.md#L95-L115
[readme-plugin-help]: https://github.com/microsoft/artifacts-credprovider/blob/9c3840be1c97594708331b1797b0a2d9dce480b3/README.md#L296-L311
[session-token-client]: https://github.com/microsoft/artifacts-credprovider/blob/9c3840be1c97594708331b1797b0a2d9dce480b3/CredentialProvider.Microsoft/CredentialProviders/Vsts/VstsSessionTokenClient.cs#L22-L104
[session-token-provider]: https://github.com/microsoft/artifacts-credprovider/blob/9c3840be1c97594708331b1797b0a2d9dce480b3/CredentialProvider.Microsoft/CredentialProviders/Vsts/VstsSessionTokenFromBearerTokenProvider.cs#L27-L61
[vsts-provider]: https://github.com/microsoft/artifacts-credprovider/blob/9c3840be1c97594708331b1797b0a2d9dce480b3/CredentialProvider.Microsoft/CredentialProviders/Vsts/VstsCredentialProvider.cs#L100-L188
