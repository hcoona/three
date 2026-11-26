# Release Notes

## Unreleased (0.1.0)

- Introduce `Hcoona.Nanopass` for .NET 11 and C# 15 with `Seq<T>` structural-equality sequences, `Symbol`, and the closed-hierarchy `SExpr` reader and formatter.
- Add incremental Roslyn language generation (`[Language]`, `[Extends]`, `[PrunedLanguage]`, `[Entry]`, `[Terminal]`, `[RemoveTerminal]`, `[NonTerminal]`, `[NonTerminalDef]`, `[ExtendNonTerminal]`, `[Remove]`, `[Syntax]`, `[Pretty]`) emitting C# 15 `union` nonterminals, `sealed record` productions, S-expression parsers, raw and pretty unparsers, grammar diffing/pruning, node counters, cross-assembly `[LanguageMetadata]`, and `NP0101`–`NP0105` compile-time diagnostics.
- Add incremental `define-pass` synthesis (`[Pass]`, `[Processor]`, `[Rewrite]`, `[Recur]`, `IPass<TInput, TOutput>`, `Pipeline`, `PassDescriptor` introspection, `PassTrace`, and `RawTerm` endpoints), `NanopassPassAnalyzer` (`NP1001`–`NP1003`), and `MissingRewriteRuleCodeFixProvider` (single and batch Fix All generation of missing `[Rewrite]` stubs).
- Add stateful zero-closure `Seq<T>.Select<TState, TResult>` projection, memoized S-expression grammar parsing and recursive DAG term validation, compilation-cached pass analysis, and end-to-end verification via C# 15 ports of the CoreScheme-to-JavaScript and 20-pass Scheme reference compilers (`alltests.rkt`).
