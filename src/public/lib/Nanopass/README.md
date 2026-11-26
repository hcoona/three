# Hcoona.Nanopass

`Hcoona.Nanopass` brings the [Nanopass Compiler Framework](https://github.com/nanopass/nanopass-framework-scheme) (Sarkar, Waddell, and Dybvig, ICFP 2004; Keep and Dybvig, ICFP 2013) natively to **C# 15 and .NET 11**.

Rather than embedding a Lisp macro sub-language inside strings or runtime expression trees, `Hcoona.Nanopass` treats intermediate representations (IRs) as strongly typed C# 15 `union` and `sealed record` hierarchies synthesized by an incremental Roslyn source generator from declarative grammar definitions and deltas.

## Capabilities and Current Scope

### Implemented (`define-language` and `define-pass` Ecosystems)

- **Native C# 15 Type-First & Clause-Based IR Definitions**: Define base languages either directly with C# 15 `partial union` and `sealed record` declarations or with concise Nanopass S-expression production attributes (`[Terminal]`, `[NonTerminalDef]`).
- **Incremental Grammar Algebra**: Derive new IR stages via `[Extends(typeof(LBase))]`, `[Remove(...)]`, `[RemoveTerminal(...)]`, `[ExtendNonTerminal(...)]`, and `[PrunedLanguage(typeof(LSource))]` across arbitrarily deep inheritance chains within one compilation or across assemblies.
- **Value-Equatable AST Containers**: `Seq<T>` provides deep structural equality, hash codes, C# collection-expression construction (`[a, b, .. rest]`), and C# list-pattern slicing (`[var head, .. var tail]`).
- **S-Expression Tooling**: Generated `Parse`, `Unparse` (raw and `=>` pretty modes), `ToSExpression`, `DiffTo`, `Prune`, `CountNodes`, and language/nonterminal predicates (`IsLanguageTerm`, `Is<NonTerminal>`).
- **Automatic Pass Synthesis & Catamorphisms**: Declare transformations via `[Pass(typeof(LIn), typeof(LOut))]`, `[Processor]`, `[Rewrite]`, and `[Recur]` catamorphisms over scalars, `Maybe` (`?`), nested `Seq<T>`, and extra-return tuples, with parameter-name context forwarding, guarded clauses, `IsElse` fallbacks, `RawTerm` (`*`) endpoints, `PassDescriptor` introspection, `PassTrace`, and strongly typed `Pipeline` composition.
- **Compile-Time Diagnostics & IDE Code Fixes**: Static grammar validation (`NP0101`–`NP0105`), pass completeness/signature analysis (`NP1001`–`NP1003`), and `MissingRewriteRuleCodeFixProvider` (single-rule and BatchFixer `[Rewrite]` stub generation).

### Planned Stacked Advancements ([#1779](https://github.com/hcoona/three/issues/1779))

- **Reference Multi-Pass Compilers**: Full C# 15 ports of the upstream CoreScheme-to-JavaScript compiler and 20-pass Scheme compiler test suites.

### Explicit Non-Goals

- **No Lisp Hygienic Macro or Quasiquote Emulation**: `Hcoona.Nanopass` does not implement Racket/Scheme hygienic macros, `quasiquote`/`unquote` AST templates, or `with-output-language`/`in-context` lexical scope capture. Output IR nodes are constructed with native C# 15 constructors and union conversions.
- **No Unimplemented Upstream TODOs**: Experimental ideas listed only as upstream `TODO` notes (such as automatic multi-pass fusion or heuristic template reshaping) are out of scope.

## Quick Start

### Type-First C# 15 Language Evolution

```csharp
using Hcoona.Nanopass;

[Language]
public static partial class L0
{
    [NonTerminal("e")]
    [Entry]
    public partial union Expr(Int, Add, Sub, Neg);

    public sealed record Int(int Value);
    public sealed record Add(Expr Left, Expr Right);
    public sealed record Sub(Expr Left, Expr Right);
    public sealed record Neg(Expr Operand);
}

[Language]
[Extends(typeof(L0))]
[Remove(typeof(L0.Neg))]
public static partial class L1;
```

`L1.Expr` is generated as a distinct C# 15 `union` over `L1.Int`, `L1.Add`, and `L1.Sub`: constructing or nesting an `L0.Neg` inside an `L1.Expr` is a compile-time type error, and `switch` expressions over `L1.Expr` are exhaustively checked without a `Neg` arm. To add new production records onto an inherited nonterminal in a derived type-first language, declare a `[NonTerminal] public readonly partial struct` container (for example `[NonTerminal] public readonly partial struct Expr { public sealed record Mul(Expr Left, Expr Right); }`) rather than a partial `union` with an incomplete case list.

### Defining a Nanopass Transformation with Catamorphism

```csharp
[Pass(typeof(L0), typeof(L1))]
public partial class LowerNegation
{
    [Rewrite(typeof(L0.Neg))]
    private static L1.Expr LowerNeg([Recur] L1.Expr operand) =>
        new L1.Sub(new L1.Int(0), operand);

    public static L1.Expr Demo()
    {
        var pipeline = Pipeline.Start<L0.Expr>().Then(new LowerNegation());
        return pipeline.Run(new L0.Add(new L0.Neg(new L0.Int(3)), new L0.Int(5)));
        // Returns: Add(Sub(Int(0), Int(3)), Int(5))
    }
}
```

Only the `L0.Neg` rule is written by hand; `Int`, `Add`, and `Sub` are synthesized automatically with recursive traversal. Removing `LowerNeg` reports compile-time error `NP1001` and offers an IDE Code Fix to generate the missing `[Rewrite]` stub.

### Nanopass Clause Syntax with Parser and Pretty Unparser

```csharp
using Hcoona.Nanopass;

// Round-trip S-expressions and inspect the grammar:
LLet.Expr term = LLet.Parse("(let ([a (num 1)] [b (num 2)]) a b)");
string pretty = LLet.UnparseToString(term);          // (let ((a 1) (b 2)) a b)
string raw = LLet.UnparseToString(term, raw: true);  // (let ((a (num 1)) (b (num 2))) a b)
int nodes = LLet.CountNodes(term);                   // 5

[Language]
[Terminal(typeof(Symbol), "x")]
[Terminal(typeof(long), "n")]
[NonTerminalDef(
    "Expr",
    ["e", "body"],
    "x",
    "(num n) => n",
    "(let ([x* e*] ...) body* ... body)")]
public static partial class LLet;
```

## Releasing

Workflow Delivery releases the `Hcoona.Nanopass` package through the manually dispatched
`.github/workflows/workflow-delivery-v3-nuget-release.yml` caller with `unit: nanopass`,
as declared by `workflow-delivery.release-unit.yml`. Use `registry: github-packages` for
GitHub Packages (Buddy) from any branch, or `registry: nugetorg` for nuget.org (Official)
from `main` only. Preparation packs `lib/net11.0/Hcoona.Nanopass.dll` together with
`analyzers/dotnet/cs/Hcoona.Nanopass.Generators.dll`, verifies the native archive and
NBGV assembly metadata, and exercises both the packed source generator and runtime in an
isolated consumer project before any publication.

## Project Records

- [Architecture and Design Invariants](https://github.com/hcoona/three/blob/main/src/public/lib/Nanopass/docs/architecture.md)
- [Validation and Testing Strategy](https://github.com/hcoona/three/blob/main/src/public/lib/Nanopass/docs/validation.md)
- [Release Notes](https://github.com/hcoona/three/blob/main/src/public/lib/Nanopass/ReleaseNotes.md)
- [Third-Party Notices (MIT Attribution)](https://github.com/hcoona/three/blob/main/src/public/lib/Nanopass/THIRD-PARTY-NOTICES.txt)
