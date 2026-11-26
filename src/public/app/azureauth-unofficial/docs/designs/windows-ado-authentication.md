# Windows Azure DevOps Authentication Slice

This record owns the concrete Windows request implementation design and the protocol 1
command-line/process semantics. The linked JSON Schemas own serialized field shapes.
Requirements remain authoritative for required behavior; the
[architecture](../architecture/overview.md) owns system-wide boundaries. This is a
design record, not an activated Client Profile or support claim. The Slice is implemented
under `src/` and `tests/`; the [validation strategy](../validation/strategy.md#windows-slice-requirement-to-evidence-ledger)
owns its evidence and the release gates that remain open.

## Selected Boundary and Dependencies

A WSL caller explicitly launches one Windows executable for one authentication request.
The same request design covers a selected personal Microsoft account and a selected work
account. Git and Azure Artifacts use the caller-supplied Azure DevOps scope; repository,
organization, feed, and package operations stay with the caller. The engine returns the
original delegated access token. The [PAT prohibition](../product/requirements/product-boundary.md#v2-req-005-no-personal-access-tokens)
applies to every operation, configuration, and failure path.

| Choice | Concrete design and reason |
| --- | --- |
| Runtime and UI | C# on .NET 10 LTS, `net10.0-windows`, `win-x64`; SDK 10.0.403 and the runtime it bundles, 10.0.14, with roll-forward disabled. A small in-process Win32 window replaces the Windows Forms host in the design below. It requires no Windows Desktop managed framework. |
| Authentication dependencies | `Microsoft.Identity.Client` and `Microsoft.Identity.Client.Broker` 4.83.1, with `Microsoft.Identity.Client.NativeInterop` 0.20.3. Preserve this authentication dependency baseline. |
| Provider | Windows WAM only. One real-account discovery, at most one selected-account silent call for the two existing modes, and at most one permitted or explicitly required interactive call. Forced interaction skips silent acquisition. No application-level network retry or second provider. |
| State | Broker-owned reusable state; a fresh in-memory MSAL application per invocation. No MSAL Extensions cache helper, serialized MSAL cache, shadow refresh-token store, engine account binding, or cross-process lock. |
| Host | Windows 11 x64 in an active interactive user session, with WAM available. WSL 2 callers use normal Windows executable interoperability. Service, impersonated, disconnected desktop, and native Linux hosts are outside this Slice. |
| Distribution | An unsigned portable `win-x64` zip, `azureauth-unofficial-<version>-win-x64.zip`, that the user extracts into a directory of their choice; see [Portable Package and Version](#portable-package-and-version). The [registry](../governance/operational-identities.yaml) owns the selected identities. `<windows-cli>` below denotes the caller-selected path of `azureauth-unofficial.exe`; nothing is placed on `PATH`. |

The [dependency assessment](../research/experiments/windows-slice-validation.md#windows-slice-dependency-and-host-contracts)
binds these choices to public source. Future upgrades must use the existing dependency
review/validation matrix.

### Native AOT Target Disposition

Select **Native AOT** for the one .NET 10 `net10.0-windows` / `win-x64` executable
with the small in-process Win32 host, under
[V2-REQ-055](../product/requirements/quality-build-and-validation.md#v2-req-055-native-aot-publishing).
Retain SDK 10.0.403 with its bundled runtime 10.0.14, MSAL/Broker 4.83.1 and
NativeInterop 0.20.3.
The existing Native AOT and real-host evidence is bound to the 10.0.12 runtime packs
and the source revisions its records name; it is not evidence for 10.0.13 or 10.0.14.
No non-AOT exception is needed. This selection is not complete-application
compatibility, release or support acceptance.

The [Native AOT assessment](../research/experiments/windows-native-aot.md#windows-native-aot-assessment)
owns the public toolchain/Win32 contracts and the bounded synthetic observations that
support this path without changing authentication dependencies or relaxing DLL search,
together with their limits. The [validation basis](../validation/strategy.md#native-aot-publishing)
retains complete-application, WAM/UI, account/Profile, dynamic-dependency, symbol,
performance, release and support obligations.

The [public AOT assessment](../research/experiments/windows-native-aot.md#windows-native-aot-assessment)
distinguishes the following alternatives:

| Alternative | Disposition and reason |
| --- | --- |
| Existing Windows Forms host | Cannot be selected as a supported Native AOT route: Native AOT requires trimming, and Microsoft disables supported Windows Forms trimming because of built-in COM dependencies. Keeping this host would require a justified non-AOT exception; it is not necessary merely to own an HWND. |
| WPF replacement | Its documented trimming limitation does not resolve the blocker. |
| Small Win32 host with static interop | Selected Native AOT path: the required parent HWND, completion and cancellation handling need only Win32 window APIs, an owned message loop, and statically known callbacks. This removes the managed desktop-framework blocker without another process or provider. The [parent selection decision](#parent-selection-decision) separately fixes that parent as hidden, without controls. |
| Larger UI framework or direct broker rewrite | No present UI requirement justifies another framework's dependency surface or replacing the supported MSAL integration with direct broker internals. Neither is needed for the selected smaller host. |
| Same Win32 host with ordinary self-contained JIT publishing | Future comparison baseline, not an accepted fallback or exception. Consider an exception only if later application validation identifies an AOT blocker that cannot reasonably be remediated, with the requirement's evidence and reassessment obligations. |

Enable `PublishAot` in the executable project, retaining AOT/trim
analysis during development. Owned libraries participate in compatibility analysis
without executable publishing settings. Do not
use blanket warning suppression, add dynamic plugins, or equate ReadyToRun/trimming with
Native AOT. Use source-generated P/Invoke for the finite owned Win32 API surface and
static `UnmanagedCallersOnly` callbacks with explicit ABI/layout. Built-in COM, runtime
code generation, reflection-based activation, and C++/CLI are not part of this host.

Read Profile JSON with a bounded `Utf8JsonReader`/explicit field reader, retaining the
existing duplicate-key, unknown-field, version, depth, and semantic validation. Write
result/event JSON from the existing field allowlists with `Utf8JsonWriter`; never serialize
arbitrary provider objects. These choices do not change the schemas or loosen input
validation. MSAL's own .NET 8 asset uses its generated JSON context; the separate Broker
asset targets .NET Standard 2.0 and does not inherit that AOT annotation automatically.

The resolved graph selects the public net8.0 Client, netstandard2.0 Broker,
net9.0 NativeInterop and `win-x64/msalruntime.dll` assets. Its loader/allocation evidence
applies to those exact assets; the older .NET Standard loader report and package metadata
do not replace that observation. The actual application's resolved graph must retain
reviewed public provenance and account for every native transitive requirement. Retain
the three authentication pins; changes require the existing dependency review.

Native assets belong in the application deployment directory with their notices. Before
provider initialization, constrain process DLL search to the application directory and
System32 with supported Windows loader controls; reject user-configured native paths.
Do not rely on the working directory, ambient `PATH`, development installations, runtime
extraction, or `Assembly.Location`. Do not enable direct P/Invoke for the broker library:
Native AOT's default binding and OS direct binding have different search semantics. The
exact upstream loader, its dependencies, and the process search restriction must be
validated together on the actual application; an incompatible loader makes the target
unavailable rather than silently weakening search rules or copying private runtime internals.

A product publish must pin the SDK/runtime inputs, resolved NuGet graph, and publish
properties before execution, and must pin the Windows x64 public native build chain
(Visual Studio C++ tools, Windows SDK, and `link.exe`). Microsoft's documented Windows
prerequisite is Visual Studio 2022 or later with Desktop development with C++ and its
default components; this record does not select a floating installed compiler. The one
exception: for `0.1.0-alpha` prereleases the repository owner accepted the floating chain
of the GitHub-hosted runner image instead of pinning it; the
[release workflow](#release-workflow) records the installed chain on every run and the
release review checks it. Artifact closure must include the native broker and
OS prerequisites; an AOT executable does not imply one-file deployment or no OS dependency.
Debug symbols have a separate diagnostic/release treatment and are included when
comparing total development and distribution costs.

The implemented application must be verified against the existing Windows scenario
basis and the release gates before the corresponding claim. Synthetic success does not satisfy real WAM, account, UI or cancellation
gates. Investigate precise supported dependency or host corrections if application
validation finds a blocker, before proposing a scoped exception.

### C4 Deployment View

This view answers which process owns authentication, configuration, and cancellation.
Blue nodes are engine-owned; gray nodes are caller or platform dependencies.

```mermaid
flowchart LR
    subgraph workstation["Developer workstation [Deployment node]"]
        subgraph wsl["WSL 2 distribution [Execution environment]"]
            caller["Git or package adapter<br/>[External system instance]"]
        end
        subgraph windows["Windows interactive user session [Execution environment]"]
            cli["Windows CLI + MSAL<br/>[Container instance: .NET 10 process]<br/>Native AOT; owns a hidden Win32 parent HWND, no controls"]
            profile[("Explicitly selected Profile file<br/>[External data store: caller-managed JSON]")]
            wam["WAM and protected reusable state<br/>[External OS system]"]
        end
    end
    identity["Microsoft identity platform<br/>[External system]"]
    resource["Azure DevOps Git / Artifacts<br/>[External system]"]
    caller <-->|Arguments, result, stderr, exit<br/>Optional stdin lifetime pipe| cli
    profile -->|One bounded read| cli
    cli <-->|Real accounts, token operations, cancellation| wam
    wam <-->|Authentication and issuance| identity
    cli -->|Authority discovery through MSAL| identity
    caller -->|Direct token presentation and service operations| resource
    classDef owned fill:#1168bd,color:#fff,stroke:#0b4884
    classDef external fill:#e5e7eb,color:#111827,stroke:#6b7280
    class cli owned
    class caller,profile,wam,identity,resource external
```

### Portable Package and Version

The CLI project's assembly name makes the published executable
`azureauth-unofficial.exe`. `packaging/windows/package.ps1` builds the portable archive on
Windows x64 from a clean full Git clone: it refuses a dirty or shallow worktree,
restores in locked mode from the repository's nuget.org-only configuration, publishes
Native AOT, and admits exactly this content:

| Archive path | Source |
| --- | --- |
| `azureauth-unofficial.exe` | Native AOT publish output. |
| `msalruntime.dll` | NativeInterop's `win-x64` asset from the same publish; it must stay beside the executable because loading is restricted to the application directory and System32. |
| `profiles/visual-studio-legacy-wam.json` | `packaging/windows/profiles/`, JSON-equal to the schema's [Visual Studio legacy candidate](#visual-studio-legacy-candidate) (same members and values); a test asserts that equality and that the product reader accepts the file. |
| `README.md` | `packaging/windows/README.md`: unofficial notice, extraction, Windows and WSL use, the bundled Profile's ownership and limits, unsigned and SmartScreen notes, and removal. |
| `LICENSE.txt` | The repository license. |
| `THIRD-PARTY-NOTICES.txt` | A fixed header for the MSAL components and the .NET runtime notices from the locked ILCompiler runtime package. |

Symbol files (the native `azureauth-unofficial.pdb`, which native debugging and
profiling of the build require, and those of the project's own managed assemblies) stay
out of the archive; any other publish output fails the script. Before writing the zip,
the script checks with `dumpbin /dependents` that both native images import only
System32 DLLs and API sets, rejecting Visual C++ redistributable DLLs by name because
they are not part of Windows even when a development host has them in System32. The
script then checks the version resource
described below and runs the staged executable's `--version` from its own directory
with `PATH` limited to System32, and finally writes the zip and a SHA-256 file. The
four text files are written with LF line endings whatever the checkout's Git
configuration, and the script fails if any contains a carriage return. Beside them it
writes `azureauth-unofficial-<version>-win-x64-symbols.zip`, holding the symbol files and
the same `LICENSE.txt` and `THIRD-PARTY-NOTICES.txt`, with its own SHA-256 file; the
script refuses to overwrite any of these outputs.
These checks are packaging checks on the build host, not evidence of deployment without
development-machine dependencies.

Nerdbank.GitVersioning, a build-only package, computes the version from
`version.json` and Git history: `0.1.0-alpha.{height}` in SemVer 2.0, with `main-v2`
as the only public-release ref; a build from another ref appends a `g<commit>`
prerelease identifier. The product uses two forms:

- the version without build metadata for the archive name, the managed User-Agent
  product token and the `ActivitySource` version;
- that version plus `+<10-hex commit>` build metadata for `--version` and help.

The Win32 version resource carries the numeric `0.1.0` file version and
Nerdbank.GitVersioning's informational version, `<version>+<10-hex commit>`, as its
product version; the packaging script checks both on the published executable. The
informational version omits the `g<commit>` prerelease identifier, so it equals the
`--version` output only on a public-release build. The script therefore refuses a
build that is not a public release unless `-AllowNonPublicRelease` is passed for
development validation. A public-release build needs `main-v2` checked out, with
`HEAD` on `refs/heads/main-v2`; a detached `HEAD` at the same commit is not a
public release, and the `PublicRelease` property override is not used.
Version computation needs a full Git clone: a shallow clone fails the build, and a tree
without `.git`, such as a `git archive` copy, does not build. CI therefore checks out
with `fetch-depth: 0`.


### Release Workflow

`.github/workflows/release.yml` builds `0.1.0-alpha` prereleases. It starts only by hand
and, in its `main-v2` copy, refuses any ref other than `refs/heads/main-v2`, so the build
is a public release. A copy of the workflow on another ref is not bound by that guard;
the release review checks that the run and its attestation come from
`.github/workflows/release.yml` at `refs/heads/main-v2` and the built commit.

Its jobs have least privilege. The build job reads contents only. It restores in locked
mode with the checksum-pinned SDK, runs the Windows scenarios excluding
`WindowsHostTiming`, runs `package.ps1` without its development switches, and records in
`build-info.json` and the run summary the runner image, the .NET SDK, and the installed
chain that the Native AOT linker resolves: the Visual Studio that `vswhere -latest
-prerelease` finds, as ILCompiler does, its default MSVC toolset and `link.exe`, and the
installed Windows SDKs. That this is the chain the linker used is an inference. A second
job holds only `id-token: write` and `attestations: write` and creates a GitHub
build-provenance attestation for both zips. A third holds only `contents: write`: it
verifies the checksums and the recorded commit, refuses a version whose tag, release or
draft exists (failing on any lookup error other than not found), and creates a draft
prerelease `v<version>` targeting the built commit, with the package zip, the symbols zip
and their `.sha256` files. The jobs pass files as workflow artifacts kept for one day.
The repository owner publishes each draft after the release review.

## Protocol 1 Command-Line Contract

Invoke `<windows-cli> authenticate` followed by the arguments below. Options and enum
values are case-sensitive. Use separate option and value arguments; short aliases,
`--name=value`, positional targets, response files, and repeated scalar options are
invalid. Windows argument quoting is the caller's responsibility; the engine consumes
the resulting argument vector without invoking another shell.

| Argument | Meaning |
| --- | --- |
| `--protocol 1` | Required protocol major. No implicit version. |
| `--profile <absolute-Windows-file-path>` | Required selection of one Profile document, not inline client configuration. |
| `--account-email <full-email>` | Required strict account constraint. |
| `--scope <qualified-scope>` | Required, repeatable for permissions on exactly one resource. |
| `--interaction non-interactive-only\|interactive-if-needed\|force-interactive` | Required interaction intent for the entire request. The first two modes retain silent-first behavior; the third requests interactive account selection without engine silent token acquisition. |
| `--tenant common\|<tenant-GUID>` | Optional; normalize under the selected Profile's tenant policy. |
| `--timeout-seconds <integer>` | Optional, 1 through 600 inclusive; default 120. The value is an unsigned ASCII decimal without sign, leading zeros, whitespace or separators. One monotonic deadline measured from managed entry, including Profile reading and validation. |
| `--cancel-on-stdin-close` | Optional valueless lifetime flag. Use a dedicated caller-owned pipe whose writer remains open until completion or cancellation. EOF or pipe failure cancels the request. Stdin is never an authentication request or credential channel. |
| `--telemetry off\|stderr` | Optional, default `off`. Enables only the bounded local event stream described below; it does not configure network export. |

The [request schema](../../contracts/v1/request.schema.json) describes the parsed argument
projection for validators and synthetic cases; it is not a stdin-JSON API. Defaults are
applied only after syntax validation. Duplicate scopes are rejected rather than silently
rewritten. Unknown options, absent required options, malformed values, unsupported
versions, and conflicts with a Profile produce `invalid_request` before provider work.
No environment variable supplies request intent or Profile selection.

`help` and root `--help` are non-authentication entry points and may print help to stdout.
The help text states that the project is unofficial and not a Microsoft release, under
[V2-REQ-003](../product/requirements/product-boundary.md#v2-req-003-unofficial-product-identity).
Root `--version` is the third non-authentication entry point: it prints one LF-terminated
line, `azureauth-unofficial <version>` with the full version defined under
[Portable Package and Version](#portable-package-and-version).
Every attempted authentication invocation, including malformed ones, uses the terminal
result contract. An unsupported or absent version returns a protocol 1 `invalid_request`
envelope with a safe reason; it does not echo the unsupported input. This bootstrap
failure envelope remains recognizable independently of later supported major versions.

### Scope and Email Normalization

Accept a nonempty full email with one `@` and no whitespace, control characters or
U+FEFF, up to 320 characters. Preserve spelling and compare the whole string using ordinal
case-insensitive equality. Do not strip aliases, fold dots, rewrite domains, apply
Unicode normalization, or create an account-kind selector.

Each scope must contain an explicit resource prefix and a final permission segment.
A scope must not contain whitespace, control characters or U+FEFF.
Split at the last `/`: the prefix must be a GUID or an absolute application-ID URI with
an authority, no userinfo, query, or fragment; the last segment must be nonempty. This
covers the Azure DevOps GUID prefix and URI resources such as `api://<application-id>`
without a resource catalog. Reject unqualified permissions and identity-protocol scopes
as caller resource targets. MSAL may add its own OAuth protocol scopes internally.
Require the same resource prefix in all requested scopes using ordinal equality; do not
guess equivalence between different resource spellings. `/.default` must be the only
scope. For dynamic scopes, compare full provider-reported scope strings using ordinal
equality, allowing extra granted scopes. Missing coverage fails closed. For `/.default`,
validate the request/result association for the same resource and operation rather than
expecting its literal spelling in returned permissions.

These are deterministic core rules with table-driven unit-test value. They do not parse
access tokens or infer service authorization.

Every GUID in the request or Profile, namely the `--tenant` value, a GUID resource
prefix, and the Profile `clientId` and `tenantId`, must use the canonical 8-4-4-4-12
hexadecimal form that the contracts' patterns state, in either letter case. Reject any
other spelling, such as braces, `0x` or `+` segment prefixes, or whitespace, instead of
parsing and rewriting it.

## Profile Contract and Ownership

The [Profile schema](../../contracts/v1/client-profile.schema.json) owns configuration
fields. A Profile has its own `schemaVersion`, name, client ID, Public Cloud selection,
tenant policy, integration kind, and explicit registration ownership/support metadata.
Protocol major and Profile schema version are separate version spaces. The current
reader accepts version 1 only and rejects unknown fields and duplicate JSON keys.
The Profile name and registration owner are bounded plain text: they reject C0 and C1
control characters and Unicode format characters (category Cf, including bidirectional
controls and U+FEFF), so that the pre-interaction indication cannot be visually
reordered or hidden.

`--profile` is the sole lookup mechanism. Open that file once, read at most 64 KiB as
strict UTF-8 JSON, validate it, and retain an immutable in-memory snapshot for the request.
One leading UTF-8 byte order mark (`EF BB BF`), as common Windows editors write, is
accepted and skipped before parsing; it counts toward the 64 KiB bound. Any other byte
order mark, a repeated one, or one after other content remains invalid.
The path must be an absolute drive-qualified path on a local fixed Windows volume;
relative, drive-relative, UNC, device, and Linux paths are invalid. No environment or home
expansion, directory search, URL retrieval, symlink-management policy, or Profile writer
is introduced. The caller owns file provisioning, permissions, update, and deletion;
the engine never modifies the file. Trust ordinary OS access control within the current
user threat model. A read error is `invalid_request` with a safe configuration reason;
an expired deadline remains `timeout`. Do not report raw paths or file contents.

Configuration is public and nonsecret, but its integrity matters. The explicit file
selection and strict semantic checks prevent accidental use of an unrelated registration.
The engine does not search upstream locations or import upstream configuration. WSL
callers convert their chosen file path to Windows form before invocation. This explicit
caller-managed file requires no engine-owned configuration namespace or persistent
configuration service.

The two integration values filter compatible provider behavior without choosing an
acquisition order:

- `windows-wam`: ordinary public-client WAM integration, with no MSA passthrough.
- `visual-studio-legacy-wam`: Public Cloud only, client ID exactly
  `872cd9fa-d31f-45e0-9eab-6e460a02d1f1`, multitenant policy, and the legacy mapping
  already defined by the [client-identity architecture](../architecture/client-application-identity.md#provider-mapping).
  Only this integration enables the provider's legacy MSA passthrough option.

The client ID check of `visual-studio-legacy-wam` is an integration-specific identity pin
permitted by
[V2-REQ-042](../product/requirements/cache-security-and-operational-identity.md#v2-req-042-client-registration-as-configuration):
the Profile still supplies the client ID, and the pin only rejects a Profile whose value
differs from that registration. Both integrations and the single-tenant policy are defined
here; only the legacy integration with multitenant policy has real-host evidence (see the
[known limitations](#validation-and-explicit-unsupported-cases)).

Both derive the broker redirect URI as `ms-appx-web://microsoft.aad.brokerplugin/<client-id>`.
Authority validation is enabled, multi-cloud behavior is disabled, and the trusted cloud
host is `login.microsoftonline.com`. Profiles cannot supply arbitrary hosts, redirect
code, secrets, scopes, cache modes, ordering, deadline defaults, prompt preferences,
telemetry destinations, or PAT behavior. Unsupported integrations fail admission.

### Visual Studio Legacy Candidate

The named example in the Profile schema is the concrete candidate definition. It is
schema documentation, not an installed/enabled Profile. An implementation must not
silently materialize or select it. Under decision
[0003](../decisions/0003-treat-client-registration-as-an-external-dependency.md)'s
prerelease exception, the [portable package](#portable-package-and-version) carries the
same document as a file; it uses the same parser and explicit path selection as a
user-created file, and nothing selects it by default.

Microsoft owns the registration. AzureAuth and GCM publicly use it; this fork has no
upstream endorsement, support commitment, or control over account eligibility, consent,
policy, or availability. Authentication, consent, and audit records can identify the
Visual Studio application rather than this fork. The best-effort pre-interaction stderr
indication identifies the fork, names the Profile and external owner as bounded plain
text, and states that the provider display continues sign-in; the fixed broker title
identifies the fork. Neither impersonates Visual Studio or promises a particular
Security Key, PIN, or MFA choice.

State sharing under this registration is intentional only through WAM APIs and compatible
account, tenant, client, resource, and Windows-user context. It does not authorize reading
Visual Studio, AzureAuth, or GCM serialized caches. No app-owned persisted state exists
to collide with those products. Changing Profile names alone neither changes the OAuth
client identity nor creates a new broker cache partition.

Before activation or general distribution, the existing
[external Profile gate](../product/compatibility-and-migration.md#externally-owned-client-profile-gate)
still needs bounded personal/work-account, host/redirect, consent/audit/branding, strict
result, and reuse evidence. Registration rejection returns the existing failure outcome;
there is no alternate registration, PAT, or SelfDescribing fallback. Those acceptance
obligations are distinct from selecting this candidate's concrete design.

## Cohesive Request Implementation

Use one executable and one request coordinator. The CLI parser/serializer, Windows host,
and MSAL adapter are dependency boundaries; pure account, tenant, scope, and terminal
outcome rules are ordinary functions. Do not create a service/container/plugin framework
for these responsibilities. A provider seam and a host/clock seam suffice for scenario
validation; the number of conceptual architecture boxes is not a class count.

### Admission and Provider Setup

1. Establish the monotonic request start, bounded deadline, cancellation latch, and one
   terminal-result gate. Parse and validate the arguments and Profile before any account
   discovery or authentication. Honor cancellation during admission.
2. Normalize tenant policy once. A fixed Profile uses its fixed GUID and rejects a
   conflicting selector with `invalid_request` and the distinct safe reason
   `tenant_conflict`. Multitenant omission becomes `common`; an explicit GUID remains
   an exact resource-tenant constraint throughout.
3. Keep the provider factory and constructor inert. At the start of its first
   `GetAccountsAsync(originalToken)`, complete the local host admission below on request
   work, then construct a fresh `PublicClientApplication` using the chosen Profile and
   trusted cloud, with Windows broker enabled and `ListOperatingSystemAccounts = true`.
   Use no persistent cache callbacks, MSAL logging callback, default OS account sentinel,
   `cp1`, PoP, or username/password API. Keep provider PII/default logging disabled.
4. Check broker availability using the pinned synchronous `IsBrokerAvailable()` API;
   confine that dependency to the adapter. Install a rejecting
   `ICustomWebUi` before any interactive call so an MSAL fallback cannot open a browser
   if broker availability changes. Do not implement an OAuth exchange in that callback.
5. Use the concrete `ClientApplicationBase.GetAccountsAsync(CancellationToken)` overload,
   which exists even though the interface's older overload omits cancellation. Thread
   the original request cancellation token into it and every token operation.

Profile/client identity is bound by the application instance and its request-local
operation context. It is not inferred from token text. Every candidate carries that
context back to the coordinator; a callback for another or ended operation is discarded.

### Local Windows Host Admission

Microsoft's [WAM integration contract](https://learn.microsoft.com/entra/msal/dotnet/acquiring-tokens/desktop-mobile/wam#integration-best-practices)
requires an active interactive Windows user session capable of displaying UI, even for
silent acquisition. The checks below implement the selected host boundary; passing them
does not establish WAM health or broaden platform support. Perform them before creating
the real MSAL application or checking broker availability. A synthetic provider does not
need to inspect real logon state merely to exercise the owned window.

| Required local condition | Selected observation and rejection boundary |
| --- | --- |
| Windows 11 client on native x64 | Check Windows before any Windows import. Require both process and OS architecture to be x64, excluding x64 emulation on ARM64. Use `Environment.OSVersion` for NT 10.0, build at least 22000, and a positive workstation-product comparison with `VerifyVersionInfoW` / `VerSetConditionMask`, selecting only `VER_PRODUCT_TYPE`, `VER_EQUAL`, and `VER_NT_WORKSTATION`. A failed comparison rejects admission. |
| Calling thread is not impersonating | `OpenThreadToken(GetCurrentThread(), TOKEN_QUERY, TRUE, ...)` must fail specifically with `ERROR_NO_TOKEN`. A returned token, anonymous impersonation, or any other failure rejects admission. Capture the native error immediately and close any returned token. |
| Own process has an interactive local logon | Open only the current process token with `TOKEN_QUERY`. Use `GetTokenInformation(TokenStatistics).AuthenticationId` to query that one logon through `LsaGetLogonSessionData`. Require sufficient returned structure size, the matching LUID and a valid nonnull SID; reject LocalSystem, LocalService and NetworkService. Accept only `Interactive`, `RemoteInteractive`, `CachedInteractive` or `CachedRemoteInteractive`. Other types, including `NewCredentials`, reject admission. |
| Current session is actively connected | Obtain the current process's nonzero session ID with `ProcessIdToSessionId`. Query only that local session with `WTSQuerySessionInformationW(WTSConnectState)` and require a correctly sized `WTSActive` value. Unknown, disconnected, malformed or unavailable state rejects admission. |
| Visible station belongs to the local logon user | On the borrowed `GetProcessWindowStation` handle, require successful `GetUserObjectInformationW(UOI_FLAGS)` with `WSF_VISIBLE`. Require a bounded valid nonempty `UOI_USER_SID`. Accept exact equality to the own-logon account SID, or, only for an associated SID classified as `WinLogonIdsSid`, exact equality to the logon SID returned by `GetTokenInformation(TokenLogonSid)` on that same current process token. Missing associated user, mismatch or failed observation rejects admission. |
| Calling desktop is the current input desktop | Obtain the borrowed `GetThreadDesktop(GetCurrentThreadId())` handle and a non-inheritable `OpenInputDesktop(0, FALSE, DESKTOP_READOBJECTS)` handle in the current process station. Require complete bounded `UOI_NAME` observations with identical nonempty names. Require an active current session both before and after this comparison. Unavailable or malformed observations, a different desktop, or inactive session reject admission. An input-desktop handle alone does not satisfy this condition. |

The [.NET OS architecture contract](https://learn.microsoft.com/dotnet/core/compatibility/interop/7.0/osarchitecture-emulation)
distinguishes the host from emulation. [.NET's OS version property](https://learn.microsoft.com/dotnet/api/system.environment.osversion?view=net-10.0)
supplies actual version discovery. The workstation comparison follows the public
[VersionHelpers.h product-type test](https://github.com/microsoft/win32metadata/blob/b07213e28bcc48221c155024f9c5e0e1a92c6497/generation/WinSDK/RecompiledIdlHeaders/um/VersionHelpers.h#L135-L142);
`IsWindowsServer` itself is an inline helper, not an exported entry point. Do not use
the deprecated, manifest-sensitive `VerifyVersionInfoW` API to discover major/minor/build.
Its [type-mask contract](https://learn.microsoft.com/windows/win32/api/winbase/nf-winbase-verifyversioninfow)
ignores unselected fields. Build 22000 identifies the
[original Windows 11 generation](https://learn.microsoft.com/windows-insider/older-windows-insider-preview-builds#windows-11,-version-21h2-original-release),
not compatibility with every later product or servicing release.

The [thread-token API](https://learn.microsoft.com/windows/win32/api/processthreadsapi/nf-processthreadsapi-openthreadtoken)
does not revert impersonation when `OpenAsSelf` is true. The
[own-logon query](https://learn.microsoft.com/windows/win32/api/ntsecapi/nf-ntsecapi-lsagetlogonsessiondata)
requires no administrator role for the session owner. Its
[logon-type contract](https://learn.microsoft.com/windows/win32/api/ntsecapi/ne-ntsecapi-security_logon_type)
distinguishes `NewCredentials`, which can keep the local identity while replacing outbound
credentials. The LSA session's account SID and a token's logon SID represent different identities.
The [token-information contract](https://learn.microsoft.com/windows/win32/api/winnt/ne-winnt-token_information_class)
returns the latter as `TOKEN_GROUPS` for `TokenLogonSid`; the
[group contract](https://learn.microsoft.com/windows/win32/api/winnt/ns-winnt-token_groups)
identifies it with `SE_GROUP_LOGON_ID`. The
[well-known SID classes](https://learn.microsoft.com/windows/win32/api/winnt/ne-winnt-well_known_sid_type)
include `WinLogonIdsSid` (21). Do not compare a logon SID to an account SID and interpret
inequality as a different user. Preserve the account-SID comparison, then allow the
same-token logon-SID comparison only when the associated station SID has that class.
Use one fixed bounded query, not arbitrary group enumeration or a SID synthesized
from the authentication LUID. On the admitted x64 ABI, accept exactly one group,
require `SE_GROUP_LOGON_ID`, and validate its SID pointer and complete SID bytes within
the returned buffer before native validation or equality. A failed or malformed query
remains unavailable; a valid unequal SID remains a mismatch. Read no token names,
foreign tokens or account metadata, and export no SID, group attributes or pointers.

The historical
[Winlogon interface](https://learn.microsoft.com/windows/win32/api/winwlx/nf-winwlx-wlxloggedoutsas)
describes using a logon-session SID to protect the station and desktop. This motivates
handling the distinct representation; it does not prove which representation the
current Windows host returns or establish the cause of a failed acceptance attempt.
The account-SID branch detects a different local user but does not prove equal logon
sessions. The logon-SID branch requires exact same-token logon identity. Neither branch
exhaustively detects same-user alternate-launch provenance. Retain normal launch as an
acceptance precondition; do not silently require `LOGON_WINLOGON`, another user's token,
a shell process, or privileged `WTSQueryUserToken` access.

The [WTS query](https://learn.microsoft.com/windows/win32/api/wtsapi32/nf-wtsapi32-wtsquerysessioninformationw)
can fail when Remote Desktop Services is unavailable. A session ID is not substitute
connection evidence; return unavailable without starting or repairing the service. Do
not impose a physical-console-only condition. The
[user-object information contract](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getuserobjectinformationw)
defines associated-user SID and object-name observations. The
[thread-desktop contract](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-getthreaddesktop)
returns the calling thread's borrowed desktop. The
[input-desktop contract](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-openinputdesktop)
resolves the input desktop in the current process's station; it can instead return the
desktop that will become active when a disconnected session reconnects. Preserve the
active-session predicate and recheck it after the desktop observation. The
[desktop namespace contract](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-createdesktopw)
places desktops in the current station and opens the existing object for an existing
name. Compare the two returned names exactly, without normalization or case folding,
while both handles remain live. A matching arbitrary name from another station is not
sufficient; this process never changes its station, and both handles come from that
station. Do not enumerate, attach to, switch, or create a desktop.

Use one fixed 512-byte UTF-16 buffer per name observation, without size discovery or
retry. Validate the returned byte count, even alignment, nonempty name, final NUL and
absence of embedded NULs before comparing the complete names. Overlong, unavailable or
malformed observations reject admission. Names remain local and transient; no name,
handle or name hash enters results, logs or telemetry. Close the owned input-desktop
handle once in `finally`; failure to close is a host fault. Keep the calling-thread
handle borrowed. This selects name equality to the resolved input desktop rather than
requiring `UOI_IO` as an additional eligibility flag. It makes no claim about the cause
of an earlier `UOI_IO` rejection or OS behavior beyond the cited API contracts. Neither
visible station surfaces nor `OpenInputDesktop` success alone proves all required
conditions.

Recheck the original token before and after each synchronous observation, before
classifying its result or releasing the next effect. A rejected or unobservable required
condition yields the existing sanitized `mechanism_unavailable`; cancellation/deadline
retain precedence and programming faults retain `internal_failure`. No native diagnostic,
SID, LUID, session ID or returned account metadata enters results, stderr or telemetry.
Bound variable buffers and parsing; use static System32 interop with reviewed ABI layouts.
Close owned process/thread token handles, free WTS buffers with `WTSFreeMemory`, and free
the LSA buffer once with `LsaFreeReturnBuffer` after SID comparisons finish. Its SID remains
borrowed; station, thread-desktop and pseudo-handles remain borrowed too. Never inspect or
copy the returned LSA name, domain, UPN, profile-path or logon-server strings. The query
still retrieves local metadata; real-host observation of it requires an accepted protocol
under the [experiment policy](../research/experiment-safety.md).

Stable process architecture and logon observations need not be repeated in this process,
which never changes identity. Recheck calling-thread impersonation, session connection
and input desktop immediately before each later provider operation. For real-provider
interaction, recheck those volatile conditions on the actual owned UI thread before
native creation and before publishing the hidden parent, as well as on the calling
thread before the interactive provider effect. Retain the coordinator's post-readiness
token check and terminal/publication synchronization. Rejection destroys any hidden parent through the
same lifetime path; it cannot restart acquisition. There is no background polling,
wait-for-unlock, identity/desktop switching or new timeout. Synchronous checks run on
request/UI work, with the independent process watchdog retaining its existing bound.

In pinned MSAL 4.83.1, [IsBrokerAvailable](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/blob/d5d7de6b103f0d9dd7bca9bf13cbb9f3da37bc9f/src/client/Microsoft.Identity.Client/PublicClientApplication.cs#L91-L98)
is synchronous, has no cancellation parameter, and marks obsolescence only under its
Android/iOS conditional. Its [runtime broker](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/blob/d5d7de6b103f0d9dd7bca9bf13cbb9f3da37bc9f/src/client/Microsoft.Identity.Client.Broker/RuntimeBroker.cs#L685-L708)
can initialize process-global `NativeInterop.Core`. It is a provider effect after local
admission, bracketed by original-token checks, not an inert installed-package probe.
False or recognized initialization failure follows the existing unavailable mapping;
success does not prove account visibility, UI or persistence. Do not poll it or add
account discovery to verify availability. Preserve the rejecting custom web UI.

These predicates are snapshots, not an atomic freeze of the Windows session or a defense
against a compromised current user. The selected normal host and exact Native AOT
interop still require the evidence in the [validation strategy](../validation/strategy.md#windows-slice-design-acceptance).
Real-host observation of these predicates follows the experiment policy.

### Managed HTTP Identity

The [operational identity registry](../governance/operational-identities.yaml) owns the
fork's HTTP product token and development-version behavior. The Windows adapter supplies
one thread-safe, process-owned HTTP client through MSAL's supported
`WithHttpClientFactory(IMsalHttpClientFactory)` seam. A small managed handler adds the
selected product/version token once to each outgoing `User-Agent`, including when MSAL
has supplied its own request header. Preserve dependency-owned product tokens; do not
add account, tenant, Profile, token, machine or persistent identifiers. Forward the
original cancellation token and introduce no application retry or telemetry exporter.

The pinned MSAL 4.83.1
[builder](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/blob/d5d7de6b103f0d9dd7bca9bf13cbb9f3da37bc9f/src/client/Microsoft.Identity.Client/AppConfig/BaseAbstractApplicationBuilder.cs#L66-L93)
accepts this factory, and its
[HTTP manager](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/blob/d5d7de6b103f0d9dd7bca9bf13cbb9f3da37bc9f/src/client/Microsoft.Identity.Client/Http/HttpManager.cs#L214-L259)
sends managed requests through the returned client. Microsoft's
[HTTP client contract](https://learn.microsoft.com/entra/msal/dotnet/advanced/httpclient)
requires safe client reuse and states that MSAL does not dispose the client. The
one-process host owns its lifetime. `WithClientName` and `WithClientVersion` alone are
not evidence that an HTTP `User-Agent` was set.

This selection applies to fork-owned managed requests, including MSAL authority
discovery when needed. WAM/native broker transport and its HTTP identity remain
dependency-owned; no claim is made that the managed factory controls those requests.
The OAuth client registration, consent/audit identity, mechanism and Profile gates are
unchanged. A synthetic message-handler test establishes header behavior.

### Resolution, Acquisition, and Failure Classification

Use MSAL's returned real accounts as the visible account set. Do not consult upstream
files, OS-account sentinels, a previous chosen-account binding, or a different source
after ambiguity. Count exact full-email matches: zero permits interaction only if the
request and host/Profile allow it, one selects that `IAccount`, and more than one is
terminal `account_ambiguous` in every mode. Missing account email is not a match.

A provider failure raised during account discovery is terminal in every mode, including
a provider interaction requirement: discovery has no claims challenge for this request,
so the request ends with that classified outcome (`interaction_required` keeps its safe
reason) and does not open owned UI or make an interactive call, even when interaction
is permitted or forced. Only a no-match discovery result or a silent-call interaction
requirement can advance to interaction.

For a unique real account in `non-interactive-only` or `interactive-if-needed`, call
`AcquireTokenSilent(scopes, account)` before considering interaction. Preserve the
normalized tenant constraint separately from the provider route. Under the legacy
integration and `common` only, a resolved MSA home-account tenant
selects the public transfer tenant `f8cdef31-a31e-4b4a-93e4-5f571e91255a` for silent use;
the initial authority is `organizations` with passthrough. An explicit resource GUID
always wins and is never replaced. This mapping comes from the existing architecture,
not from an email suffix or a new account-kind request.

In `interactive-if-needed`, only no match or a classified `MsalUiRequiredException` can
advance to the one interactive call. `force-interactive` instead enters that call directly
after unambiguous account resolution; perform no silent token-acquisition call and do not
fabricate a provider exception. Establish the hidden owned parent first. For
`interactive-if-needed`, the pinned builder makes `WithAccount` and `WithLoginHint`
mutually exclusive: use `WithAccount` for the uniquely resolved real account, from which
MSAL derives the observed username hint; otherwise use `WithLoginHint` with the requested
email. For `force-interactive`, supply neither (see
[Forced Interactive Authentication](#forced-interactive-authentication)). Preserve the
original request email separately, including when its casing differs from the observed
account spelling. No choice replaces strict final matching. The pinned
[interactive builder](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/blob/d5d7de6b103f0d9dd7bca9bf13cbb9f3da37bc9f/src/client/Microsoft.Identity.Client/ApiConfig/AcquireTokenInteractiveParameterBuilder.cs)
and [request parameters](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/blob/d5d7de6b103f0d9dd7bca9bf13cbb9f3da37bc9f/src/client/Microsoft.Identity.Client/Internal/Requests/AuthenticationRequestParameters.cs)
own that API behavior. Forward an opaque claims challenge
only when it came from this request's silent exception. No caller claims payload,
cross-process continuation, automatic interactive retry, or browser/device-code path is
provided. MSAL/WAM may perform their own documented protocol steps within the original
cancellation/deadline boundary.

| Observation | Coordinator outcome/action |
| --- | --- |
| Admission or Profile semantic error | `invalid_request`; no provider call. |
| No visible match or silent-call interaction requirement | `interaction_required`, or the one compatible interactive call when permitted. |
| Provider interaction requirement during account discovery | `interaction_required` with its safe reason; terminal, no owned UI or interactive call in any mode. |
| Explicit `force-interactive` with zero or one exact match | One provider interactive call through the sign-in/account-selection route with neither account nor login hint; no engine silent acquisition or fallback. |
| Multiple exact visible matches | `account_ambiguous`; no silent call or picker. |
| Broker unavailable, unsupported session, or rejected web fallback | `mechanism_unavailable`; no alternate mechanism. |
| Caller cancellation, lifetime-pipe EOF, `WM_CLOSE` delivered to the hidden parent, or provider user cancellation | `cancelled`; no retry. The local cancellation/deadline latch determines its own origin before interpreting a provider cancellation code. |
| Recognized provider denial, including OAuth `access_denied` or AADSTS65004 | `denied`; no retry. Do not classify all service exceptions as denial. |
| Recognized network/service transient or provider `IsRetryable` hint | `temporarily_unavailable`; no application retry in this Slice. |
| Required result postcondition absent or mismatched | `identity_validation_failed`; discard the candidate token and stop. |
| Original deadline reached before complete validation | `timeout`; invalidate late completion. |
| Unrecognized provider/native failure or application defect | `internal_failure`; no fallback or raw exception export. |

Map codes through an explicit bounded table using structured provider codes/statuses,
never message substring parsing. Safe reason strings identify categories such as
`invalid_configuration`, `tenant_conflict`, `consent_required`, `provider_transient`,
`network_transient` or `service_transient`; otherwise the reason repeats the outcome
name. They never contain arbitrary provider text. Provider timeouts follow the
[observation table](#adapter-observation-boundary): a retryable MSAL timeout, such as
`request_timeout` for an STS request, is `temporarily_unavailable` with
`provider_transient`; an HTTP cancellation with an inner `TimeoutException` while the
original operation token remains active is `temporarily_unavailable` with
`network_transient`. Any other provider-reported timeout, and a provider-reported
invalid request, map to `internal_failure`. A failure to construct or initialize
the provider passes through the same safe filter as any other provider observation:
broker unavailability and the recognized broker failures in the
[observation table](#adapter-observation-boundary) map to `mechanism_unavailable`, a
recognized transient such as `network_not_available` maps to
`temporarily_unavailable`, and any other failure maps to `internal_failure`. Only the
request's own deadline produces `timeout` and only admission produces
`invalid_request`. An interactive call that again requires interaction terminates
as `interaction_required`. State-related interaction requirements follow the same
permission policy; an unexplained broker failure does not authorize account deletion,
cache repair, or retries.

### Forced Interactive Authentication

`force-interactive` is an explicit opt-in extension to protocol 1. Existing requests,
defaults, outcomes, result fields, and their meanings are unchanged. An older executable
that does not implement this enum value returns `invalid_request`; callers must not
silently downgrade a rejected forced request to `interactive-if-needed`.

The managed request projection retains a request-local `ForceInteractive` flag alongside
interaction permission through CLI admission, provider initialization and the MSAL
interactive operation. Forced intent requires permission; an inconsistent internal
request is rejected before discovery. The coordinator preserves exact account discovery
and ambiguity rejection, then skips its silent branch only for that flag. The adapter
omits the discovered account and login hint from the forced interactive builder, so
the pinned broker enters its sign-in/account-selection path and the engine pre-fills
no identity. Both older modes retain their selected account or
requested hint. The request still retains its exact account
selector for discovery and final validation. Controlled application and adapter
scenarios cover this projection, single-call effects, identity rejection and original
cancellation/deadline.

The adapter enters the WAM interactive path through its sign-in/account-selection route,
even when the engine could acquire silently. Whether WAM then presents interactive UI is
provider behavior that the adapter cannot enforce (see the
[known limitations](#validation-and-explicit-unsupported-cases)); entering the path is
not a guarantee that the provider renews credentials or displays a particular surface.
WAM may reuse an existing session. Success reports `interaction: interactive` for the
invoked path, not proof of visible UI.

Do not implement this intent with `WithPrompt` alone. In pinned MSAL/Broker 4.83.1,
the [interactive builder](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/blob/d5d7de6b103f0d9dd7bca9bf13cbb9f3da37bc9f/src/client/Microsoft.Identity.Client/ApiConfig/AcquireTokenInteractiveParameterBuilder.cs#L177-L180)
stores the requested `Prompt`, but [RuntimeBroker's interactive path](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/blob/d5d7de6b103f0d9dd7bca9bf13cbb9f3da37bc9f/src/client/Microsoft.Identity.Client.Broker/RuntimeBroker.cs#L149-L292)
does not read its interactive-parameters argument. Its selected-account and sign-in
paths use [common native parameters](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/blob/d5d7de6b103f0d9dd7bca9bf13cbb9f3da37bc9f/src/client/Microsoft.Identity.Client.Broker/WamAdapters.cs#L130-L211),
which copy supplied extra query parameters without translating that `Prompt`.
This is a source finding about the pinned mapping, not proof that every WAM/native route
is incapable of reauthentication. NativeInterop cannot recover an intent omitted before
its boundary.

For this mode only, leave the interactive builder's account and login hint unset and
set no extra query parameters. In the pinned
[RuntimeBroker](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/blob/d5d7de6b103f0d9dd7bca9bf13cbb9f3da37bc9f/src/client/Microsoft.Identity.Client.Broker/RuntimeBroker.cs#L174-L292),
a bound account can enter `AcquireTokenInteractivelyAsync` for that account;
an unbound request enters `SignInInteractivelyAsync`, the broker's sign-in and
account-selection route, and passes any request login hint to it
([hint selection](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/blob/d5d7de6b103f0d9dd7bca9bf13cbb9f3da37bc9f/src/client/Microsoft.Identity.Client.Broker/RuntimeBroker.cs#L242-L292)).
The account binding, not the hint, chooses the route. The pinned
[interactive builder](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/blob/d5d7de6b103f0d9dd7bca9bf13cbb9f3da37bc9f/src/client/Microsoft.Identity.Client/ApiConfig/AcquireTokenInteractiveParameterBuilder.cs#L136-L138)
documents `WithLoginHint` as a way to avoid select-account dialogs when the user is
signed in with several identities. That is the builder's documented intent, a source
finding, not a UI observation. The official
[MSAL Python broker mapping](https://github.com/AzureAD/microsoft-authentication-library-for-python/blob/03d030f715f8398a26151602ac578f66b4bb40e8/msal/broker.py#L199-L210)
likewise implements `select_account` by dropping the login hint, not by setting a
native `prompt` property. This cross-library reference supports the choice; it does
not establish pinned .NET or Windows runtime behavior. No supported builder input on the
pinned .NET broker path is found that forces the WAM UI: `WithPrompt` is not transported
(above), so this design controls only whether an account or hint is supplied.

By repository-owner decision (2026-10-16 UTC, quoted verbatim in
[PR #501](https://github.com/hcoona/microsoft-authentication-cli/pull/501), which made
this change), forced interaction supplies neither the login hint nor the discovered
account. The [real-host results](../research/experiments/real-host-validation.md#results)
compare this choice with a hinted implementation and an experiment-only account-bound
variant; with either alternative, WAM completed some forced calls without presenting
interactive UI. That the hint or the bound account caused this is an **inference**; the
source findings above are consistent with it but do not establish broker behavior.

The Microsoft [WAM API reference](https://learn.microsoft.com/entra/identity-platform/reference-entra-id-wam-api)
documents a `prompt` request property (`no_select` or `select_account`) and a
`LoginHint` property. That is a documented request contract; the pinned transport above
does not carry `Prompt`, and actual WAM UI behavior for either route is a runtime
observation. Do not set an undocumented or untranslated native property, an extra
`prompt` query parameter, or change client/Profile/scopes, application configuration,
dependencies, cache keys or either silent-first mode.

The picker may let the user choose an account other than the requested one. Final
validation rejects any different returned identity with `identity_validation_failed`
before emitting a token. No new discovery, silent call, browser fallback or retry is
introduced. A provider/host combination that cannot honor the invocation intent returns
`mechanism_unavailable` when that condition is established; an unexplained broker
failure retains its existing mapping.

Reuse the same account discovery, strict selection and final validation, Profile/tenant/
scope constraints, hidden owned parent, one interactive call, original deadline, and cancellation
paths. Forced interaction carries no fabricated or caller-supplied claims challenge.
Success reports `interaction: interactive`; failure retains the existing no-token
result and exit contract. Actual visible WAM behavior still requires runtime observation.

This operation does not call token-cache refresh, sign-out, account removal, consent
revocation, or cache-clearing APIs. Normal provider-managed protected-state/session updates
can occur. Success does not guarantee new token bytes, revocation of earlier credentials,
or a persistence receipt; keep the existing persistence warnings.

### Token Format and Future Extensions

Interaction intent and token format are separate concerns: forced interaction does
not select PoP, refresh semantics, or a different resource. Keep access-token material
opaque and preserve the provider-reported `tokenType` in the existing result. Consumers
must use the reported type rather than infer a type from the interaction mode; that field
alone does not advertise support for another token format.

A future opt-in PoP contract would need explicit request-binding semantics, resource
compatibility, signing/key ownership, confidential transport, and lifecycle review.
Preserve the meaning of current requests and results. Use a compatible additive extension
only when those meanings remain intact; otherwise select a new protocol major. Do not
reserve dormant PoP fields, export private keys, accept unknown request options, or
reinterpret a current access token as a signing handle. PoP remains unsupported in this
Slice, and no signing service or downstream request protocol is introduced here.

### Adapter Observation Boundary

Evaluate original-token cancellation before interpreting provider observations;
the existing request lifetime determines cancellation versus the original
deadline. Use ordinal provider-code comparison. Known cancellation, denial, and
identity mismatch precede the broader UI-required and retryable branches.

| Public observation | Existing outcome and reason |
| --- | --- |
| Original operation token is canceled | Propagate cancellation carrying that token. |
| `authentication_canceled` | `cancelled`. |
| `access_denied` or positively recognized structured Entra code 65004 | `denied`. |
| `user_mismatch` | `identity_validation_failed`. |
| `MsalUiRequiredException` | `interaction_required`; `ConsentRequired` classification uses `consent_required`. |
| Explicit availability/host failure, rejecting custom UI, `platform_not_supported`, or `wam_runtime_init_failed` | `mechanism_unavailable`. |
| Remaining `MsalException.IsRetryable` | `temporarily_unavailable` / `provider_transient`. |
| `service_not_available` or `temporarily_unavailable` | `temporarily_unavailable` / `service_transient`. |
| `network_not_available`, or `HttpRequestException` with NameResolutionError/ConnectionError | `temporarily_unavailable` / `network_transient`. |
| HTTP cancellation with an inner `TimeoutException` while the original token remains active | `temporarily_unavailable` / `network_transient`. |
| Other observations, including unexplained cancellation and unknown configuration/native errors | `internal_failure`. |

The MSAL exception type and its public retry hint already normalize the relevant
broker statuses. Do not duplicate a private native enum or interpret
AdditionalExceptionData numeric values as Entra error codes. A provider timeout
does not establish expiry of the caller's deadline. No branch introduces an
application retry, alternate mechanism, account repair, or raw diagnostic export.
Only claims from this request's silent UI-required exception may reach its single
interactive continuation; a subsequent challenge ends the request.

For the accepted 65004 denial example, inspect only a bounded public
`MsalServiceException.ResponseBody`: at most 8,192 UTF-16 code units before JSON
parsing, maximum JSON depth 8, and at most 16 top-level `error_codes` entries.
Require one root object and exactly one case-sensitive `error_codes` property
whose complete array consists of Int32 integers. Recognize 65004 only after
validating the entire document and array. Reject duplicates, trailing syntax,
comments, trailing commas, malformed data, and exceeded bounds. Never inspect
messages, descriptions, URLs, inner-exception text, or nested error-code arrays
to infer denial. These bounds limit recognition work; they are not a public
input contract or evidence that all real WAM denials can be distinguished.
Unrecognized details contribute no denial evidence; independent known codes,
types, and retry hints retain their defined precedence.

[Microsoft's error reference](https://learn.microsoft.com/entra/identity-platform/reference-error-codes) identifies `error` as the application reaction code
and numeric `error_codes` as diagnostic information. It explicitly warns that
the numbers can change and that applications depending on them can break.
Recognizing the existing design's 65004 example is a bounded repository mapping
choice based on its currently documented meaning, not a stable MSAL or service
contract. Pinning MSAL does not freeze service diagnostics. Reassess this narrow
premise when accepting or revising the mapping and at release review through
`RECHECK-009` in the existing [recheck registry](../research/rechecks.yaml). That review cannot create stability
or complete observability that the provider does not promise.

Some pinned account-discovery failure paths return an empty list. Supported
public APIs cannot reconstruct that hidden reason; apply the existing no-match
permission policy and retain that evidence limitation. No extra discovery or
private diagnostic path is introduced.

The exact custom-UI interface spelling is
`Microsoft.Identity.Client.Extensibility.ICustomWebUi`. Its implementation always
rejects navigation with the sanitized mechanism-unavailable failure. A scenario runs
the production interactive builder through a real MSAL public-client application
built without a broker, over a synthetic transport that answers only instance
discovery and fails on any other request; MSAL reaches the rejecting UI and the
request maps to `mechanism_unavailable` with `rejected_web_ui`. That establishes the
pinned fallback route, not real broker-disappearance behavior.

The exact source basis is MSAL 4.83.1 commit
`d5d7de6b103f0d9dd7bca9bf13cbb9f3da37bc9f`:
[exception evidence](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/blob/d5d7de6b103f0d9dd7bca9bf13cbb9f3da37bc9f/src/client/Microsoft.Identity.Client/MsalServiceException.cs),
[UI-required classification](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/blob/d5d7de6b103f0d9dd7bca9bf13cbb9f3da37bc9f/src/client/Microsoft.Identity.Client/MsalUiRequiredException.cs),
[broker translation](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/blob/d5d7de6b103f0d9dd7bca9bf13cbb9f3da37bc9f/src/client/Microsoft.Identity.Client.Broker/WamAdapters.cs),
and [custom UI contract](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/blob/d5d7de6b103f0d9dd7bca9bf13cbb9f3da37bc9f/src/client/Microsoft.Identity.Client/Extensibility/ICustomWebUI.cs).
The .NET 10 [HTTP cancellation contract](https://learn.microsoft.com/dotnet/api/system.net.http.httpclient.sendasync?view=net-10.0)
and [network categories](https://learn.microsoft.com/dotnet/api/system.net.http.httprequesterror?view=net-10.0)
supply the non-MSAL observations. These are source contracts and repository mapping
choices; controlled exception/result fixtures cannot establish real denial, broker,
UI, account discovery or network behavior.

### UML Request Sequence

```mermaid
sequenceDiagram
    participant Caller as WSL caller
    participant CLI as Windows request coordinator
    participant UI as Owned Windows UI
    participant MSAL as Request-local MSAL application
    participant WAM as Windows broker and state
    Caller->>CLI: authenticate + explicit protocol/Profile/account/scopes/permission
    CLI->>CLI: Validate admission#59; establish original deadline
    CLI->>MSAL: GetAccountsAsync(original cancellation token)
    MSAL->>WAM: Discover eligible real accounts
    WAM-->>MSAL: Provider accounts
    MSAL-->>CLI: Visible account set
    CLI->>CLI: Count strict full-email matches
    alt Multiple exact matches
        CLI-->>Caller: account_ambiguous#59; exit 1
    else Zero or one exact match
        opt Exactly one match and mode is not force-interactive
            CLI->>MSAL: AcquireTokenSilent(scopes, real account)
            MSAL->>WAM: Selected-account silent operation
            WAM-->>MSAL: Candidate or classified failure
            MSAL-->>CLI: Candidate or interaction-required
        end
        opt force-interactive, or permitted no match / silent interaction requirement
            CLI->>UI: Create hidden owned parent and completion context
            CLI-->>Caller: Best-effort unofficial/Profile indication on stderr
            CLI->>MSAL: One interactive call#59; account or hint only when not force-interactive
            MSAL->>WAM: Interactive operation with owned HWND and cancellation
            WAM-->>MSAL: Candidate or failure
            MSAL-->>CLI: Candidate or typed provider observation
        end
        CLI->>CLI: Validate identity, tenant, operation binding, scopes, and metadata
        CLI->>CLI: Classify already available persistence observation
        CLI->>UI: Destroy hidden parent#59; invalidate acquisition completion
        CLI-->>Caller: One JSON result#59; success 0 or failure 1
    end
    Note over Caller,WAM: Cancellation/deadline can terminate any pending path#59; external session changes are not rolled back
```

The diagram's success tail applies only to a candidate that passed validation. Ambiguity,
denial, cancellation, timeout, and invalid candidates do not continue to it.

### Authoritative Result and State Observations

Validate `AuthenticationResult.Account?.Username`, `TenantId`, `Scopes`, `TokenType`,
`ExpiresOn`, nonempty access token, and operation context. Require the requested email
and the exact tenant when constrained. The client, cloud and resource context is
established only by the operation binding described below, not by a returned value.
Preserve
the provider's email spelling, tenant, token type, expiration, granted scopes, and
correlation GUID when nonempty. Reject already expired candidates; impose no additional
minimum remaining lifetime. Use provider metadata rather than parsing an access or ID
token in application code.

Two postconditions rely on provider-call binding rather than on a returned value. The
coordinator accepts a candidate only when its operation identifier, returned as the MSAL
correlation ID, equals the one it sent with the provider call that carried the admitted
Profile, client, authority and scopes (`RequestCoordinator.ValidateCandidate`). For a `/.default` request,
scope satisfaction is then accepted without comparing granted scopes, because providers
need not report the literal `/.default` value (`SatisfiesScopes`). Profile and client
identity are likewise not read back from the result: the request-local application
instance carries them. This binding establishes only that the candidate came from that
provider call. It does not prove the resource or client for which the token was issued,
does not detect a provider that ignored a request parameter, and rests on MSAL and WAM
as trusted dependencies. For dynamic scopes, each requested scope must be among the
granted scopes, compared ordinally; extra grants are allowed. The token is never parsed
to fill the gap.

Missing account email and invalid or missing tenant metadata, including an all-zero
tenant GUID, remain missing in the adapter projection; requested values must not fill
them. Validation independently rejects an all-zero actual tenant. Keep the request-local
operation identifier separately from the observed correlation ID. The pinned
[public result shape](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/blob/d5d7de6b103f0d9dd7bca9bf13cbb9f3da37bc9f/src/client/Microsoft.Identity.Client/AuthenticationResult.cs)
provides the observed fields; projection does not establish persistence.

The public `authority` is the canonical Public Cloud authority URI formed from the
validated single-cloud operation and actual `TenantId`:
`https://login.microsoftonline.com/<actual-tenant-guid>`. It identifies the resulting
cloud/tenant, not a network trace, home-tenant claim, or the initial `common`,
`organizations`, or transfer routing alias. MSAL's public `AuthenticationResult` does
not expose a literal final endpoint URI; the contract does not invent one. A missing
actual tenant or unverifiable cloud association fails validation.

Result projection writes `mechanism: wam` and `interaction` as `silent` or `interactive`,
from whether the coordinator completed the request through the provider's silent or
interactive operation. This describes the API used, not a claim about which MFA option the user
saw or whether an interactive API happened to complete without a visible prompt.

WAM owns persistence, locking, corruption handling, and account/session state. Use its
documented operations without inspecting its files or resetting accounts. A clean
interaction-required/miss indication may proceed through the normal permission gate;
an unusable or unverifiable candidate cannot succeed. There is no owned cache to repair.
The pinned public result has no per-request durable-write receipt, so successful results
carry `persistence_unconfirmed`. This warning does not claim persistence failed, disable
future reuse, or require a readback. Do no post-validation persistence I/O or waiting.

## Windows UI and Request Lifetime

### Parent Selection Decision

The CLI product boundary remains governed by
[V2-REQ-002](../product/requirements/product-boundary.md#v2-req-002-no-implicit-product-expansion);
an in-process Win32 implementation does not justify an extra user-facing desktop surface.
The repository owner selects a hidden owned parent as the WAM parent, as recorded in
[PR #462](https://github.com/hcoona/microsoft-authentication-cli/pull/462) without a
verbatim owner quotation. The
[Microsoft WAM guide](https://learn.microsoft.com/entra/msal/dotnet/acquiring-tokens/desktop-mobile/wam#parent-window-handles)
requires an explicit parent HWND, but no visible V2 window. A visible parent's Cancel
action cannot stop a pending WAM prompt while WAM disables its owner, and it adds a
DPI-unaware surface the product does not otherwise need. The research record owns the
[console-parent comparison](../research/experiments/windows-slice-validation.md#console-parent-versus-a-separate-desktop-surface)
and [hidden-parent observations](../research/experiments/windows-slice-validation.md#hidden-owned-parent-candidate-observations)
that informed this decision; borrowed console or foreground windows are not parent
sources.

The product uses an ordinary top-level Win32 window, never shown, on an owned STA thread
with a message loop, only when interaction is both needed and permitted. It supplies a
stable nonzero HWND and has no child controls: no label, button, font, layout, dialog
message routing, `WM_COMMAND` or Escape handling. It does not ask for account email,
passwords, URLs, or credentials; those belong to the caller's request and provider UI.
Its window text is the fixed unofficial display name
`AzureAuth Unofficial V2 (hcoona/microsoft-authentication-cli)`, the same constant as
the first help line, so any surface that names the owner window names the unofficial
product rather than Microsoft or the registration owner. The window is never shown, so
the text is not a user-facing claim.
Parent WAM with that HWND and let WAM choose sign-in/MFA/consent methods. Failure to
create the parent makes the path unavailable before the interactive call.

The window procedure handles only `WM_CLOSE`, which signals the request cancellation
latch, `WM_DESTROY`, the private owned-closure message and creation failure. The UI
thread registers the window class and creates and destroys its HWND without showing it.
No custom credential form, embedded browser, COM automation or rich-text rendering is
needed. Keep the window-procedure callback and request context alive through window
destruction; unmanaged callbacks must not unwind managed exceptions across the ABI
boundary.

Unofficial identity under
[V2-REQ-003](../product/requirements/product-boundary.md#v2-req-003-unofficial-product-identity)
is presented without owned controls. Before the interactive call, write one bounded
stderr indication, best-effort, from a fixed template that contains only the unofficial
project identity and the admitted Profile's public name and registration owner. Set the
fixed public MSAL [`BrokerOptions.Title`](https://learn.microsoft.com/dotnet/api/microsoft.identity.client.brokeroptions.title)
`Unofficial hcoona/microsoft-authentication-cli`. Whether a caller shows stderr to the
user, and how WAM presents the title, are outside the engine's control and are not
claimed. No per-use confirmation is requested.

Human cancellation is WAM's own cancel. Cancellation sources are caller cancellation,
lifetime-pipe EOF or failure, the original deadline, WAM's cancel and `WM_CLOSE`
delivered to the hidden parent. Borrowed terminal windows are never closed or destroyed
by V2. A later simplification must preserve self-contained interaction and completion,
strict selection, the pre-interaction indication, operator-controlled sign-in,
cancellation, the original deadline, and normal termination.

The coordinator awaits a ready HWND or a typed creation failure under the original
deadline before the one interactive call. Its asynchronous MSAL work must not block the
UI message pump with `.Result`, synchronous waits, or synchronous cross-thread window
messages. `WM_CLOSE` signals the request cancellation latch. Terminal completion
invalidates the request context and posts owned-window closure to the creating thread;
that thread destroys its window and quits its loop. A queued completion cannot recreate
UI or authorize success after cancellation. If creation, dispatch or destruction stalls,
the existing one-process shutdown watchdog remains the final bound.

### UML UI Ownership and Cancellation Sequence

```mermaid
sequenceDiagram
    participant C as Request coordinator
    participant U as Owned STA, hidden parent and message loop
    participant P as MSAL and WAM
    participant W as Process shutdown watchdog
    C->>U: Start only when interaction is needed and permitted
    U-->>C: Ready hidden HWND or creation failure
    opt Ready before original deadline and request still active
        C-->>C: Write best-effort unofficial/Profile indication to stderr
        C->>P: One asynchronous interactive call with HWND and request token
        Note over U: Continue dispatching messages while provider work is pending
        alt Caller cancel, lifetime-pipe EOF, deadline, WAM cancel, or WM_CLOSE
            U-->>C: Cancellation notification when WM_CLOSE reaches the hidden parent
            C->>C: Latch cancellation/timeout and reject late success
            C->>P: Request cancellation
            C->>W: Enforce existing shutdown allowance
        else Candidate or failure completes
            P-->>C: Request-bound provider observation
            C->>C: Validate candidate and select terminal outcome
        end
        C->>U: Post closure with invalidated request context
        U->>U: Destroy HWND on creating thread and quit loop
        U-->>C: Closed
        Note over C,W: A stalled provider, UI, or output cannot extend process lifetime
    end
```

No window or prompt is created for a prohibited-interaction request. Broker
discovery and silent APIs are the eligible no-interaction operations; no opaque default
account call is used. The engine cannot promise that a malfunctioning OS is repaired by
application code. A dependency contract regression makes that combination ineligible.

The coordinator races provider completion against the original cancellation/deadline
latch. Cancellation sources are caller cancellation (Windows console cancellation when
delivered), EOF/failure on the optional lifetime pipe, the original deadline, WAM's own
cancel, and `WM_CLOSE` delivered to the hidden parent. With
`--cancel-on-stdin-close`, stdin must be a redirected pipe; reject a console or regular
file before provider work. The caller must retain the sole writer and avoid leaking it
to descendants. Data is not interpreted or logged; the reader only observes closure.
An already closed pipe cancels before authentication.
Console cancellation is exercised by a synthetic scenario whose child process has its
own hidden console and raises `GenerateConsoleCtrlEvent(1, 0)` (Ctrl+Break to that
console's processes) rather than starting in a new process group; this establishes
delivery to the product's handler, not real terminal behavior.

Without that flag, stdin is unused. WSL Linux signals and Linux parent-process death are
not assumed to become Windows cancellation notifications. A caller needing prompt
disconnect cancellation supplies the lifetime pipe; otherwise the finite CLI deadline
is the backstop, and a broken output pipe ends delivery when detected. Immediate detection
of an arbitrary Linux parent's death without a lifetime channel is explicitly unsupported.
Do not discover Linux process IDs, create listeners, or add a helper service to infer it.

On any terminal acquisition outcome, latch the outcome, reject later callbacks, cancel
the provider token, close owned UI, and stop initiating work. Deliver at most one result.
Caller cancellation observed before result commitment wins over a candidate success or
persistence warning. Deadline expiry before complete validation yields `timeout`;
incidental expiry after timely validation may use the requirements' success-or-timeout
latitude. Once delivery starts, a concurrent cancellation cannot rewrite a partial JSON
object: finish the selected result or terminate with incomplete output, never emit a
second object.

Anonymous-pipe I/O may block. Use dedicated bounded-lifetime I/O work and a process-level
shutdown watchdog, not an assumption that `WriteAsync` makes an anonymous pipe cancellable.
At cancellation or expiry, allow at most one second for local closure/result delivery;
no acquisition, retry, persistence wait, or renewed deadline occurs in this shutdown
interval. If owned cleanup or transport cannot finish, terminate the one process. OS
process termination releases its threads and handles; external WAM services remain OS
owned. A forced termination before a complete result has been written, or an
incomplete write, is a transport failure, not a fabricated typed result. This is the exceptional fail-closed boundary, not a resident watchdog
process or a mechanism for making unsupported platforms work.
Under [V2-REQ-030](../product/requirements/result-and-process-protocol.md#v2-req-030-versioned-result-and-exit-status),
the complete written result fixes the exit status. The status is fixed when the host
observes that the write of all the result's bytes to stdout has completed: the typed
status `0` or `1` that matches the result is then published before any drain or
diagnostic wait. If the bound then expires because owned cleanup, required drain or
optional diagnostics have not finished, the watchdog terminates with that published
status. If the bound expires before the host observes write completion, even when the
bytes have in fact reached the pipe, the watchdog exits `2`: a fail-closed transport
failure. A failed result write publishes no status, so no completion indication,
token-shape observation or request-span stop event follows it, and the process exits
`2`.

The managed process host keeps its entry thread as the watchdog and moves request
work, native handle I/O, and cleanup onto dedicated background work. It reads the
original deadline and first terminal/cancellation timestamps without waiting on
request locks. Result commitment is serialized with host cancellation. The observed
completion of the result's native transfer fixes the exit status; required operation,
cancellation-callback, and lifetime-pipe drain are prerequisites for ending the
process normally before the bound, not for that status. Pipe admission runs within the
original request lifetime and precedes Profile access. An initially closed pipe
cannot release Profile work while cancellation propagation is pending. Optional
diagnostics have separate bounded bytes and do not participate in required drain.

The request lifetime dispatches its dependency body through `Task.Run` before
returning the terminal task. A synchronous dependency prefix therefore cannot keep
the result worker from observing cancellation or the original deadline. Dispatch
has no scheduling cancellation token: the body checks the original provider token
before effects, and its `finally` must publish actual operation completion even
when cancellation occurred before execution. ExecutionContext continues to flow;
native admission checks the actual executing request thread, and owned UI remains
on its dedicated STA. Scheduling delay or an uncooperative dependency may still
prevent required drain. A selected terminal task does not establish cleanup or result
delivery, and the unchanged watchdog remains the process bound.

External broker windows/session changes are not forcibly rolled back. Forward cancellation
through MSAL, destroy the hidden parent, invalidate completion, and write a fixed sanitized
completion/cancellation indication to stderr when available. Do not wait for the user to
dismiss an external surface before ending the CLI process.

The product forwards cancellation but does not guarantee that WAM closes its window. The
window is provider-owned and can remain after the process ends (finding F2 in the
[known limitations](#validation-and-explicit-unsupported-cases)); the user closes it
manually. The typed `timeout` or `cancelled` result is already committed, no token is
delivered and a late completion cannot resume the request. By repository-owner decision,
recorded as a non-goal in
[PR #493](https://github.com/hcoona/microsoft-authentication-cli/pull/493) without a
verbatim owner quotation, no window-closing mechanism is added; the
[threat model](../security/threat-model.md#known-limitation-provider-window-after-non-human-termination)
records this known limitation.

### UML Terminal State View

```mermaid
stateDiagram-v2
    [*] --> Admission
    Admission --> Active: Valid arguments/Profile
    Admission --> Ending: Invalid request, cancellation, or timeout
    Active --> Validated: Candidate passes all postconditions before deadline
    Active --> Ending: Host admission rejected or unobservable at first discovery, failure, denial, cancellation, or timeout
    Validated --> Ending: Success with available warning, cancellation, or permitted incidental timeout
    Ending --> Delivering: Latch outcome, invalidate callbacks, close owned UI
    Delivering --> Exited: Complete JSON fixes the matching exit, even if cleanup is later forced, or failed write and exit 2
    Ending --> Exited: Shutdown watchdog or process termination
    Exited --> [*]
```

## Result, Diagnostics, and Compatibility

The [result schema](../../contracts/v1/result.schema.json) owns the result object's
serialized fields. This design owns how it is delivered: an `authenticate` invocation
writes one UTF-8 JSON object followed by LF to stdout, with no BOM. Exit status has
three meanings:

- `0`: a complete `success` object on stdout.
- `1`: a complete typed failure object on stdout; every typed failure shares it.
- Any other status: V2 could not guarantee a complete, parseable result. The caller
  treats the run as a transport failure, whatever stdout contains. The product itself
  uses `2` when no complete result was written: a broken or failed result write, or the
  process watchdog when its bound expires before the result is written. The OS or
  runtime may produce other values, for example when the process is killed.

Once the host observes that the complete result has been written, the exit status
follows it. A later failed or stalled owned cleanup, required drain, watchdog
termination or diagnostic keeps the `0` or `1` that matches the written result and
may be reported only on stderr. A bound that expires before that observation yields
`2`, the fail-closed transport failure, even if the caller can read a complete object.

Failure objects contain no token or account metadata. A success with
`persistence_unconfirmed` still exits zero. Invalid/partial JSON, absent output, or an
object whose outcome does not match the exit status is also a caller-observed
transport/protocol failure and must not be converted into an engine outcome. Result
projection fails closed: a selected success whose access token, token type or account
email is empty, or whose scope list is absent, is projected as `internal_failure`
rather than as a success the schema would reject. `help`, root `--help` and root
`--version` are not part of the `authenticate` protocol; their exit status reports only
whether their text was written.

Build the result from an allowlist of fields, not by serializing an MSAL result or
exception. Only `accessToken` in a validated success may expose authentication material.
Do not emit refresh/ID tokens, claims, account IDs, raw exceptions, provider property bags,
or diagnostic email hashes. Keep stdout exclusively for that result.

A `mechanism_unavailable` result may additionally have one fixed stderr indication
naming the first failed local admission or broker boundary: `host_platform`,
`host_workstation`, `host_thread_token`, `host_logon`, `host_session`,
`host_input_desktop`, `dll_search`, `broker_unavailable`, `broker_platform`,
`broker_initialization`, or `rejected_web_ui`. `unavailable` means no boundary
was captured. Record only these source-defined categories, never a native error,
identifier, exception, provider property, or input value. An admission category
identifies a rejected predicate, not the underlying OS cause. Emission remains
optional and cannot change result, cancellation, retry, or termination semantics.
The own-logon observation further distinguishes its existing rejection branches:
`host_logon_process_token`, `host_logon_token_statistics`,
`host_logon_session_data`, `host_logon_session_data_size`,
`host_logon_session_id`, and `host_logon_session_sid` identify an unavailable or
invalid required read; `host_logon_type` and `host_logon_identity` identify rejected
local conditions. `host_station_visible`, `host_station_user_unavailable`, and
`host_station_user_mismatch` distinguish the existing station checks. The generic
`host_logon` remains the fallback for an unclassified missing observation. These
categories add no native query, expose no observed value, and relax no admission
condition. Classification happens after the original cancellation recheck.
The current-session observation similarly distinguishes `host_session_id_unavailable`
from `host_session_query_unavailable`, identifying failure of the already selected
process-session or WTS query. `host_session_zero`, `host_session_inactive`, and
`host_session_malformed` identify a rejected zero session, a valid non-active connection
state, or invalid returned data. `host_session_unavailable` covers an unclassified
unavailable observation; `host_session` remains the fallback for an unknown category.
The input-desktop check distinguishes `host_input_desktop_unavailable` from
`host_input_desktop_not_receiving`. Use the same classification during initial admission
and rechecks. These categories add no query, disclose no session ID, connection-state
number or native error, and do not authorize session/service repair or infer a reason
for a non-active connection. The
[connection-state contract](https://learn.microsoft.com/windows/win32/api/wtsapi32/ne-wtsapi32-wts_connectstate_class)
defines `WTSActive` and the other states; an inactive category alone does not select a
particular non-active state or prove how the operator is connected.
Absent or invalid diagnostics remain unavailable or invalid. They neither establish
authentication success nor relax any passing expectation.

The consuming scenario for this vocabulary is a bounded Windows host observation in
which an otherwise valid request returns `mechanism_unavailable`: the operator needs
the first failed boundary to decide which ordinary host condition (for
example, an inactive session) to restore before another permitted attempt, without
exporting native details. The vocabulary changes no product behavior.

Stderr carries bounded fixed human indications, the bounded pre-interaction indication
from the parent selection decision, and, only with `--telemetry stderr`, local
JSON event lines. Stage events carry `event`, `stage`, request-local trace and span IDs
and elapsed milliseconds; a completed stage adds its duration and `status`, and the
completed request stage may add the `error.type` described below; the
final `request_completed` event carries `event`, `stage`, `outcome` and elapsed
milliseconds, plus the token-shape fields below for a success. Events
contain no token, email, path, Profile/client/account identifier, tenant, resource, scope,
claims, or provider text. Stderr indications, telemetry events and exception output
follow the exclusions of
[V2-REQ-036](../product/requirements/result-and-process-protocol.md#v2-req-036-account-and-host-identifier-privacy).
Only the stdout result carries the email and tenant that its contract requires. This is optional local
telemetry; network export and persistent telemetry identifiers are not provided by this
Slice. With `--telemetry stderr`, all
event and indication bytes accepted for one invocation total at most 8 KiB. Of that
total, 1 KiB stays reserved for the final completion line and a further 512 bytes for
the owned request span's stop event, so phase events and the pre-interaction notice
cannot exhaust either; excess is dropped, not queued. A sink that fails or refuses a
write ends local output for the invocation. Never await a flush before result delivery.
After the result is written and request work has drained, the process host waits for
the local writer, and for any best-effort stderr indication, only within the remaining
process shutdown bound; a blocked or failed sink cannot change authentication or exit
status. No upstream telemetry configuration is read.

### Local OpenTelemetry-Compatible Observation

Instrument the request and its Profile read, account discovery, silent acquisition,
owned interaction, interactive acquisition, candidate validation and owned-UI close
with .NET `ActivitySource`/`Activity`. Use the fork-owned source name
`hcoona.microsoft-authentication-cli`, versioned with the product version without build
metadata ([Portable Package and Version](#portable-package-and-version)).
These APIs are the [.NET OpenTelemetry instrumentation boundary](https://learn.microsoft.com/dotnet/core/diagnostics/observability-with-otel#net-implementation-of-opentelemetry),
not an assertion that an OpenTelemetry SDK/exporter has been installed or run.
No new dependency, exporter endpoint or Collector service is needed for local observation.

`--telemetry stderr` installs one `ActivityListener` for the invocation that listens
only to that source and projects fixed stage-start and stage-completion JSON lines to
the existing bounded stderr sink. .NET registers activity listeners process-wide, so
the listener observes any activity of that source in the process, not only this
request's; the product runs one request per process, and the projection still emits
only allowlisted fields of owned stage names. Start notifications supply immediate progress;
completed spans supply durations. A pending span is not evidence of completion.
Keep the existing `request_completed` event and fixed human indications. The local
projection permits fixed event/stage/outcome names, nonnegative relative durations,
and request-local W3C trace/span/parent-span IDs only. IDs are newly generated for
each request, never derived from input/provider/machine state and never persisted
as a user identity. Do not serialize arbitrary tags, baggage, exceptions, resource
attributes, dependency activities or provider objects. Unknown events/attributes
are omitted rather than forwarded. Telemetry-off installs no local listener.

For a committed `internal_failure`, the completed owned request span may additionally
carry the OpenTelemetry `error.type` attribute. The adapter supplies one fixed enum
through its existing sanitized failure boundary; no exception object, message, stack,
dynamic MSAL error code or broker property is retained. Permitted values are
`query_parameter_conflict`, `broker_contract`, `broker_configuration`, `msal_client`,
`msal_service`, `argument`, `invalid_operation`, and `unexpected`. The first recognizes
MSAL's exact duplicate-query-parameter error; the next two recognize only the pinned
broker's structured contract/configuration status. Other exceptions receive only their
fixed category. Unknown enum or provider values cannot become attribute text.

Only the committed request's stopped span may project this optional attribute into
the existing bounded local JSON event. Do not forward arbitrary tags or dependency
activities. A later cancellation, timeout or other commitment omits the provisional
classification. The attribute identifies the observed failure boundary, not a root
cause, registration ownership or provider-internal state. Its absence is unavailable
evidence. Result fields, authentication routing and failure mapping remain unchanged.

Use explicit parent contexts for the owned spans and preserve `Activity.Current`
around their start/stop. The observation must not become ambient context for MSAL,
HTTP or other dependencies: do not add outgoing trace headers or baggage. Listener
or sink exceptions are contained; no synchronous I/O, unbounded queue, flush wait
or export work belongs on an authentication callback or commitment lock. Keep one
bounded local writer and the original process watchdog/ending allowance. Missing,
dropped, partial or sampled observations cannot establish success or closure.

Under [V2-REQ-038](../product/requirements/result-and-process-protocol.md#v2-req-038-local-token-shape-validation-observation),
the token-shape observation lets integration validation check a delivered token
without exposing it. It runs only with `--telemetry stderr`, only for a committed,
validated success, and only after the complete result has been written to stdout, so
its work can never delay result delivery or consume the shutdown allowance before that
write. At most 64 KiB of
token text and a 32 KiB decoded JWT payload with JSON depth eight may be examined.
Malformed input, duplicate members, unsupported format or an exceeded bound yields
only a fixed unavailable/unreadable/limit category. Emit only a format category,
whether `exp`, `aud` and `scp` claims are present in their expected types, and
the provider-reported expiration and remaining lifetime, which V2-REQ-038 permits as
non-token metadata already in the success result. Never emit a header/payload,
claim value/name supplied by input, token length/hash, account or tenant identifier.
JWT decoding is explicitly unverified; it does not establish signature validity,
identity, scope satisfaction or resource acceptance. Opaque tokens remain valid
authentication candidates. Interpretation cannot change the committed result.

Output-contract validation on a real host uses the Windows-local result validator in
`tools/result-validation` and exports only its fixed check conclusions. Telemetry is not
evidence of normal output, process exit or WSL credential transport. No new
authentication command or alternative stdout result mode is introduced.

Protocol 1 producers emit only the defined fields. Consumers must tolerate unknown
additional result fields but still validate the known outcome and required fields;
additive optional result metadata is compatible. Request and Profile readers reject
unknown inputs so typos cannot become silently ignored intent. Existing flags, defaults,
outcomes, and result meanings cannot be changed incompatibly while protocol 1 remains
supported. A breaking request/result change requires a new major; a breaking Profile
interpretation requires a new Profile schema version. No v1 AzureAuth compatibility is
implied, and no protocol is published as supported merely by merging this design.

## Validation and Explicit Unsupported Cases

The [Slice scenario basis](../validation/strategy.md#windows-slice-design-acceptance)
owns cases and required evidence. Contract examples are synthetic and demonstrate schema
shape only. Core normalization/selection/outcome rules receive unit tests; ordinary
orchestration is validated by scenario outcomes, provider calls, observed UI/lifetime,
and output effects rather than private class structure.

Unsupported in this Slice: native Linux/macOS authentication, WSL forwarding, non-Public
Cloud, Windows services/impersonation/disconnected sessions, ARM64, browser/device code,
PoP, confidential/workload credentials, PATs and derived-credential exchange, opaque OS
account selection, alias equivalence, stable account selectors, cross-process claims
continuation, shared-cache import/repair, logout/cache commands, duplicate-prompt
suppression, network telemetry export, Profile auto-discovery/activation, and immediate
Linux-parent-death detection without the lifetime pipe. Git clone and NuGet restore
remain downstream service tests, not operations of this engine.

Known limitations recorded from the
[real-host results](../research/experiments/real-host-validation.md#results), which own
the observations and their role, caller and subject scope:

- **Interactive UI is provider behavior.** `force-interactive` enters the WAM interactive
  path, makes no silent call and supplies neither account nor login hint; whether WAM
  presents interactive UI is provider behavior that the engine cannot enforce or
  verify. With this implementation WAM presented its UI in every observed forced call;
  with a login hint or a bound account it completed some forced calls without UI. That
  this continues on other hosts or account states is not established.
- **Finding F1.** With the `visual-studio-legacy-wam` Profile (`organizations` authority
  with MSA passthrough), a personal Microsoft account can reach a second provider page
  that does not offer it for direct selection, so the user enters the requested account
  manually. The final identity remains bound by strict validation: any returned account
  other than the requested email fails with `identity_validation_failed`.
- **Finding F2.** Closing a provider-owned WAM window after a product timeout or caller
  cancellation (lifetime-pipe EOF or caller cancellation) is unsupported. Such a window
  can remain after the process ends and the user closes it; the committed typed result
  and its no-token outcome are unaffected. See
  [request lifetime](#uml-ui-ownership-and-cancellation-sequence) and the
  [threat model](../security/threat-model.md#known-limitation-provider-window-after-non-human-termination).
- **Credential-method default (F3).** Observed only with the login hint, which forced
  interaction no longer supplies: WAM defaulted to a credential method the user then
  changed. The credential method WAM offers is provider behavior the engine does not
  select. The cause is not established, and no product change or owner decision is
  pending.
- **Defined but not validated by real-host evidence.** The `windows-wam` integration and
  the single-tenant (fixed-tenant) policy are defined by this design and covered only by
  controlled scenarios. Every real-host observation used the `visual-studio-legacy-wam`
  Profile with multitenant policy and `common`.

Profile activation, platform support and release each retain their own authorization
and evidence gates; the [validation strategy](../validation/strategy.md#release-gates)
lists them. Slice closure is not release readiness.
