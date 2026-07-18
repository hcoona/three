# Atlas V0 A1 Foundation Plan

**Status:** Proposed execution plan; execution requires an exact-plan independent review record

**Increment:** A1 - C# Foundation

**Implementation language:** C# on the repository-pinned .NET 10 SDK

**Governing baseline:** `atlas-v0-execution-plan.md`, Increment A1

**A0 release gate:** `../reviews/atlas-v0-a0-release-gate.md`

## 1. Purpose

A1 establishes only the three C# boundaries required for later Atlas work:

1. a reusable library;
2. a thin command-line executable; and
3. a directly runnable test project.

The increment proves dependency direction, deterministic output, cancellation propagation,
exit-code behavior, and process-free testability. It does not inspect, copy, decode, interpret, or
write a save. It does not access the installed game or the private Atlas workspace.

## 2. Dependencies and entry conditions

A1 implementation may begin only when:

- A0 release commit `5681fccc8af78a8253e5d995f90825ecd387350d` is on the shared branch;
- this plan is committed and pushed;
- an independent subagent reviews the exact plan commit and reports `No findings`;
- the plan-review record is committed and pushed as the only change after that plan candidate; and
- the tracked worktree is clean.

The repository provides:

- .NET SDK `10.0.300` through `global.json`;
- target framework `$(CurrentTargetFramework)`, currently `net10.0`;
- nullable, analysis, deterministic-build, and warnings-as-errors policy through
  `Directory.Build.props`;
- Central Package Management through `Directory.Packages.props`;
- `xunit.v3.mtp-v2` and Microsoft.Testing.Platform; and
- automatic project discovery through root `dirs.proj`.

No package version, traversal file, solution file, or root build property should change in A1.

## 3. Risks, limitations, and disagreements

Unresolved disagreements: None.

Accepted residual limitations:

- A1 does not inject an operating-system Ctrl+C event. It exercises `Program.Main` startup and
  shutdown in subprocess tests, directly tests caller-token propagation, and requires review of
  handler registration, `Cancel = true`, cancellation, and `finally` removal.
- The subprocess smoke test is Windows-only because repository policy builds C# projects on
  Windows. It launches the generated `.exe` apphost directly.
- The versioned smoke document is not a canonical Atlas observation schema. A later plan must
  define that schema independently.

If any limitation prevents an acceptance criterion from being measured, A1 stops and this plan is
revised before implementation continues.

## 4. Exact project layout

A1 creates these projects:

Source-project root: `src\private\app\celesphonia-modifier\`

- Library:
  `Hcoona.CelesphoniaModifier.Atlas\Hcoona.CelesphoniaModifier.Atlas.csproj`
- CLI:
  `Hcoona.CelesphoniaModifier.Atlas.Cli\Hcoona.CelesphoniaModifier.Atlas.Cli.csproj`

Test-project root: `tests\private\app\celesphonia-modifier\`

- Tests:
  `Hcoona.CelesphoniaModifier.Atlas.Tests\Hcoona.CelesphoniaModifier.Atlas.Tests.csproj`

Each project also commits its generated `packages.lock.json`.

Project identities are:

| Project | Root namespace                           | Assembly or executable                   |
| ------- | ---------------------------------------- | ---------------------------------------- |
| Library | `Hcoona.CelesphoniaModifier.Atlas`       | `Hcoona.CelesphoniaModifier.Atlas`       |
| CLI     | `Hcoona.CelesphoniaModifier.Atlas.Cli`   | `celesphonia-atlas`                      |
| Tests   | `Hcoona.CelesphoniaModifier.Atlas.Tests` | `Hcoona.CelesphoniaModifier.Atlas.Tests` |

The implementation candidate contains exactly this new tracked file manifest:

Library directory:

- `Hcoona.CelesphoniaModifier.Atlas.csproj`;
- `packages.lock.json`; and
- `EmptyAtlasSurvey.cs`.

CLI directory:

- `Hcoona.CelesphoniaModifier.Atlas.Cli.csproj`;
- `packages.lock.json`;
- `Program.cs`; and
- `AtlasCliApplication.cs`.

Test directory:

- `Hcoona.CelesphoniaModifier.Atlas.Tests.csproj`;
- `packages.lock.json`;
- `EmptyAtlasSurveyTests.cs`;
- `AtlasCliApplicationTests.cs`;
- `AtlasProcessSmokeTests.cs`; and
- `ProjectBoundaryTests.cs`.

Test-only streams and other helpers are private nested types in their owning test file. No other
tracked file is permitted under the three project directories. Ignored `bin` and `obj` outputs are
not candidate files.

The root traversal already discovers all three projects. A1 does not add a nested solution or
project traversal file.

## 5. Dependency direction

The permitted dependency graph is:

```text
Hcoona.CelesphoniaModifier.Atlas.Tests
    -> Hcoona.CelesphoniaModifier.Atlas.Cli
    -> Hcoona.CelesphoniaModifier.Atlas

Hcoona.CelesphoniaModifier.Atlas.Tests
    -> Hcoona.CelesphoniaModifier.Atlas
```

The library:

- has no `ProjectReference`;
- declares no project-local `PackageReference`;
- uses only .NET Base Class Library APIs;
- does not depend on `System.CommandLine`;
- does not reference the CLI, WinUI, Windows App SDK, JavaScript, an Agent SDK, or a network
  service; and
- does not contain process, console, environment, or installed-game access.

The CLI:

- references only the Atlas library as a project;
- declares no project-local `PackageReference`;
- binds command-line input, maps results to output channels and exit codes, and handles Ctrl+C;
- contains no save discovery, codec, graph, schema interpretation, or write semantics; and
- exposes internals only to the test assembly through `InternalsVisibleTo`.

The test project:

- references the library and CLI projects;
- uses synthetic in-memory inputs and repository-safe project metadata; and
- never reads game-derived or private data.

Its exact direct `PackageReference` set is:

- `Microsoft.NET.Test.Sdk`;
- `Microsoft.Testing.Extensions.CodeCoverage`;
- `Microsoft.Testing.Extensions.TrxReport`;
- `xunit.v3.mtp-v2`;
- `xunit.runner.visualstudio` with `PrivateAssets` and the repository-standard `IncludeAssets`; and
- `coverlet.collector` with `PrivateAssets` and the repository-standard `IncludeAssets`.

No other test package is permitted. The test project sets `MSTestAnalysisMode` to `None`, matching
the repository's xUnit v3 projects.

Repository-wide `GlobalPackageReference` items inherited from `Directory.Build.props` remain
allowed build infrastructure. "No package reference" in this plan means no project-local direct
declaration. Boundary tests inspect direct project XML separately from inherited global packages.

## 6. Project properties

Every project sets:

```xml
<TargetFramework>$(CurrentTargetFramework)</TargetFramework>
<ImplicitUsings>enable</ImplicitUsings>
<Nullable>enable</Nullable>
```

The CLI additionally sets:

```xml
<OutputType>Exe</OutputType>
<AssemblyName>celesphonia-atlas</AssemblyName>
<UseAppHost>true</UseAppHost>
```

Root analysis and warnings-as-errors settings remain inherited. A1 does not suppress analyzers,
lower warning severity, enable unsafe code, or relax nullable checks.

The test project emits one `AssemblyMetadataAttribute` named `AtlasCliAppHostPath`. Its value is the
absolute Windows path to the CLI project's
`bin\$(Configuration)\$(TargetFramework)\celesphonia-atlas.exe`. Process smoke tests read this
metadata and fail if the apphost is absent.

## 7. Empty-survey document contract

The document version is:

`atlas-empty-survey/v1`

The library writes exactly these bytes:

```text
{"schemaVersion":"atlas-empty-survey/v1","observations":[]}\n
```

The output is 60 bytes. Its hexadecimal representation is:

```text
7B 22 73 63 68 65 6D 61 56 65 72 73 69 6F 6E 22 3A 22 61 74 6C 61 73 2D 65 6D 70 74 79
2D 73 75 72 76 65 79 2F 76 31 22 2C 22 6F 62 73 65 72 76 61 74 69 6F 6E 73 22 3A 5B 5D 7D 0A
```

Spaces and the visual line break in the hex block are presentation only. The `\n` notation in the
JSON block means one final byte `0A`, not two characters. The byte contract is:

- UTF-8 without BOM;
- one compact JSON object;
- member order exactly as shown by the byte contract;
- no spaces or indentation;
- one LF after the object;
- no CR;
- no timestamp, machine name, path, hash, random identifier, or environment-derived value; and
- no culture-sensitive formatting.

Repeated writes in the same or separate processes must be byte-identical.

A1 deliberately does not add a formal JSON Schema or a JSON Schema validator. The
`schemaVersion` member makes the smoke document version explicit, while the exact-byte and BCL
JSON-shape tests define its A1 contract. A later increment must introduce a separately reviewed
canonical Atlas schema rather than silently broadening this smoke document.

## 8. Minimal library contract

The library exposes only the A1 operation surface:

```csharp
namespace Hcoona.CelesphoniaModifier.Atlas;

public static class EmptyAtlasSurvey
{
    public const string SchemaVersion = "atlas-empty-survey/v1";

    public static ValueTask WriteAsync(
        Stream destination,
        CancellationToken cancellationToken = default);
}
```

Behavior:

- a null `destination` throws `ArgumentNullException`, even when cancellation is requested;
- after the null check, cancellation is checked before inspecting or writing the stream;
- a non-writable or disposed stream whose `CanWrite` is false throws `NotSupportedException`;
- a failure thrown while reading `CanWrite` propagates unchanged;
- the token is passed to the asynchronous stream write;
- all 60 bytes are submitted in one `WriteAsync` call;
- cancellation and stream failures propagate to the caller;
- the method writes the exact bytes from section 7;
- the method does not flush, close, or dispose the caller-owned stream; and
- the method performs no file, console, environment, network, or private-workspace access.

No interface, dependency-injection registration, repository abstraction, options type, result
hierarchy, or general serializer abstraction is introduced in A1.

## 9. CLI contract

The executable name is `celesphonia-atlas`. A1 supports one operation:

```text
celesphonia-atlas empty-survey
```

It also supports root and command help through exactly `-h` and `--help`. `empty-survey` accepts no
options, operands, paths, or private values.

Argument matching uses ordinal BCL sequence comparisons. A1 does not use `System.CommandLine` or
another parser package because the complete accepted surface is only these sequences:

- `empty-survey`;
- `-h`;
- `--help`;
- `empty-survey -h`; and
- `empty-survey --help`.

Every other sequence is invalid. The matcher never expands response files, evaluates directives,
reads a path, performs suggestion or version actions, applies POSIX option bundling, or invokes a
framework exception or process-termination handler. In particular, `@file`, `--version`,
`[suggest]`, `--`, `/h`, `-?`, and `/?` are invalid and are never interpreted.

The CLI application has a process-free internal production entry point that accepts:

- an argument array;
- caller-owned standard-output and standard-error streams; and
- a cancellation token.

An internal test overload additionally accepts:

```csharp
Func<Stream, CancellationToken, ValueTask> writeEmptySurvey
```

The production overload passes `EmptyAtlasSurvey.WriteAsync`. The delegate is the only injected
operation seam; no global mutable hook, interface, container, or service locator is added.

`Program.Main` owns only console stream selection, Ctrl+C registration, and synchronous process
exit. The internal application performs exact argument matching and invokes the operation
delegate.

### 9.1 Output channels

- Successful `empty-survey`: stdout receives the exact bytes from section 7; stderr is empty.
- Help: stdout receives the exact help bytes below; stderr is empty.
- Invalid arguments: stdout is empty; stderr receives `Invalid arguments.` plus LF.
- Cancellation: stdout is empty when canceled before writing; stderr receives
  `Operation canceled.` plus LF.
- I/O failure: stdout makes no success claim; stderr receives `I/O failure.` plus LF.
- Unexpected failure: stdout makes no success claim; stderr receives `Unexpected failure.` plus
  LF.

Root and command help use the same exact UTF-8 text:

```text
Usage:
  celesphonia-atlas empty-survey

Commands:
  empty-survey  Write a deterministic empty Atlas survey.

Options:
  -h, --help  Show help.
```

The text has LF line endings and one final LF. Diagnostics never include exception messages, stack
traces, argument tokens or values, environment values, or paths.

### 9.2 Exit codes

| Code | Meaning                                  |
| ---: | ---------------------------------------- |
|    0 | Command or help completed successfully   |
|    1 | Unexpected internal failure              |
|    2 | Command-line usage or validation failure |
|    3 | Operation canceled                       |
|    4 | Standard-stream I/O failure              |

No failure path returns zero. Cancellation is not reported as an unexpected failure.

### 9.3 Result precedence

The internal CLI application applies this order:

1. Null argument or stream parameters throw `ArgumentNullException` to the direct caller.
2. Arguments are matched before operation cancellation is observed.
3. Help returns 0 even when the operation token is already canceled.
4. Invalid arguments return 2 even when the operation token is already canceled.
5. A help or diagnostic stream failure returns 4.
6. A valid operation receives the caller's token.
7. A caller-related `OperationCanceledException` returns 3.
8. A production stream-output failure returns 4.
9. Any other operation exception returns 1.
10. Failure to write a terminal diagnostic changes the result to 4.

An `OperationCanceledException` is caller-related only when the caller token is requested and the
exception token is either the caller token or the default token. An exception carrying a different
non-default token, or any cancellation exception when the caller token is not requested, is an
unexpected failure.

Production stream-output failures are `IOException`, `ObjectDisposedException`,
`NotSupportedException`, and the `NotSupportedException` used for a non-writable destination.
Exception messages are never inspected or emitted.

Help and terminal diagnostics use `CancellationToken.None` so an already-canceled operation token
cannot suppress result presentation. This is the only permitted use of `CancellationToken.None`;
the operation always receives the caller token.

A stream may write a prefix before failing or observing cancellation. The CLI cannot retract those
bytes. It returns 3 or 4 as classified above, emits the applicable terminal diagnostic when
possible, and never emits or returns a success claim.

### 9.4 Cancellation

`Program.Main` registers a `Console.CancelKeyPress` handler, sets `Cancel = true`, and cancels one
owned `CancellationTokenSource`. The token flows through the internal CLI application into
`EmptyAtlasSurvey.WriteAsync` and its asynchronous stream write.

The handler is removed in `finally`. Cancellation is not swallowed or converted to success.

A1 introduces no asynchronous file or scan boundary. Future boundaries must accept and pass the
same operation token before A1's cancellation criterion can remain satisfied.

## 10. Test plan

The test project adds focused tests in these groups.

### 10.1 Library tests

- `SchemaVersion` equals `atlas-empty-survey/v1`.
- One write matches the exact byte sequence in section 7.
- Repeated writes are byte-identical.
- Output is valid JSON with exactly the required properties.
- The output is 60 bytes and matches the documented hex sequence.
- A null destination throws `ArgumentNullException` before cancellation is considered.
- A pre-canceled token throws `OperationCanceledException` before a write.
- A pre-canceled write to a non-writable stream throws `OperationCanceledException`.
- A non-writable and a disposed stream throw `NotSupportedException` when not canceled.
- A stream whose `CanWrite` getter throws propagates that exception unchanged.
- A stream that cancels during `WriteAsync` receives the caller's token.
- A successful write makes exactly one stream-write call with all 60 bytes.
- A stream failure propagates.
- The writer leaves the destination open and does not flush it.

### 10.2 CLI application tests

- Null arguments, stdout, stderr, or injected operation throw `ArgumentNullException`.
- `empty-survey` returns 0, writes exact stdout bytes, and leaves stderr empty.
- Root and command help return 0 with exact help bytes and do not invoke the operation.
- Missing, unknown, extra, and option-bearing input returns 2.
- Case variants such as `EMPTY-SURVEY` and `--HELP` return 2.
- `@file`, directives, version, terminators, and undeclared help aliases return 2 without file I/O.
- Invalid input writes only `Invalid arguments.` plus LF and leaves stdout empty.
- Help and invalid input take precedence over pre-cancellation.
- Pre-cancellation returns 3 and writes the cancellation diagnostic.
- Caller-token and default-token cancellation map to 3 only when the caller token is requested.
- Foreign-token or unsolicited cancellation maps to 1.
- Partial-write cancellation returns 3 and makes no success claim.
- Standard-output and standard-error failure precedence follows section 9.3.
- Partial-write I/O failure returns 4 and makes no success claim.
- An unexpected injected operation failure returns 1 without exception details.
- A terminal diagnostic failure returns 4.
- Output and error streams remain caller-owned.

### 10.3 Process smoke tests

- A built `celesphonia-atlas.dll` process returns 0 for `empty-survey`.
- Captured stdout bytes exactly match section 7 and captured stderr is empty.
- An invalid-argument process returns 2, writes the fixed stderr bytes, and leaves stdout empty.
- The process tests exercise `Program.Main`, console stream selection, and argument forwarding.
- The process launches the apphost path from `AtlasCliAppHostPath` and fails if it is unavailable.
- Core library and CLI behavior remains covered by process-free tests.

### 10.4 Boundary tests

- The library project declares no project-local `PackageReference` or `ProjectReference`.
- The CLI project declares the library reference and no project-local `PackageReference`.
- The test project references both A1 production projects.
- The test project has exactly the direct package set and asset metadata from section 5.
- Core empty-survey behavior is exercised directly without starting a process.
- No test reads the game installation, a live save directory, or `.private`.

Project-file boundary assertions inspect repository-safe project metadata from paths derived from
the test assembly location. They do not assume the current working directory.

## 11. Implementation sequence

### A1.1 Project scaffold

Create the three project files, project references, test package references, and namespaces.
Run one ordinary restore to generate initial lock files. Do not add behavior beyond compile-safe
placeholders.

Acceptance:

- all three projects are discovered by root traversal;
- restore succeeds without package-version changes;
- dependency direction matches section 5; and
- both production projects compile with no project-local package reference.

### A1.2 Deterministic library operation

Add `EmptyAtlasSurvey` and its exact document contract.

Acceptance:

- exact-byte, JSON-shape, ownership, failure, and cancellation tests pass; and
- the library remains free of CLI and environment dependencies.

### A1.3 Thin CLI

Add `empty-survey`, help, output-channel behavior, exit-code mapping, and Ctrl+C propagation.

Acceptance:

- direct CLI application tests pass;
- the CLI has only the Atlas library project dependency;
- every undeclared argument sequence is rejected without interpretation or file access;
- no save semantics exist in the CLI; and
- no diagnostic discloses argument values or exception details.

### A1.4 Integrated validation

Update `.copilot\README.md` so it no longer claims that no projects or package references exist.
Run the traversal, locked restore, build, format, test, and smoke commands from section 12.

Acceptance:

- every command succeeds from the repository root;
- automated process smoke tests prove exit code and raw stdout and stderr bytes;
- locked restore succeeds without changing dependency resolution;
- root traversal enumerates all three A1 projects;
- no new warning appears; and
- the implementation diff changes only the paths permitted below.

The implementation candidate is compared with the pushed plan-review record commit. Its allowed
paths are exactly:

- the file manifest in section 4, rooted at its declared source and test directories; and
- `src\private\app\celesphonia-modifier\docs\.copilot\README.md`.

The candidate check rejects every additional tracked file, including an undeclared file inside an
A1 project directory. Changing this plan, a governing plan, root build infrastructure, package
versions, or any other path requires a new plan candidate and plan review before implementation
continues.

### A1.5 Independent release gate

The implementation candidate must descend from the pushed A1 plan-review record commit. Commit and
push the exact candidate, then record its commit, tree, plan-review base commit, and changed paths.
Rerun every section 12 check on that exact commit and require the tracked worktree to remain clean.
An independent subagent then reviews the full candidate against this plan, A0 privacy constraints,
tests, and command output. Resolve every finding and repeat until the exact result is
`No findings`.

Persist `../reviews/atlas-v0-a1-release-gate.md` as the only change after the accepted candidate.
The record contains every operating-model field:

- increment identifier `A1` and final outcome;
- candidate commit and tree;
- governing-plan path and final persisted plan commit;
- plan-review record commit and implementation diff base;
- reviewed path set and acceptance criteria;
- reviewer identity and independence attestation;
- every review iteration, finding disposition, and final `No findings` result;
- validation commands or procedures and their outcomes;
- an explicit statement that A1 created and accessed no private evidence; and
- first-parent and changed-path requirements.

Commit only the release record, mechanically verify that its first parent is the reviewed candidate
and that its diff contains only the release-record path, then push it. Verify the release-record
commit equals upstream. Only after those checks may A1 move to `done` or A2 begin.

## 12. Validation commands

Run every .NET command from the repository root with the user-required wrapper and Windows paths.

Define the project paths:

```powershell
$sourceRoot = "src\private\app\celesphonia-modifier"
$libraryProject = Join-Path $sourceRoot `
  "Hcoona.CelesphoniaModifier.Atlas\Hcoona.CelesphoniaModifier.Atlas.csproj"
$cliProject = Join-Path $sourceRoot `
  "Hcoona.CelesphoniaModifier.Atlas.Cli\Hcoona.CelesphoniaModifier.Atlas.Cli.csproj"
$testRoot = "tests\private\app\celesphonia-modifier"
$testProject = Join-Path $testRoot `
  "Hcoona.CelesphoniaModifier.Atlas.Tests\Hcoona.CelesphoniaModifier.Atlas.Tests.csproj"
```

Prove root traversal includes all three projects:

```powershell
$graph = mise exec -- dotnet msbuild dirs.proj '-getItem:ProjectReference' |
  ConvertFrom-Json
$identities = @($graph.Items.ProjectReference.Identity)
$expected = @($libraryProject, $cliProject, $testProject)
$missing = @($expected | Where-Object { $_ -notin $identities })
if ($missing.Count -ne 0) {
  throw "Root traversal omitted an A1 project."
}
```

After A1.1 generates the initial lock files, restore in locked mode:

```powershell
mise exec -- dotnet restore $testProject --locked-mode
```

On an exact committed candidate or fresh checkout, also require locked restore to leave the tracked
worktree clean:

```powershell
if (git status --porcelain --untracked-files=no) {
  throw "Locked restore changed tracked files."
}
```

Build the complete A1 graph:

```powershell
mise exec -- dotnet build $testProject --no-restore
```

Verify formatting for all three projects:

```powershell
foreach ($project in @($libraryProject, $cliProject, $testProject)) {
  mise exec -- dotnet format $project --no-restore --verify-no-changes
  if ($LASTEXITCODE -ne 0) {
    throw "Formatting validation failed."
  }
}
```

Run targeted tests:

```powershell
mise exec -- dotnet test --project $testProject --no-restore
```

The test run includes raw-byte subprocess tests. A human-readable smoke command may also be run
after the build:

```powershell
mise exec -- dotnet run --project $cliProject --no-build -- empty-survey
```

The observational command must display the compact object:

```text
{"schemaVersion":"atlas-empty-survey/v1","observations":[]}
```

It is not acceptance evidence by itself. `AtlasProcessSmokeTests` must launch the built CLI,
capture raw stdout and stderr, and assert the exact bytes and exit codes.

For clean-checkout acceptance, repeat traversal, locked restore, build, format, and test in a
fresh worktree or CI checkout. Do not delete or reset the current shared worktree to simulate
cleanliness.

## 13. Increment acceptance criteria

A1 is accepted only when:

1. all outputs in section 4 exist;
2. a clean checkout passes traversal, locked restore, build, format, and targeted tests;
3. all projects use `$(CurrentTargetFramework)`, nullable, and implicit-usings conventions;
4. the dependency graph exactly matches section 5;
5. the library has no CLI, WinUI, JavaScript, Agent, network, process, console, or environment
   dependency;
6. the CLI contains only parsing, cancellation wiring, invocation, result presentation, and
   process exit;
7. every asynchronous boundary receives the caller's cancellation token;
8. the smoke command emits the exact schema-versioned bytes from section 7;
9. core behavior is directly tested without process invocation;
10. all failures are nonzero and follow the precedence and output rules in section 9;
11. tests use only synthetic data and repository-safe project metadata;
12. committed lock files reproduce restore without package-version drift;
13. no original or copied save is read or modified;
14. the implementation candidate is committed and pushed; and
15. the independent release gate reports and persists `No findings`.

## 14. Stop conditions

Stop A1 and revise this plan before continuing if:

- implementation requires a fourth production or test project;
- any project path or dependency direction changes;
- the library requires a project-local package reference;
- the CLI requires any project-local package reference;
- any save, game-installation, private-workspace, or network access appears;
- a codec, scanner, graph model, semantic claim, writer, WinUI type, DI Host, or Agent abstraction
  is introduced;
- deterministic output requires a timestamp, random value, path, hash, locale, or environment
  input;
- cancellation cannot reach an asynchronous boundary;
- core behavior can be tested only by process invocation;
- a failure is reported as success or leaks exception, argument, path, or environment details;
- restore requires an unapproved package source, package-version change, or credential workaround;
- a test requires real game or user data; or
- an independent reviewer has any unresolved finding.

## 15. Expected outputs and exclusions

Repository-safe outputs:

- three project files and three lock files;
- minimal C# source for the library and CLI;
- focused C# tests;
- the updated `.copilot\README.md`;
- the exact-plan A1 review record; and
- the exact-candidate A1 release-gate record.

Private outputs: none.

Explicit exclusions:

- save discovery, copying, decoding, decompression, JsonEx traversal, and fingerprinting;
- definition-source scanning and semantic correlation;
- private artifact creation or migration;
- write operations, backups, recovery, or compatibility claims;
- WinUI, Windows App SDK, packaging, and deployment;
- telemetry, logging frameworks, configuration systems, DI hosting, and network services; and
- AI, ML, Agent Framework, Copilot SDK, or external Agent runtime integration.

## 16. Authority and change control

The project leader may approve a material scope or dependency change. Copilot may resolve
implementation details that preserve this plan's boundaries and measurable behavior.

Any change to project count, project paths, dependency direction, public library contract, command
name, output bytes, exit codes, privacy boundary, acceptance criteria, or stop conditions requires:

1. a plan revision;
2. a committed and pushed plan candidate;
3. another independent no-findings plan review; and
4. a new exact-plan review record before implementation resumes.

## 17. Plan review and execution handoff

Before implementation:

1. commit and push this plan plus its index entry;
2. record the plan candidate commit and tree;
3. have an independent subagent review the complete plan for speculative architecture, dependency
   leakage, testability, deterministic output, cancellation, and command behavior;
4. resolve every finding and repeat exact-candidate review until `No findings`;
5. commit `../reviews/atlas-v0-a1-plan-review.md` as the only change after the accepted plan
   candidate;
6. include in that record increment `A1 plan`, execution-ready outcome, plan commit and tree,
   governing documents, reviewed paths, reviewer identity and independence attestation, every
   iteration and disposition, final result, private-evidence absence, first-parent requirement, and
   changed-path restriction;
7. verify its first parent and that it changes only the plan-review record;
8. push the review record;
9. verify the review-record commit equals upstream; and
10. treat the pushed review record as repository authority to begin A1.

After step 10, an operational session todo may move from `pending` to `in_progress` without
changing tracked files. Session state is not execution authority and does not modify the reviewed
plan.

Another contributor resumes by checking out the shared branch and verifying the plan-review
record. If the three A1 projects do not exist, start with A1.1. Run each section 12 command only
after its referenced project and lock files exist. If projects exist, start at the first incomplete
sequence in section 11. Conversation history and session task state are not execution authority.
