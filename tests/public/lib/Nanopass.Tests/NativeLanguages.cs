#pragma warning disable CA1720

namespace Hcoona.Nanopass.Tests;

[Language]
public static partial class NativeBaseLanguage
{
    [NonTerminal("e")]
    [Entry]
    public partial union Expr(Int, Add, Neg, Tree, Named, Endpoint, Value);

    [NonTerminal("v")]
    [Cases(typeof(bool))]
    public readonly partial struct Value;

    [NonTerminal("d")]
    public readonly partial struct Dead
    {
        public sealed record Unused();
    }

    [NonTerminal("a")]
    public readonly partial struct Auxiliary
    {
        public sealed record Unit();
    }

    public sealed record Int(int Number);

    [Syntax("(plus e1 e2)", Name = "Plus")]
    [Pretty("(+ e1 e2)")]
    public sealed record Add(Expr Left, Expr Right);

    public sealed record Neg(Expr Operand);

    public sealed record Tree(Seq<Seq<Expr?>> Rows, Expr? Tail);

    [Pretty(PrettyHandler = nameof(PrintNamed))]
    public sealed record Named(Expr Body);

    [Syntax("(endpoint body)")]
    public sealed record Endpoint(Auxiliary Body);

    private static SExpr.Pair PrintNamed(SExpr syntax) =>
        new(SExpr.Parse("named!"), ((SExpr.Pair)syntax).Cdr);
}

[Language]
[Extends(typeof(NativeBaseLanguage))]
[Remove(typeof(NativeBaseLanguage.Neg), typeof(NativeBaseLanguage.Dead.Unused))]
public static partial class NativeDerivedLanguage
{
    [NonTerminal("e")]
    public readonly partial struct Expr
    {
        public sealed record Multiply(Expr Left, Expr Right);

        public readonly record struct Opaque(int Number);

        public sealed record UseOpaque(Opaque Data);
    }
}
