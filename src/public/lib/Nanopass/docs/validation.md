# Validation and Testing Strategy

This record defines how `Hcoona.Nanopass` behavior and compile-time guarantees are verified in `tests/public/lib/Nanopass.Tests/`.

## Test Layering Principles

Following the repository's [shared engineering principles](../../../../../docs/engineering/engineering-principles.md#testing), tests are organized by responsibility rather than duplicating every case at every layer:

1. **Core Algorithmic Unit Tests**:
    - `SeqTests.cs`: Element-wise structural equality and hash invariants across nested `Seq<Seq<T>>`, nullable elements, `default(Seq<T>)` vs `Seq<T>.Empty`, collection expressions, and C# list-pattern slicing.
    - `SExprTests.cs`: Equivalence classes and boundary conditions for the Scheme/Racket S-expression codec—proper and dotted improper lists (`(a b . c)` and `(a b . ())` normalization), quote reader macros, bracket matching, line/block/datum comments, Racket pipe and backslash symbol escapes, character literals (`#\space`, `#\newline`, `#\(`), fail-closed rejection of unsupported numeric tokens (`1/2`, `1f3`), and canonical `SExpr.FromValue` / `Boxed` invariants.
    - `LanguageServicesTests.cs`: Pure descriptor operations for `ToSExpression`, `Diff`, and entry-reachable fixed-point `Prune`.
2. **Generator and Diagnostic Contract Tests**:
    - `LanguageGeneratorTests.cs`: Drives `NanopassGenerator` through `CSharpGeneratorDriver` to verify `NP0101`–`NP0105` diagnostics (covering upstream error regressions including unrecognized base languages, invalid terminal/nonterminal clauses, duplicate base and extended productions, and cross-language unparse rejection) as well as cross-assembly `[LanguageMetadata]` round-tripping across compiled `MetadataReference` boundaries and C# 15 union emission.
3. **End-to-End Language Scenario and Round-Trip Suites**:
    - `NativeLanguageTests.cs`: C# 15 type-first `[Language]` declarations, `[Extends]` + `[Remove]` type safety, nested `Seq<Seq<Expr?>>` fields, and custom pretty handlers.
    - `ClauseLanguageTests.cs` & `PrettyLanguageTests.cs`: Behavioral ports of upstream `define-language`, `define-parser`, `unparse-L`, and `define-language-node-counter` suites, plus `Parse(Unparse(ir))` round-trip invariants.

## Upstream Test Matrix and Scope Boundary

| Upstream Suite (`nanopass-framework-racket` / `nanopass-framework-scheme`)     | Status in `Hcoona.Nanopass.Tests`                                                                             |
| ------------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------- |
| `unit-tests` (`LUNPARSE` raw & pretty, `LBool`, `LBoolLambda`)                 | Covered in `PrettyLanguageTests.cs` and `ClauseLanguageTests.cs`                                              |
| `maybe-tests`, `maybe-dots-tests`, `maybe-unparse-tests`                       | Covered in `ClauseLanguageTests.cs`                                                                           |
| `language-dot-support` (`LPairs`, `LList`, `Lx` Issue #7)                      | Covered in `ClauseLanguageTests.cs`                                                                           |
| `empty-lists`, `LMULTI`, `L-list-tagged`, `Leach-any` (`bb47b56`)              | Covered in `ClauseLanguageTests.cs`                                                                           |
| `language-node-counter` (`Lnodecount` Issue #42)                               | Covered in `ClauseLanguageTests.cs`                                                                           |
| `error-messages` (`define-language` / `diff` / `prune` + `ea23996`)            | Covered in `LanguageGeneratorTests.cs`                                                                        |
| `define-pass`, `argument-name-matching`, `pass-parser-unparser` (`ski->lc`)    | Scheduled for Stacked Package 2 (`define-pass` synthesis, `PassDescriptor` introspection, `NP10xx` & CodeFix) |
| `coresheme-to-javascript-compiler.rkt` & 20-pass `compiler.rkt`/`alltests.rkt` | Scheduled for Stacked Package 3 (in-process semantic equivalence verification without external child tools)   |
| `with-output-language-tests.rkt` (hygienic macro `quasiquote` capture)         | Excluded by project scope ([#1779](https://github.com/hcoona/three/issues/1779))                              |

## Execution Commands

Run targeted validation from the repository root (or owning worktree):

```bash
mise exec -- dotnet build src/public/lib/Nanopass/Hcoona.Nanopass.csproj -c Release
mise exec -- dotnet test --project tests/public/lib/Nanopass.Tests/Hcoona.Nanopass.Tests.csproj
```

All tests execute strictly in-process on `.NET 11` without spawning external compilers, Node.js, or shell processes.
