# Windows Native AOT Synthetic Experiment

This is a completed experiment record for
[Issue #76](https://github.com/hcoona/microsoft-authentication-cli/issues/76) and its
[Issue #92](https://github.com/hcoona/microsoft-authentication-cli/issues/92) readiness
supplement, retained as evidence cited by current records. It authorizes no execution.
The Delivery Wave grants it ran under were completed and removed. The full protocol,
diagnostic rounds, procedures, recipes, allocations and accounting, source pins, hashes
and execution history remain at the
[pinned revision](https://github.com/hcoona/microsoft-authentication-cli/blob/242cd292a10ee5626e783f42349e570c2e5a19a1/docs/research/experiments/windows-native-aot.md).
The synthetic probe sources remain at the same revision:
[`windows-native-aot`](https://github.com/hcoona/microsoft-authentication-cli/tree/242cd292a10ee5626e783f42349e570c2e5a19a1/tools/probes/windows-native-aot)
and
[`windows-native-aot-readiness`](https://github.com/hcoona/microsoft-authentication-cli/tree/242cd292a10ee5626e783f42349e570c2e5a19a1/tools/probes/windows-native-aot-readiness).
The [experiment policy](../experiment-safety.md) governs any future experiment; another
run requires a new accepted protocol and authorization.

The [Native AOT assessment](#windows-native-aot-assessment) below, moved from the
[V1 baseline](../v1-public-contract-baseline.md) under
[Issue #509](https://github.com/hcoona/microsoft-authentication-cli/issues/509) with its
evidence types and dates unchanged, keeps the public-source findings and RECHECK-008
refresh. The [Windows design](../../designs/windows-ado-authentication.md) owns the publishing
selection, and the [validation strategy](../../validation/strategy.md) owns the remaining
validation obligations. The experiment sections keep only the bounded conclusions those
records rely on.

## Question and Common Basis

The experiment asked whether a pinned .NET 10 Windows x64 synthetic program can publish
with Native AOT and enter the actual upstream NativeInterop configuration-allocation
import with DLL search restricted to the application directory and System32. A failed
publish or failed allocation was a valid observed blocker. No broker startup, loader
workaround, warning suppression or non-AOT fallback was permitted.

- **Subject.** Synthetic probes only; no product CLI or library source was built.
  Dependencies were MSAL Client and Broker 4.83.1 and NativeInterop 0.20.3, with
  Abstractions 8.14.0, DiagnosticSource 6.0.1, Unsafe 6.0.0 and ValueTuple 4.5.0 pinned
  as direct constraints. All packages came from fixed public NuGet archives whose
  identities are retained at the pinned revision.
- **Host.** One existing Windows 11 x64 host driven through WSL 2, with existing .NET SDK
  10.0.401/runtime 10.0.12, MSVC 14.51.36231 and Windows SDK 10.0.26100.0. No tool or
  package installation, account, authentication, broker session, UI or resource
  operation occurred.
- **Call boundary.** This is a static source/IL finding, not a runtime observation. At
  public MSAL commit
  [`d5d7de6b`](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/tree/d5d7de6b103f0d9dd7bca9bf13cbb9f3da37bc9f),
  application building records a broker factory without invoking it, and constructing
  NativeInterop `AuthParameters` imports `MSALRUNTIME_CreateAuthParameters` from
  `msalruntime`. Disposal can call `MSALRUNTIME_ReleaseAuthParameters` and
  `MSALRUNTIME_Shutdown`, but the upstream safe-handle release catches exceptions, so
  managed disposal returning does not prove native release succeeded. Account
  enumeration, token acquisition, logging callbacks, discovery and broker sessions were
  never invoked.
- **Process accounting.** Every build and runtime action ran inside an owned Windows Job
  Object guard. Normal completion required zero remaining Job processes; any other
  outcome was a controller safety stop.

## Diagnostic Round 05 Results

**Runtime/synthetic observation (September 12, 2026 UTC):** The final round ran accepted
source `953b5ad9ea0fb470a54dc556955ec622211e6d6d` from a clean detached checkout, with a
dedicated scratch home, temp and HTTP-cache environment for every action.

- **Restore.** Restore completed normally. Complete screened diagnostics contained no
  warning text, diagnostic code, truncation or suppression. The ten-library graph and
  every package and download-pack payload matched the reused public archives. Selected
  assets were net8.0 Client, netstandard2.0 Broker, net9.0 NativeInterop and the
  package's x64 native DLL. This is a local-feed restore, not a clean public-network
  restore.
- **Publish.** The rooted Native AOT publish exited zero. Complete screened diagnostics
  contained no AOT, trim, managed-compiler or linker warning and no suppressed or
  truncated diagnostic. The selected provider API surface was rooted for compilation and
  never invoked at runtime. No suppression or dependency exception was needed for this
  exact synthetic graph.
- **Publish completion.** One verified MSVC `vctip.exe` (VC++ Technology Improvement
  Program telemetry uploader) Job member remained after the normal drain. Under the
  prospectively accepted identity/accounting criterion, owned-Job termination succeeded
  and the final active count was zero. This is an accepted VCTIP cleanup completion, not
  a zero-process normal drain. It does not prove absence of telemetry or opaque helper
  effects, and it does not reconstruct earlier unidentified survivors.
- **Static inspection.** Both published images are AMD64 PE32+ with no CLR directory.
  Their direct imports are Windows libraries and API-set contracts, with no delay
  imports. The output contains no PDB.

| Published artifact | Bytes | SHA-256 |
| --- | --- | --- |
| `NativeAotReadinessProbe.exe` | 8,643,584 | `31fc25f4b5704bb6837e1078d60a51c3ee4e225728bdcdd6650104e7b7a5577e` |
| `msalruntime.dll` | 2,949,656 | `9df30b54b7af974a072b1d55fee3590a5562c77ebc46f47016f0dd5199cd0c79` |

- **Cleanup case.** The published EXE ran with its bound x64 DLL in a fresh case
  directory and completed normally with zero Job processes and no termination. The
  native-AOT flag, restricted search, a first-chance exception self-check, MSAL builder
  creation, configuration allocation, cleanup-export checks, first and repeated
  disposal and application-directory module checks all passed. No unexpected preload
  and zero first-chance cleanup exceptions were observed.
- **Wrong-architecture case.** The same EXE ran with the genuine public x86 NativeInterop
  DLL, SHA-256 `ed45298d791cc6509ca5cdc3ed497b77ddfe6b5c1475c7fe9470b5c0e0f41c28`,
  substituted as `msalruntime.dll`. It returned the expected exit 1 with
  `System.BadImageFormatException`. Allocation and module loading were false, cleanup
  did not run, and the controller completed normally with zero Job processes and no
  termination. The expected negative exit is a passed rejection case.

**Inference:** Together with the pinned public call boundary, the round supports the
preimplementation Native AOT premise for the exact Windows x64 design: public asset
resolution, warning-free rooted compilation, accepted publish completion, restricted
native loading and allocation, observable release and shutdown without a managed or
wrapper-reported failure, and genuine wrong-architecture rejection.

**Limitations:** One synthetic executable on one existing host with development tools
installed. The evidence does not inspect opaque native deallocation, claim a shutdown
return status, enumerate dependencies loaded only by actual WAM or authentication paths,
or prove deployment on a host without development tools. Nonempty PE debug directories
do not select a product symbol policy. Full application, WAM, UI, account and
cancellation behavior, product symbols, performance, release and support are
**unvalidated** and remain later validation obligations.

## Retained Native Artifact Runtime Results

**Runtime/synthetic observation (September 12, 2026 UTC):** Earlier attempts ran the
retained Native AOT output of accepted source
`9055a9b473ab574a27469c96e31e1416ec69b14f` without a new restore or publish. All three
cases used the same 2,419,200-byte `NativeAotProbe.exe`, SHA-256
`e7fbef7f06f86236ae38658052e4e46d46e3851048c7ec878e215986214ae495`.

| Case | Actual observation |
| --- | --- |
| Positive | Exit 0; MSAL configuration created; native module loaded from the application directory. |
| Missing library | Exit 1; `System.DllNotFoundException`; native module not loaded. |
| Working-directory/PATH decoy | Exit 1; `System.DllNotFoundException`; native module not loaded. The genuine DLL was present only in the working directory, which was also first in PATH. |

All three subjects reported Native AOT execution, restricted search, no unexpected
preload and successful builder creation. Every controller completed with zero Job
processes, no termination request and no safety stop. The negative exits are expected
observations, not failed controller executions.

**Inference:** This exact .NET 10/MSAL 4.83.1/Broker 4.83.1/NativeInterop 0.20.3
synthetic executable can run as Native AOT, build its in-memory MSAL configuration and
enter the actual upstream configuration-allocation import under the application
directory/System32 search restriction. The missing and decoy cases demonstrate
missing-library failure and rejection of that working-directory/PATH placement.

**Limitations:** These results bind only that retained artifact. Its publish ended in a
controller safety stop whose origin is unknown and whose publish diagnostics were not
retained; these successful cases neither diagnose that stop nor imply zero AOT/trim
warnings for that artifact. Runtime intervals were single runs, not a benchmark.

## Historical Stops and Preserved Unknowns

**Runtime/synthetic observation:** Earlier preparation and rounds observed these
blockers. None was evidence of an MSAL, Broker or NativeInterop package rejection,
network blocking, or Native AOT compiler failure.

- **Preparation.** Initial restores failed until the SDK first-use control, the NuGet
  program-files environment inputs and two publicly downloadable 10.0.12 runtime packs
  (WindowsDesktop and ASP.NET Core) were corrected. Supplying the actual Windows OS
  identifier then let the unchanged publish produce a native artifact. The first
  failed restore's exact cause remains unknown because raw output was not retained.
- **Original publish stop.** The publish that produced the retained artifact exited zero,
  but the controller recorded a safety stop. Its origin and publish diagnostics remain
  **unknown**; later warning-free output does not recover them.
- **Readiness and early diagnostic stops.** Several later publishes exited zero but left
  one Job process at the immediate zero-process check, so the controller terminated the
  owned Job and stopped. Where identity was later sampled, the survivor matched the
  expected MSVC `vctip.exe` path lexically; earlier survivors remain unidentified. One
  diagnostic preflight was rejected because its encoded command exceeded the documented
  `CreateProcessW` command-line limit; its actual error was not retained. These stops
  led to the prospective completion criterion used in Round 05.
- **Public VCTIP context (public documentation, not an observation here).** Microsoft's
  [BuildXL MSVC definition](https://github.com/microsoft/BuildXL/blob/280e155410f72498fbf908913e7027a99c0892e1/Public/Sdk/Experimental/Msvc/VisualCpp/visualCpp.dsc)
  identifies `VCTIP.EXE` as the VC++ Technology Improvement Program telemetry uploader
  and lists it among surviving compiler children. The searched public guidance
  established no process-local suppression control for the pinned compiler; that is a
  search limit, not proof that none exists.

All stopped roots, receipts and artifacts were intentionally retained read-only. No
stopped root, consumed allowance or unused buffer may be replayed or reused. Historical
Windows trust-processing uncertainty also remains unchanged.

## Current Bounded Conclusion

**Inference:** Public toolchain and Win32 contracts plus the bounded observations above
support selecting Native AOT for the exact .NET 10 Windows x64 Win32 host at the
preimplementation design level, without changing authentication dependencies, relaxing
DLL search, suppressing warnings or accepting a non-AOT exception. The native DLL and
applicable Windows components remain required assets; single-executable native
compilation is not a one-file distribution guarantee.

**Unvalidated:** Clean public-network restore, dynamic WAM dependency closure,
deployment without development tools, opaque native deallocation, product build helpers
and symbols, startup, memory and size, and complete-application WAM, account, UI,
cancellation, release and support behavior. Those remain obligations of the validation
strategy and a separately authorized implementation.

## Windows Native AOT Assessment

**RECHECK-008 refreshed September 12, 2026 UTC, for Issue #92 publishing selection:** Current Microsoft Learn
[Native AOT guidance](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)
continues to list Windows x64 as supported and now explicitly specifies Visual Studio
2022 **or later** with Desktop development with C++ and default components. The existing
Visual Studio 18 toolchain therefore does not require an older installation merely to
match that prerequisite. Exact compiler/SDK identities remain experimental inputs.
The current [trimming limitations](https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/incompatibilities)
still exclude supported Windows Forms/WPF trimming and built-in COM. Retain the small
Win32/source-generated-interop design and explicit JSON readers/writers.

Both registered upstream issues and their four comments each were retrieved anonymously
from the public GitHub API. Issue 5226 remains closed as completed, last updated August
11, 2025, and concerns NativeInterop 0.18.1/MSAL 4.70.1 on .NET 9; its comments supply no
exact-version validation for this target. Issue 5248 remains closed as completed, last
updated September 11, 2025; its July 1 comment specifically recommends an AOT-published
test application for the netstandard dependency. Neither closure establishes the
complete Broker path. Current guidance also explains that AOT assembly metadata was
introduced in .NET 10: absence of that new metadata on older-targeted dependencies is
not by itself proof of incompatibility.

The `native-aot-publishing` decision trigger is satisfied by this source assessment
and the exact-graph/runtime evidence below. The refreshed public sources remain compatible
with the selected Win32 route; none establishes full Broker compatibility by itself.
The Windows design consumes their bounded inference and the validation strategy retains
later application obligations. No interaction, account, cache, Profile, Linux-broker,
browser or support decision follows from this refresh.

**Public desk findings, retrieved 2026-09-12 UTC:** This assessment applies to the
Windows Slice's SDK 10.0.401/runtime 10.0.12, `net10.0-windows`/`win-x64`, MSAL and
Broker 4.83.1, and NativeInterop 0.20.3. No package was restored, loaded, built, published,
or authenticated for these findings. Public NuGet archives were inspected as data only.

| Public input | Finding and limit |
| --- | --- |
| [.NET Native AOT overview](https://github.com/dotnet/docs/blob/cc76ba79b6b0de125017d49b93ae101f13e3b2aa/docs/core/deploying/native-aot/index.md), prerequisites and limitations | Windows x64 is a supported compilation target. Native AOT requires trimming and excludes runtime code generation and built-in COM. Windows publishing needs the public Visual Studio C++ toolchain; the SDK pin alone is not the full reproducible build input. |
| [Trimming incompatibilities](https://github.com/dotnet/docs/blob/cc76ba79b6b0de125017d49b93ae101f13e3b2aa/docs/core/deploying/trimming/incompatibilities.md), WPF and Windows Forms | Microsoft disables supported trimming for Windows Forms because of built-in COM reliance, and for WPF because necessary runtime/reflection behavior cannot be preserved by trimming analysis. Replacing WinForms with WPF does not supply a supported AOT path. |
| [P/Invoke source generation](https://github.com/dotnet/docs/blob/cc76ba79b6b0de125017d49b93ae101f13e3b2aa/docs/standard/native-interop/pinvoke-source-generation.md) and [Native AOT interop](https://github.com/dotnet/docs/blob/cc76ba79b6b0de125017d49b93ae101f13e3b2aa/docs/core/deploying/native-aot/interop.md) | Compile-time marshalling and static unmanaged entry points avoid runtime-generated interop stubs. Ordinary Native AOT P/Invoke binds lazily by default; optional direct binding uses OS loader rules and does not honor `DefaultDllImportSearchPaths`. This supports a statically known Win32 surface, not an automatic guarantee for every third-party loader. |
| Win32 [window creation](https://github.com/MicrosoftDocs/win32/blob/b46b3638e4691f4f451590e1f861db27649849bf/desktop-src/LearnWin32/creating-a-window.md) and [window messages](https://github.com/MicrosoftDocs/win32/blob/b46b3638e4691f4f451590e1f861db27649849bf/desktop-src/LearnWin32/window-messages.md) | Native windows expose HWNDs and receive messages through a registered window procedure. An application-owned thread/message loop can own a small parent and standard controls without Windows Forms. Correct callback lifetime, thread ownership, accessibility and cancellation remain application obligations. |
| MSAL [shared build properties](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/blob/d5d7de6b103f0d9dd7bca9bf13cbb9f3da37bc9f/src/Directory.Build.props), [client project](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/blob/d5d7de6b103f0d9dd7bca9bf13cbb9f3da37bc9f/src/client/Microsoft.Identity.Client/Microsoft.Identity.Client.csproj), and [JSON helper](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/blob/d5d7de6b103f0d9dd7bca9bf13cbb9f3da37bc9f/src/client/Microsoft.Identity.Client/Utils/JsonHelper.cs) | The net8-compatible build enables `IsAotCompatible`; the client supplies a net8.0 asset and its modern JSON path uses the generated `MsalJsonSerializerContext.Custom`. These are source-level AOT provisions. The separate Broker netstandard2.0 project does not thereby inherit the net8 AOT annotation. |
| [NativeInterop 0.20.3 public package](https://www.nuget.org/packages/Microsoft.Identity.Client.NativeInterop/0.20.3) | The archive includes `lib/net9.0`, `lib/netstandard2.0`, and `lib/net462` managed assets and `runtimes/win-x64/native/msalruntime.dll`. Its net9.0 dependency group has no declared managed dependencies. The net9.0 DLL contains `IsTrimmable=True` metadata and the `get_BaseDirectory` string; unlike the older asset, it has no `get_Location` string. String/metadata inspection is not a full IL audit or a proof of the executed loading path. Public source links identify private OneAuth; that source was not accessed and is not an evidence dependency. |

The three NuGet archives' SHA-512 identities, in Client/Broker/NativeInterop order, are:

- `692ae5e6b961a2ef71b747a9877f7a7f0460a03f9fb2edc0fa7e4d457a5419a0f564afae53c6296b7e75e0ab2b1c61b3f621a9d56e99945bb047b02dcfe9a2bd`
- `9923928bde2049ed3ec125f871eb37f125a2bb28d20e0d5ebdf59d1a7cb1f37858f4c7d818dd25fd72f1fa7ae96a01a1320d1a21bb3ba3a1379d3fe37463f2ec`
- `e8d30c22acc6c14d91f09c9e8204278357f2500a11e1e7befb1443f0e806a9dd5522938d37733bfe3de11a1c4e30ccea4755f80fcd1f9de6d8c87a88910ae5cd`

The first two packages' public repository metadata identifies the MSAL source commit
above. These archive identities support recovery of the metadata assessment; they are
not product lock files or installed-dependency acceptance.

**Reported historical issue, not a current-version observation:** MSAL
[issue 5226](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/issues/5226)
reports an `Assembly.Location`/IL3000 Native AOT failure with MSAL 4.70.1 and
NativeInterop 0.18.1 on .NET 9. It was closed on August 11, 2025. Issue closure does not
identify a verified fix for this Slice's exact dependency set. Likewise,
[issue 5248](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/issues/5248)
and the AOT annotation work in [PR 5458](https://github.com/AzureAD/microsoft-authentication-library-for-dotnet/pull/5458)
do not establish end-to-end Windows Broker compatibility. Do not extrapolate the older
failure to 0.20.3 or extrapolate annotation to successful WAM execution.

**Accepted synthetic evidence, September 12, 2026:** The
[latest round](#diagnostic-round-05-results) binds
accepted source `953b5ad9ea0fb470a54dc556955ec622211e6d6d` to the existing Windows
host and exact public toolchain/package inputs. Its local-feed restore verified the ten
resolved libraries, including net8.0 Client, netstandard2.0 Broker, net9.0 NativeInterop
and the pinned x64 native DLL, against the retained public archives. This reused-feed
result is not a clean public-network restore.

Complete screened native publish diagnostics contained no AOT/trim/compiler/link warning
or suppressed diagnostic. The selected provider surface was rooted for compilation and
never invoked. Publish completion used the prospectively accepted current VCTIP
identity/accounting and owned-cleanup criterion, with final zero-process quiescence.
Static PE inspection found native x64 images, Windows/API-set direct imports and no
delay imports. Neither package annotations nor these import tables prove dynamic WAM
dependency closure or product deployment without development-machine components.

The actual native EXE then created MSAL configuration and allocated parameters through
the public NativeInterop API. Cleanup exports were present, first and repeated disposal
returned, and a self-checked first-chance observer saw no cleanup exception. This
supports observable cleanup without a managed or wrapper-reported failure, not proof
of opaque native deallocation or a returned shutdown status. The separate genuine x86
case rejected the DLL without loading the native module. Both runtime controllers
completed normally with zero Job counts and no termination. The
[earlier missing/decoy cases](#retained-native-artifact-runtime-results)
retain their own artifact-specific restricted-search observations.

**Design inference:** The public Windows x64 toolchain, small Win32/static-interop host,
resolved dependency assets, rooted warning-free publish and bounded native runtime
observations support the preimplementation Native AOT selection for the exact Windows
Slice. No present synthetic evidence requires a package replacement, warning suppression,
search-boundary exception or non-AOT mode. This resolves the design premise; it does not
establish the complete application's WAM, account, UI, cancellation or deployment
behavior. The native DLL and applicable Windows components remain required assets;
single-executable native compilation is not a one-file distribution guarantee.
No startup, memory, size, authentication or cancellation benefit was measured.

The [experiment authority](windows-native-aot.md) preserves every earlier
stop, source/artifact identity, failed preparation, exhausted allowance and unknown.
In particular, attempt 10's stop origin and missing diagnostics remain unknown; new
warning-free output does not recover them. The stopped readiness, recovery and diagnostic
roots remain ineligible for replay. The current VCTIP criterion does not retrospectively
turn an old stop into success or establish absence of telemetry/helper effects.

Immutable snapshots retain their evidence level. Mutable guidance and compatibility
reports remain routed by RECHECK-008 for later applicable decisions and releases.
The [Windows design](../../designs/windows-ado-authentication.md#native-aot-target-disposition)
owns the publishing choice, and the
[validation strategy](../../validation/strategy.md#native-aot-publishing) owns remaining
full-application, WAM/UI, dynamic-dependency, symbol, performance and support obligations.
