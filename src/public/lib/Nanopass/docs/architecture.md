# Architecture and Design Invariants

This record owns the internal structure, dependency boundaries, and compile-time invariants of `Hcoona.Nanopass`.

## Design Principles

1. **Grammar Metamodel as Single Source of Truth**:
   Both native C# 15 type-first declarations (`partial union` + `sealed record`) and S-expression clause attributes (`[Terminal]`, `[NonTerminalDef]`, `[ExtendNonTerminal]`) normalize into one immutable, value-equatable `LanguageModel`. Neither Roslyn symbols nor emitted C# AST classes serve as the sole authority.
2. **Deep Modules Over Boilerplate**:
   Users declare only language grammars and incremental deltas (`[Extends]`, `[Remove]`, `[PrunedLanguage]`). AST record synthesis, structural equality, S-expression parsing, raw and pretty unparsing, node counting, and grammar diffing/pruning remain encapsulated behind generated static members and runtime descriptors.
3. **Reasonable Platform Trust and Fail-Closed Boundaries**:
   Generated IR types rely directly on C# 15 unions (`System.Runtime.CompilerServices.UnionAttribute`, `IUnion`, and `IUnionMembers`), closed hierarchies (`closed record SExpr`), and Roslyn `5.12.0` exhaustiveness checking. Malformed grammar definitions fail closed at compile time (`NP0101`–`NP0105`), and malformed runtime S-expressions or cross-language unparse inputs fail closed with `NanopassParseException` or `NanopassException`.

## Component View

```mermaid
flowchart LR
    subgraph Consumer["Consumer Compiler Assembly (net11.0 / C# 15)"]
        Decl["[Language] / [Extends] & [Pass] Declarations"]
        GenIR["Generated C# 15 Unions, Records, Passes & Descriptors"]
    end

    subgraph Generator["Hcoona.Nanopass.Generators (netstandard2.0)"]
        Reader["LanguageInputReader & PassInputReader"]
        Model["Immutable Equatable LanguageModel & PassInput"]
        Resolver["LanguageResolver & PassResolver"]
        Analyzer["NanopassPassAnalyzer (NP10xx) & MissingRewriteRuleCodeFixProvider"]
        Emitter["LanguageEmitter & PassEmitter"]
    end

    subgraph Runtime["Hcoona.Nanopass Runtime (net11.0)"]
        Primitives["Seq&lt;T&gt;, Symbol, SExpr Codec"]
        Services["LanguageDescriptor, PassDescriptor, Pipeline & PassTrace"]
    end

    Decl --> Reader --> Resolver --> Model
    Model --> Emitter --> GenIR
    Decl --> Analyzer
    GenIR --> Primitives
    GenIR --> Services
```

## Key Technical Invariants

### Single-Pass Roslyn Resolution and Cross-Assembly Metadata

Roslyn `IIncrementalGenerator` implementations execute in a single pass and do not observe C# types emitted by generators within the same compilation. If `L1` extends `L0` and `L2` extends `L1` in the same project, `L2` cannot inspect Roslyn `INamedTypeSymbol` members synthesized for `L1`.

`Hcoona.Nanopass.Generators` solves both intra-compilation and cross-assembly inheritance uniformly:

- **Intra-compilation**: `LanguagePipeline` collects all `[Language]` syntax inputs in the compilation, topologically sorts the `[Extends]` / `[PrunedLanguage]` graph, detects cycles (`NP0102`), and resolves derived `LanguageModel`s directly in memory.
- **Cross-assembly**: `LanguageEmitter` annotates every generated language class with `[LanguageMetadata("...")]` carrying lossless canonical metadata encoded by `LanguageMetadataCodec`. When `[Extends(typeof(ExternalLang))]` targets a compiled assembly, `LanguageInputReader` reconstructs `ExternalLang`'s `LanguageModel` from its `[LanguageMetadata]` attribute.
- **Incremental Caching**: Every pipeline intermediate uses `EquatableArray<T>` and `LocationInfo` value types; no `ISymbol`, `SyntaxNode`, or `Compilation` instance is retained across incremental steps.

### C# 15 Union Lowering and `CS0542` Avoidance

- For generated nonterminals whose identifier is not `Value`, `LanguageEmitter` emits native C# 15 `public partial union <NonTerminal>(<Case1>, <Case2>, ...)` declarations augmented with `IEquatable<T>`, `==`, and `!=`. Because C# 15 allows only one `partial union` declaration to specify a case parameter list (`CS8863`), user-authored `partial union` declarations must enumerate all resolved cases of that nonterminal; derived or hybrid nonterminals that combine inherited/clause cases with newly added nested records use `[NonTerminal] public readonly partial struct <NonTerminal>` (reporting `NP0105` otherwise).
- In C# 15, a `union` declaration synthesizes an instance property named `Value`, which causes compiler error `CS0542` (`member names cannot be the same as their enclosing type`) when a grammar defines a nonterminal literally named `Value` (a standard nonterminal name in Nanopass compilers). For `Value` nonterminals (and user-declared `partial struct` nonterminals), `LanguageEmitter` emits C# 15's custom union provider pattern (`[Union] public readonly partial struct Value : Value.IUnionMembers`) with explicit `object? IUnionMembers.Value`, preserving implicit union conversions and exhaustive `switch` checking without member-name collision; declaring `partial union Value(...)` directly reports `NP0105`.
- Case creation and `TryGetValue` members are deduplicated by CLR case type so multiple standalone terminals backed by the same CLR representation (such as `Symbol`-typed `variable` and `primitive` terminals distinguished by terminal predicates) coexist in one union.
- Generated top-level production record names are disambiguated against reserved static language member names (`Parse`, `Unparse`, `Prune`, `Descriptor`, `CountNodes`, `ToSExpression`, `DiffTo`, `IsLanguageTerm`), and productions shared by name across multiple nonterminals in one language nest under their owning nonterminal.

### Structural Value Equality and S-Expression Dispatch

- Standard .NET arrays and `ImmutableArray<T>` use reference or handle equality, which breaks structural equality on `record` AST nodes containing variadic (`...`) fields. `Seq<T>` wraps `ImmutableArray<T>` with element-wise `EqualityComparer<T>.Default` equality, hash aggregation at arbitrary nesting depth (`Seq<Seq<Expr?>>`), and state-passing `Select<TState, TResult>` projection, while supporting C# collection expressions and list patterns.
- `SExpr` is a C# 15 `closed record` hierarchy (`Nil`, `Bool`, `Int`, `Float`, `Char`, `Str`, `Sym`, `Pair`, `Vector`, `Boxed`). `SExpr.FromValue` canonicalizes CLR primitives into their dedicated `SExpr` cases and reserves `Boxed` strictly for opaque CLR terminal values during `raw: true` unparsing; text formatting fails closed on `Boxed`.
- Matching upstream Nanopass `parser.rkt`, nonterminal parsing dispatches in four ordered phases: (1) non-implicit nonterminal inclusions, (2) list productions (keyword-tagged before implicit, matching `SExpr.Pair` and empty-sequence `SExpr.Nil` patterns as well as dotted `(s ... . t)` and `(tag . (maybe e))` tails), (3) standalone terminal predicates when and only when the input datum is not `SExpr.Pair`, and (4) implicit nonterminal inclusions. Parsing memoizes `(SExpr, nonterminal)` results with per-datum cycle isolation to prevent exponential backtracking on shared-prefix grammars, while `IsLanguageTerm` and `Is<NonTerminal>` recursively validate record fields and terminal predicates with DAG memoization over `FrozenDictionary` runtime dispatch tables.

## Compile-Time Diagnostic Codes (`define-language`)

| Code     | Condition                                                                                         |
| -------- | ------------------------------------------------------------------------------------------------- |
| `NP0101` | Unrecognized base or source language in `[Extends]` or `[PrunedLanguage]`.                        |
| `NP0102` | Cyclic `[Extends]` chain, duplicate `[Extends]` / `[Entry]`, or unknown entry nonterminal.        |
| `NP0103` | Unrecognized terminal, nonterminal, or production in a subtraction (`-` / `[Remove]`).            |
| `NP0104` | Duplicate terminal or duplicate production within a base nonterminal or extension addition (`+`). |
| `NP0105` | Malformed terminal/production pattern syntax or unrecognized metavariable reference.              |

### Pass Synthesis, Catamorphisms, and Subtype Inclusion

- **Incremental Value-Equatable Pass Pipeline**: `PassInputReader` extracts a pure, value-equatable `PassInput` per `[Pass]` declaration (including decoded `[LanguageMetadata]` for external language references) without combining with `CompilationProvider` or retaining Roslyn symbols/syntax nodes, so unrelated edits and edits to sibling passes leave `PassInputs` cached. `NanopassPassAnalyzer` evaluates user declarations against the pre-generation compilation with compilation-cached `LanguageModel` resolution so generator and analyzer observe identical symbol state.
- **Structural Synthesis and `build-subtype-call` Parity**: For each processor `(From -> To)`, `PassResolver` synthesizes boilerplate clauses using named record constructor arguments when a compatible production exists in `To` (or transitively included output nonterminals, wrapping union conversions along the inclusion path) and terminal identities match (`left.Target == right.Target` or an explicit terminal processor applies). Standalone input nonterminal inclusions and `[Recur]` sites search `To` and its transitively included nonterminals/terminals by shortest inclusion path and NRT-safe structural extra-return variance compatibility (bounded at Roslyn's 50-layer variance depth limit), while ordinary C# type checking of user `[Rewrite]` extra-return expressions and `DefaultExtraReturns` values is delegated directly to `csc`.
- **Catamorphisms, Guards, and Parameter-Name Matching**: Guard methods (`Guard = nameof(...)`) execute on the matched input node and matched context parameters _before_ evaluating `[Recur]` catamorphisms over scalars, `Maybe` (`?`), nested `Seq<T>`, and extra-return tuples. Extra processor parameters bind by parameter name (`Ordinal`) from the caller's scope (plus production fields when `[Recur(Arguments = ...)]` is specified), falling back to C# optional parameter defaults.
- **First-Class Pass Introspection and IDE Tooling**: Generated passes implement `INanopassPass` and `INanopassPassMetadata` (`PassDescriptor` exposing `InputLanguage`, `OutputLanguage`, `ParseInput`, `UnparseOutput`, and `Processors`), and implement `IPass<TInput, TOutput>` whenever a single-argument `Run(TInput)` entry point is synthesized or unambiguously adaptable; `PassTrace` coordinates one-shot stage handoff so `Pipeline` execution of a `Trace = true` pass emits a single pass-level `Enter`/`Exit` pair alongside processor-level events. `NanopassPassAnalyzer` reports `NP1001`–`NP1003` while `NanopassGenerator` emits compilable placeholder stubs so IDE code fixes operate without cascading C# compiler errors; `MissingRewriteRuleCodeFixProvider` generates single-rule and BatchFixer `[Rewrite]` method stubs.

## Compile-Time Diagnostic Codes (`define-pass`)

| Code     | Condition                                                                                                                    |
| -------- | ---------------------------------------------------------------------------------------------------------------------------- |
| `NP1001` | Missing transformation rule when no compatible production exists in the target nonterminal (fixable via IDE Code Fix).       |
| `NP1002` | No compatible processor exists and automatic processor synthesis is disallowed or impossible (including terminal subterms).  |
| `NP1003` | Invalid `[Pass]`, `[Processor]`, `[Rewrite]`, `[Recur]`, or `Guard` declaration, unknown language/nonterminal, or ambiguity. |
