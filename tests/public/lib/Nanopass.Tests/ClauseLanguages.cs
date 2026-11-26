namespace Hcoona.Nanopass.Tests;

[Language]
[Terminal(typeof(bool), "b")]
[NonTerminalDef("Expr", ["e"], "b")]
public static partial class BooleanLanguage;

[Language]
[Terminal(typeof(Symbol), "x")]
[NonTerminalDef("Expr", ["e"], "u => (from-u u)", "w => (from-w w)", "v => (dup v v)")]
[NonTerminalDef("U", ["u"], "x => (u-symbol x)")]
[NonTerminalDef("W", ["w"], "x => (w-symbol x)")]
[NonTerminalDef("V", ["v"], "w => (v-form w)")]
public static partial class TwoInclusionsLanguage;

[Language]
[Terminal(typeof(Symbol), "x", Predicate = nameof(IsIdentifier))]
[NonTerminalDef("Expr", ["e"], "v => (expr v)", "(pair e0 e1)")]
[NonTerminalDef("Value", ["v"], "a => (value a)", "(unit) => cooked")]
[NonTerminalDef("Atom", ["a"], "x => (id x)")]
public static partial class InclusionPrettyLanguage
{
    private static bool IsIdentifier(Symbol symbol) =>
        !string.Equals(symbol.Name, "reserved", StringComparison.Ordinal);
}

[Language]
[Terminal(typeof(bool), "b")]
[Terminal(typeof(long), "i", Name = "integer")]
[NonTerminalDef("Exp", ["e"], "(Int i)", "(Bool b)", "(Bar (maybe i) e)", "(Foo i (maybe e))")]
public static partial class MaybeLanguage;

[Language]
[Terminal(typeof(bool), "b")]
[Terminal(typeof(long), "i", Name = "integer")]
[NonTerminalDef(
    "Exp", ["e"], "(Int i)", "(Bool b)", "(Bar (maybe i) ... e)", "(Foo i (maybe e) ...)")]
public static partial class MaybeSequenceLanguage;

[Language]
[Terminal(typeof(Symbol), "x")]
[Terminal(typeof(long), "n", Name = "number")]
[NonTerminalDef("Expr", ["e"], "(foo x (maybe n))", "(bar (maybe e) x)", "(num n)", "(ref x)")]
public static partial class MaybeWithoutBooleanLanguage;

[Language]
[Terminal(typeof(Symbol), "x")]
[Terminal(typeof(long), "n", Name = "number")]
[NonTerminalDef(
    "Expr", ["e"], "(foo ([x (maybe n)] ...) e)", "(bar (maybe e) ... x)", "(num n)", "(ref x)")]
public static partial class MaybeParallelLanguage;

[Language]
[Terminal(typeof(Symbol), "x")]
[Terminal(typeof(SExpr.Nil), "n", Name = "null")]
[NonTerminalDef("Expr", ["e"], "x", "n", "(e0 . e1)")]
public static partial class PairsLanguage;

[Language]
[Terminal(typeof(Symbol), "x")]
[Terminal(typeof(SExpr.Nil), "n", Name = "null")]
[NonTerminalDef("Expr", ["e"], "x", "n", "(e0 ... . e1)")]
public static partial class DottedListLanguage;

[Language]
[Terminal(typeof(Symbol), "x")]
[Terminal(typeof(SExpr.Nil), "n", Name = "null")]
[NonTerminalDef("Expr", ["e"], "x", "n", "(e0 ... e1)")]
public static partial class PrefixListLanguage;

[Language]
[Terminal(typeof(Symbol), "x")]
[NonTerminalDef(
    "Expr", ["e"], "x", "(lambda (x* ... . x) e)", "(define (x x* ... . x1) e)", "(define x e)")]
public static partial class ImproperLambdaLanguage;

[Language]
[Terminal(typeof(Symbol), "s")]
[NonTerminalDef("Expr", ["e"], "s", "(e ...)")]
public static partial class EmptyListLanguage;

[Language]
[Terminal(typeof(Symbol), "s")]
[NonTerminalDef("Expr", ["e"], "s", "(e* ... . e)")]
public static partial class EmptyDottedListLanguage;

[Language]
[Terminal(typeof(Symbol), "x", Name = "var")]
[Terminal(typeof(Symbol), "pr", Name = "primitive")]
[Terminal(typeof(SExpr), "d", Name = "datum")]
[NonTerminalDef(
    "Expr",
    ["e"],
    "(var x)",
    "(primref pr)",
    "(quote d)",
    "(if e0 e1 e2)",
    "(begin e0 ... e1)",
    "(let ([x e] ...) e1)",
    "(letrec ([x le] ...) e)",
    "(app e0 e1 ...)")]
[NonTerminalDef("LambdaExpr", ["le"], "(lambda (x ...) e)", "(case-lambda cl ...)")]
[NonTerminalDef("CaseLambdaClause", ["cl"], "(clause (x ...) e)")]
public static partial class MultiLanguage;

[Language]
[Entry("Expr")]
[Terminal(typeof(Symbol), "x")]
[Terminal(typeof(SExpr), "s")]
[NonTerminalDef("Binding", ["b"], "[(x ...) e]")]
[NonTerminalDef("Expr", ["e"], "x", "(e1 e2)", "(let-values s (b ...) e0 e1 ...)")]
public static partial class TaggedListLanguage;

[Language]
[Terminal(typeof(Symbol), "x")]
[Terminal(typeof(long), "n")]
[NonTerminalDef("Prog", ["p"], "(prog s ...)")]
[NonTerminalDef("Stmt", ["s"], "e", "(set! x e)")]
[NonTerminalDef(
    "Expr",
    ["e"],
    "x",
    "(int n)",
    "(app e0 e1 ...)",
    "(lambda (x* ...) e)",
    "(nest ((e* ...) ...))",
    "(opt (maybe e))")]
public static partial class CountingLanguage;

[Language]
[Terminal(typeof(Symbol), "x")]
[NonTerminalDef("Program", ["p"], "(locals ((x** ...) ...) e)")]
[NonTerminalDef("Expr", ["e"], "x")]
public static partial class EachAnyLanguage;

[Language]
[Terminal(typeof(SExpr), "d", Name = "datum")]
[NonTerminalDef("Expr", ["e"], "d", "(if e1 e2)", "(e0 e1 ...)", "(quote d)")]
public static partial class PairDispatchLanguage;

[Language]
[Terminal(typeof(Symbol), "x")]
[NonTerminalDef(
    "Expr", ["e"], "(parse x)", "(parse-node x)", "(prune x)", "(parse-expr x)", "(get-type x)")]
public static partial class ReservedNameLanguage;

[Language]
[Terminal(typeof(Symbol), "x")]
[NonTerminalDef("Expr", ["e"], """(va\r |x|) => \x""", """(|tag with space| \x)""", "(|.| x)")]
public static partial class EscapedSymbolLanguage;

[Language]
[Terminal(typeof(Symbol), "x", Name = "variable", Predicate = nameof(IsVariable))]
[Terminal(typeof(SExpr), "c", Name = "constant")]
[Terminal(typeof(Symbol), "pr", Name = "primitive")]
[NonTerminalDef(
    "Expr",
    ["e"],
    "(var x)",
    "(quote c)",
    "(begin e0 ... e1)",
    "(if e0 e1 e2)",
    "(lambda (x ...) e0 ... e1)",
    "(let ([x e] ...) e0 ... e1)",
    "(letrec ([x e] ...) e0 ... e1)",
    "(primapp pr e1 ...)",
    "(app e0 e1 ...)")]
public static partial class ClauseBaseLanguage
{
    private static bool IsVariable(Symbol symbol) =>
        !string.Equals(symbol.Name, "+", StringComparison.Ordinal);
}

[Language]
[Extends(typeof(ClauseBaseLanguage))]
[ExtendNonTerminal(
    "Expr",
    MetaVars = ["out"],
    Remove = ["(if e0 e1 e2)"],
    Add = ["(if e0 e1)", "(parallel ([x* e*] ...) e4*? ... e^)", "(wrap out)"])]
[NonTerminalDef("Unused", ["u"], "(unused)", "(if u0 u1)")]
public static partial class ClauseExtendedLanguage;

[Language]
[PrunedLanguage(typeof(ClauseExtendedLanguage))]
public static partial class ClausePrunedLanguage;

[Language]
[Terminal(
    typeof(PrettyLanguage.VariableName), "x", Name = "var", Prettifier = nameof(PrintVariable))]
[Terminal(typeof(SExpr), "c", Name = "constant")]
[Terminal(typeof(Symbol), "pr", Name = "primitive")]
[NonTerminalDef(
    "Expr",
    ["e", "body"],
    "(var x) => x",
    "(quoted c) => (quote c)",
    "(seq e0 e1) => (begin e0 e1)",
    "(if e0 e1 e2)",
    "(lambda (x ...) e0 ... e1)",
    "(binding (x ...) (e ...) body0 ... body1) => (let ([x e] ...) body0 ... body1)",
    "(recbinding (x ...) (e ...) body0 ... body1) => (letrec ([x e] ...) body0 ... body1)",
    "(primapp pr e1 ...) => (pr e1 ...)",
    "(app e0 e1 ...) => (e0 e1 ...)")]
public static partial class PrettyLanguage
{
    public readonly record struct VariableName(Symbol Name);

    private static Symbol PrintVariable(VariableName variable) => variable.Name;
}
