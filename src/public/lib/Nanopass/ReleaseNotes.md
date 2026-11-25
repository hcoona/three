# Release Notes

## Unreleased (0.1.0)

- Introduce `Hcoona.Nanopass` for .NET 11 and C# 15 with `Seq<T>` structural-equality sequences, `Symbol`, and the closed-hierarchy `SExpr` reader and formatter.
- Add incremental Roslyn language generation (`[Language]`, `[Extends]`, `[PrunedLanguage]`, `[Entry]`, `[Terminal]`, `[RemoveTerminal]`, `[NonTerminal]`, `[NonTerminalDef]`, `[ExtendNonTerminal]`, `[Remove]`, `[Syntax]`, `[Pretty]`) emitting C# 15 `union` nonterminals, `sealed record` productions, S-expression parsers, raw and pretty unparsers, grammar diffing/pruning, node counters, cross-assembly `[LanguageMetadata]`, and `NP0101`–`NP0105` compile-time diagnostics.
