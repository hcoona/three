# Validation and Testing Strategy

This record defines how `Hcoona.Nanopass` behavior and compile-time guarantees are verified in `tests/public/lib/Nanopass.Tests/`.

## Test Layering Principles

Following the repository's [shared engineering principles](../../../../../docs/engineering/engineering-principles.md#testing), tests are organized by responsibility rather than duplicating every case at every layer:

1. **Core Algorithmic Unit Tests**:
    - `SeqTests.cs`: Element-wise structural equality and hash invariants across nested `Seq<Seq<T>>`, nullable elements, `default(Seq<T>)` vs `Seq<T>.Empty`, collection expressions, and C# list-pattern slicing.
    - `SExprTests.cs`: Equivalence classes and boundary conditions for the Scheme/Racket S-expression codec—proper and dotted improper lists (`(a b . c)` and `(a b . ())` normalization), quote reader macros, bracket matching, line/block/datum comments, Racket pipe and backslash symbol escapes, character literals (`#\space`, `#\newline`, `#\(`), fail-closed rejection of unsupported numeric tokens (`1/2`, `1f3`), and canonical `SExpr.FromValue` / `Boxed` invariants.
    - `LanguageServicesTests.cs`: Pure descriptor operations for `ToSExpression`, `Diff`, and entry-reachable fixed-point `Prune`.
2. **Generator, Analyzer, and CodeFix Contract Tests**:
    - `LanguageGeneratorTests.cs`: Drives `NanopassGenerator` through `CSharpGeneratorDriver` to verify `NP0101`–`NP0105` diagnostics (covering upstream error regressions including unrecognized base languages, invalid terminal/nonterminal clauses, duplicate base and extended productions, and cross-language unparse rejection) as well as cross-assembly `[LanguageMetadata]` round-tripping across compiled `MetadataReference` boundaries and C# 15 union emission.
    - `PassAnalyzerTests.cs`: Verifies `NanopassPassAnalyzer` (`NP1001`–`NP1003`), incremental generator step caching across unrelated and method-body edits, single-rule and BatchFixer `MissingRewriteRuleCodeFixProvider` stub generation, and cross-assembly `[Pass]` compilation.
3. **End-to-End Language and Pass Scenario Suites**:
    - `NativeLanguageTests.cs`, `ClauseLanguageTests.cs`, & `PrettyLanguageTests.cs`: C# 15 type-first and clause-based `[Language]` declarations, `[Extends]` + `[Remove]` type safety, S-expression parsing, raw and pretty unparsing, node counting, and `Parse(Unparse(ir))` round-trip invariants.
    - `PassRuntimeTests.cs`, `PassSynthesisTests.cs`, `CatamorphismTests.cs`, `StructuralPassTests.cs`, `ProcessorArgumentTests.cs`, & `PassParserUnparserTests.cs`: End-to-end `define-pass` synthesis, `[Recur]` catamorphisms, guarded/else rewrites, subtype inclusion flattening/splitting/wrapping, `P1`–`P12` parameter-name forwarding and terminal processors, `PassDescriptor` introspection (`ski->lc`), `PassTrace`, and `Pipeline` composition.
4. **Reference Multi-Pass Compiler Suites (`Compilers/`)**:
    - `Compilers/CoreScheme/`: Full 4-pass CoreScheme-to-JavaScript compiler (`LCS`, `LACS`, `ParseCoreScheme`, `AlphaRenameCoreScheme`, `ANormalizeCoreScheme`, `GenerateJavaScript`) verified via independent in-process `LCS` and `LACS` evaluators (including Y-combinator `(Fact 5) == 120`) and emitted JavaScript invariants.
    - `Compilers/Scheme/`: Full 20-language (`LP` and `L0`–`L18`) canonical Nanopass Scheme compiler (`RenameVarsVerifyLegal` and the 19 lowering passes `RemoveImplicitBegin` through `ExplicitLabels` verified across all 194 upstream `alltests.rkt` programs—`164` `main-tests` and `30` `final-tests`—matching upstream `compiler-test.rkt`, plus dedicated compilation and diagnostic tests for `LP.Parse` + `VerifyScheme`, `L0`–`L18` `Parse(Unparse(ir))` round-trip checks, cyclic pair/vector equivalence, and trampolined tail recursion using independent in-process source and `L18` target-machine evaluators).

## Upstream Test Matrix and Scope Boundary

| Upstream Suite (`nanopass-framework-racket` / `nanopass-framework-scheme`)     | Status in `Hcoona.Nanopass.Tests`                                                                     |
| ------------------------------------------------------------------------------ | ----------------------------------------------------------------------------------------------------- |
| `unit-tests` (`LUNPARSE` raw & pretty, `LBool`, `LBoolLambda`)                 | Covered in `PrettyLanguageTests.cs` and `ClauseLanguageTests.cs`                                      |
| `maybe-tests`, `maybe-dots-tests`, `maybe-unparse-tests`                       | Covered in `ClauseLanguageTests.cs` and `StructuralPassTests.cs`                                      |
| `language-dot-support` (`LPairs`, `LList`, `Lx` Issue #7)                      | Covered in `ClauseLanguageTests.cs` and `StructuralPassTests.cs`                                      |
| `empty-lists`, `LMULTI`, `L-list-tagged`, `Leach-any` (`bb47b56`)              | Covered in `ClauseLanguageTests.cs` and `StructuralPassTests.cs`                                      |
| `language-node-counter` (`Lnodecount` Issue #42)                               | Covered in `ClauseLanguageTests.cs`                                                                   |
| `ensure-correct-identifiers` (`LVAR` `break-variable`)                         | Covered in `StructuralPassTests.cs`                                                                   |
| `argument-name-matching` (`P1`–`P12` parameter matching & terminal processors) | Covered in `ProcessorArgumentTests.cs`                                                                |
| `pass-parser-unparser` (`Lski`, `Llc`, `ski->lc`, `ski-in`, `lc-out`)          | Covered in `PassParserUnparserTests.cs`                                                               |
| `error-messages` (`define-language` & `define-pass` + `ea23996`, #12/#27/#40)  | Covered in `LanguageGeneratorTests.cs` and `PassAnalyzerTests.cs`                                     |
| `coresheme-to-javascript-compiler.rkt` & 20-pass `compiler.rkt`/`alltests.rkt` | Covered in `Compilers/CoreScheme/` and `Compilers/Scheme/` (all 194 `alltests.rkt` programs verified) |
| `with-output-language-tests.rkt` (hygienic macro `quasiquote` capture)         | Excluded by project scope ([#1779](https://github.com/hcoona/three/issues/1779))                      |

## Execution Commands

Run targeted validation from the repository root (or owning worktree):

```bash
mise exec -- dotnet build src/public/lib/Nanopass/Hcoona.Nanopass.csproj -c Release
mise exec -- dotnet test --project tests/public/lib/Nanopass.Tests/Hcoona.Nanopass.Tests.csproj
```

All tests execute strictly in-process on `.NET 11` without spawning external compilers, Node.js, or shell processes.
