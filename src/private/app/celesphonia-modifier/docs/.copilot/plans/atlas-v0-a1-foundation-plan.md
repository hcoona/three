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
- `System.CommandLine` version `2.0.9`;
- `xunit.v3.mtp-v2` and Microsoft.Testing.Platform; and
- automatic project discovery through root `dirs.proj`.

No package version, traversal file, solution file, or root build property should change in A1.

## 3. Exact project layout

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

The root traversal already discovers all three projects. A1 does not add a nested solution or
project traversal file.

## 4. Dependency direction

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
- has no `PackageReference`;
- uses only .NET Base Class Library APIs;
- does not depend on `System.CommandLine`;
- does not reference the CLI, WinUI, Windows App SDK, JavaScript, an Agent SDK, or a network
  service; and
- does not contain process, console, environment, or installed-game access.

The CLI:

- references only the Atlas library as a project;
- references only `System.CommandLine` as a package;
- binds command-line input, maps results to output channels and exit codes, and handles Ctrl+C;
- contains no save discovery, codec, graph, schema interpretation, or write semantics; and
- exposes internals only to the test assembly through `InternalsVisibleTo`.

The test project:

- references the library and CLI projects;
- uses the repository's xUnit v3 and Microsoft.Testing.Platform package pattern; and
- uses only synthetic in-memory inputs.

## 5. Project properties

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
```

Root analysis and warnings-as-errors settings remain inherited. A1 does not suppress analyzers,
lower warning severity, enable unsafe code, or relax nullable checks.

## 6. Empty-survey contract

A1 adds the strict repository-safe schema:

`docs\.copilot\schemas\atlas-v0\empty-survey.schema.json`

Its schema identifier is:

`urn:hcoona:three:celesphonia-modifier:atlas-v0:empty-survey:v1`

Its document version is:

`atlas-empty-survey/v1`

The schema permits exactly two properties in this order:

1. `schemaVersion`, whose value is the document version; and
2. `observations`, which is an empty array.

It rejects additional properties and a nonempty `observations` array. This is an A1 smoke schema,
not the future canonical Atlas observation schema. Later increments must introduce a separately
versioned schema rather than silently broadening this one.

The library writes exactly these bytes:

```text
{"schemaVersion":"atlas-empty-survey/v1","observations":[]}\n
```

The `\n` notation above means one final byte `0A`, not two characters. The byte contract is:

- UTF-8 without BOM;
- one compact JSON object;
- property order exactly as shown;
- no spaces or indentation;
- one LF after the object;
- no CR;
- no timestamp, machine name, path, hash, random identifier, or environment-derived value; and
- no culture-sensitive formatting.

Repeated writes in the same or separate processes must be byte-identical.

## 7. Minimal library contract

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

- `destination` is required and must be writable;
- cancellation is checked before any write;
- the token is passed to the asynchronous stream write;
- cancellation and stream failures propagate to the caller;
- the method writes the exact bytes from section 6;
- the method does not flush, close, or dispose the caller-owned stream; and
- the method performs no file, console, environment, network, or private-workspace access.

No interface, dependency-injection registration, repository abstraction, options type, result
hierarchy, or general serializer abstraction is introduced in A1.

## 8. CLI contract

The executable name is `celesphonia-atlas`. A1 supports one operation:

```text
celesphonia-atlas empty-survey
```

It also supports root and command help through `-h` and `--help`. `empty-survey` accepts no
options, operands, paths, or private values.

The CLI application has a process-free internal entry point that accepts:

- an argument array;
- caller-owned standard-output and standard-error streams; and
- a cancellation token.

`Program.Main` owns only console stream selection, Ctrl+C registration, and synchronous process
exit. The internal application performs parsing and invokes `EmptyAtlasSurvey.WriteAsync`.

### 8.1 Output channels

- Successful `empty-survey`: stdout receives the exact bytes from section 6; stderr is empty.
- Help: stdout receives stable UTF-8 help text ending in LF; stderr is empty.
- Invalid arguments: stdout is empty; stderr receives a stable safe diagnostic ending in LF.
- Cancellation: stdout is empty when canceled before writing; stderr receives
  `Operation canceled.` plus LF.
- I/O failure: stdout makes no success claim; stderr receives `I/O failure.` plus LF.
- Unexpected failure: stdout makes no success claim; stderr receives `Unexpected failure.` plus
  LF.

Diagnostics never include exception messages, stack traces, argument values, environment values,
or paths. Unknown option diagnostics may include only a recognized-safe option token beginning
with `-`; otherwise the CLI uses a generic invalid-arguments message.

### 8.2 Exit codes

| Code | Meaning                                  |
| ---: | ---------------------------------------- |
|    0 | Command or help completed successfully   |
|    1 | Unexpected internal failure              |
|    2 | Command-line usage or validation failure |
|    3 | Operation canceled                       |
|    4 | Standard-stream I/O failure              |

No failure path returns zero. Cancellation is not reported as an unexpected failure.

### 8.3 Cancellation

`Program.Main` registers a `Console.CancelKeyPress` handler, sets `Cancel = true`, and cancels one
owned `CancellationTokenSource`. The token flows through the internal CLI application into
`EmptyAtlasSurvey.WriteAsync` and its asynchronous stream write.

The handler is removed in `finally`. Cancellation is not swallowed, converted to success, or
replaced with `CancellationToken.None`.

A1 introduces no asynchronous file or scan boundary. Future boundaries must accept and pass the
same operation token before A1's cancellation criterion can remain satisfied.

## 9. Test plan

The test project adds focused tests in these groups.

### 9.1 Library tests

- `SchemaVersion` equals `atlas-empty-survey/v1`.
- One write matches the exact byte sequence in section 6.
- Repeated writes are byte-identical.
- Output is valid JSON with exactly the required properties.
- Output conforms to the committed empty-survey schema.
- A pre-canceled token throws `OperationCanceledException` before a write.
- A stream that cancels during `WriteAsync` receives the caller's token.
- A stream failure propagates.
- The writer leaves the destination open and does not flush it.

### 9.2 CLI application tests

- `empty-survey` returns 0, writes exact stdout bytes, and leaves stderr empty.
- Root and command help return 0 without invoking the library operation.
- Missing, unknown, extra, and option-bearing input returns 2.
- Parse failures write only safe stderr diagnostics and leave stdout empty.
- Pre-cancellation returns 3 and writes the cancellation diagnostic.
- An operation cancellation using the caller's token returns 3.
- Standard-output or standard-error I/O failure returns 4 where a diagnostic channel remains.
- An unexpected injected operation failure returns 1 without exception details.
- Output and error streams remain caller-owned.

### 9.3 Boundary tests

- The library project has no package or project reference.
- The CLI project references the library and `System.CommandLine`, and nothing else.
- The test project references both A1 production projects.
- Core empty-survey behavior is exercised directly without starting a process.
- No test reads the game installation, a live save directory, or `.private`.

Project-file boundary assertions inspect only repository-relative test fixture paths derived from
the test assembly location. They do not assume the current working directory.

## 10. Implementation sequence

### A1.1 Project scaffold

Create the three project files, project references, test package references, and namespaces.
Restore to generate lock files. Do not add behavior beyond compile-safe placeholders.

Acceptance:

- all three projects are discovered by root traversal;
- restore succeeds without package-version changes;
- dependency direction matches section 4; and
- the library compiles with no package or project reference.

### A1.2 Deterministic library operation

Add the empty-survey schema and `EmptyAtlasSurvey`.

Acceptance:

- exact-byte, JSON-shape, schema, ownership, failure, and cancellation tests pass; and
- the library remains free of CLI and environment dependencies.

### A1.3 Thin CLI

Add `empty-survey`, help, output-channel behavior, exit-code mapping, and Ctrl+C propagation.

Acceptance:

- direct CLI application tests pass;
- only `System.CommandLine` and the Atlas library are dependencies;
- no save semantics exist in the CLI; and
- no diagnostic discloses argument values or exception details.

### A1.4 Integrated validation

Run the targeted restore, build, test, and smoke commands from section 11.

Acceptance:

- every command succeeds from the repository root;
- the smoke command emits only the exact JSON line;
- no new warning appears; and
- no tracked file exists outside the declared A1 paths and directly related documentation.

### A1.5 Independent release gate

Commit and push the exact A1 implementation candidate. An independent subagent reviews the full
candidate against this plan, A0 privacy constraints, tests, and command output. Resolve every
finding and repeat until the exact result is `No findings`.

Persist `../reviews/atlas-v0-a1-release-gate.md` as the only change after the accepted candidate.
The record binds the candidate commit and tree, validation evidence, review iterations, and
changed-path restriction.

## 11. Validation commands

Run every .NET command from the repository root with the user-required wrapper and Windows paths.

Restore all A1 projects through the test project:

```powershell
$testRoot = "tests\private\app\celesphonia-modifier"
$testProject = Join-Path $testRoot `
  "Hcoona.CelesphoniaModifier.Atlas.Tests\Hcoona.CelesphoniaModifier.Atlas.Tests.csproj"
mise exec -- dotnet restore $testProject
```

Build the complete A1 graph:

```powershell
mise exec -- dotnet build $testProject --no-restore
```

Run targeted tests:

```powershell
mise exec -- dotnet test --project $testProject --no-restore
```

Run the smoke command after the build:

```powershell
$sourceRoot = "src\private\app\celesphonia-modifier"
$cliProject = Join-Path $sourceRoot `
  "Hcoona.CelesphoniaModifier.Atlas.Cli\Hcoona.CelesphoniaModifier.Atlas.Cli.csproj"
mise exec -- dotnet run --project $cliProject --no-build -- empty-survey
```

The smoke command must write exactly:

```json
{ "schemaVersion": "atlas-empty-survey/v1", "observations": [] }
```

with one final LF and no stderr output.

For clean-checkout acceptance, repeat restore, build, and test in a fresh worktree or CI checkout.
Do not delete or reset the current shared worktree to simulate cleanliness.

## 12. Increment acceptance criteria

A1 is accepted only when:

1. all outputs in section 3 and the empty-survey schema exist;
2. a clean checkout restores, builds, and runs the targeted tests;
3. all projects use `$(CurrentTargetFramework)`, nullable, and implicit-usings conventions;
4. the dependency graph exactly matches section 4;
5. the library has no CLI, WinUI, JavaScript, Agent, network, process, console, or environment
   dependency;
6. the CLI contains only parsing, cancellation wiring, invocation, result presentation, and
   process exit;
7. every asynchronous boundary receives the caller's cancellation token;
8. the smoke command emits the exact schema-versioned bytes from section 6;
9. core behavior is directly tested without process invocation;
10. all failures are nonzero and use the correct output channel;
11. tests use only synthetic repository-safe data;
12. committed lock files reproduce restore without package-version drift;
13. no original or copied save is read or modified;
14. the implementation candidate is committed and pushed; and
15. the independent release gate reports and persists `No findings`.

## 13. Stop conditions

Stop A1 and revise this plan before continuing if:

- implementation requires a fourth production or test project;
- any project path or dependency direction changes;
- the library requires a package reference;
- the CLI requires a package other than `System.CommandLine`;
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

## 14. Expected outputs and exclusions

Repository-safe outputs:

- three project files and three lock files;
- minimal C# source for the library and CLI;
- focused C# tests;
- `empty-survey.schema.json`;
- directly related `.copilot` documentation; and
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

## 15. Authority and change control

The project leader may approve a material scope or dependency change. Copilot may resolve
implementation details that preserve this plan's boundaries and measurable behavior.

Any change to project count, project paths, dependency direction, public library contract, command
name, output bytes, exit codes, privacy boundary, acceptance criteria, or stop conditions requires:

1. a plan revision;
2. a committed and pushed plan candidate;
3. another independent no-findings plan review; and
4. a new exact-plan review record before implementation resumes.

## 16. Plan review and execution handoff

Before implementation:

1. commit and push this plan plus its index entry;
2. record the plan candidate commit and tree;
3. have an independent subagent review the complete plan for speculative architecture, dependency
   leakage, testability, deterministic output, cancellation, and command behavior;
4. resolve every finding and repeat exact-candidate review until `No findings`;
5. commit `../reviews/atlas-v0-a1-plan-review.md` as the only change after the accepted plan
   candidate;
6. verify its first parent and changed-path restriction;
7. push the review record; and
8. move A1 from `pending` to `in_progress`.

Another contributor resumes by checking out the shared branch, verifying the plan-review record,
running the commands in section 11, and starting at the first incomplete implementation sequence
in section 10. Conversation history and session task state are not execution authority.
