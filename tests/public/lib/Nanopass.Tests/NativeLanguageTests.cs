using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Hcoona.Nanopass.Tests;

[TestClass]
public sealed class NativeLanguageTests
{
    [TestMethod]
    public void TypeFirstUnion_UsesValueEqualityAndCustomPretty()
    {
        NativeBaseLanguage.Expr expected = new NativeBaseLanguage.Add(
            new NativeBaseLanguage.Int(2), new NativeBaseLanguage.Int(3));
        var parsed = NativeBaseLanguage.Parse("(plus (int 2) (int 3))");

        Assert.AreEqual(expected, parsed);
        Assert.IsTrue(expected == parsed);
        Assert.AreEqual(expected.GetHashCode(), parsed.GetHashCode());
        Assert.AreEqual(
            "(plus (int 2) (int 3))", NativeBaseLanguage.UnparseToString(parsed, raw: true));
        Assert.AreEqual("(+ (int 2) (int 3))", NativeBaseLanguage.UnparseToString(parsed));
        Assert.AreEqual(3, NativeBaseLanguage.CountNodes(parsed));
    }

    [TestMethod]
    public void TypeFirstExtension_RewritesFieldTypesAndDropsEmptyNonTerminals()
    {
        NativeDerivedLanguage.Expr expected = new NativeDerivedLanguage.Add(
            new NativeDerivedLanguage.Expr.Multiply(
                new NativeDerivedLanguage.Int(2), new NativeDerivedLanguage.Int(3)),
            new NativeDerivedLanguage.Int(4));

        var parsed = NativeDerivedLanguage.Parse("(plus (multiply (int 2) (int 3)) (int 4))");
        Assert.AreEqual(expected, parsed);
        Assert.DoesNotContain(
            new Symbol("Dead"),
            NativeDerivedLanguage.Descriptor.NonTerminals.Select(static nt => nt.Name));
        Assert.ThrowsExactly<NanopassParseException>(
            () => NativeDerivedLanguage.Parse("(neg (int 1))"));
        var exception = Assert.ThrowsExactly<NanopassException>(
            () => NativeDerivedLanguage.UnparseObject(new NativeBaseLanguage.Int(7)));
        Assert.Contains("unrecognized NativeDerivedLanguage language form", exception.Message);
    }

    [TestMethod]
    public void TypeFirstInference_ModelsNestedNullableSequencesAndValueUnion()
    {
        NativeBaseLanguage.Expr one = new NativeBaseLanguage.Int(1);
        NativeBaseLanguage.Expr expected =
            new NativeBaseLanguage.Tree([[one, null], [], [null]], null);
        const string input = "(tree (((int 1) #f) () (#f)) #f)";

        Assert.AreEqual(expected, NativeBaseLanguage.Parse(input));
        Assert.AreEqual(input, NativeBaseLanguage.UnparseToString(expected));
        Assert.IsTrue(NativeBaseLanguage.ParseValue("#f").TryGetValue(out bool value));
        Assert.IsFalse(value);
        Assert.IsTrue(NativeBaseLanguage.IsExpr(NativeBaseLanguage.ParseValue("#f")));
    }

    [TestMethod]
    public void TypeFirstExtension_RetainsPrettyHandlerForDerivedChildForms()
    {
        const string input = "(named (multiply (int 2) (int 3)))";
        var term = NativeDerivedLanguage.Parse(input);
        Assert.AreEqual(input, NativeDerivedLanguage.UnparseToString(term, raw: true));
        Assert.AreEqual(
            "(named! (multiply (int 2) (int 3)))",
            NativeDerivedLanguage.UnparseToString(term));
    }

    [TestMethod]
    public void TypeFirstInference_TreatsNestedRecordStructAsAnOpaqueTerminal()
    {
        var opaque = new NativeDerivedLanguage.Expr.Opaque(7);
        var input = SExpr.List(SExpr.Parse("useOpaque"), SExpr.FromValue(opaque));
        NativeDerivedLanguage.Expr expected = new NativeDerivedLanguage.Expr.UseOpaque(opaque);

        Assert.AreEqual(expected, NativeDerivedLanguage.Parse(input));
        Assert.AreEqual(input, NativeDerivedLanguage.Unparse(expected, raw: true));
        Assert.ThrowsExactly<NanopassParseException>(
            () => NativeDerivedLanguage.Parse("(opaque 7)"));
    }

    [TestMethod]
    public void TypeFirstExtension_PrefersExplicitTerminalToInheritedNonterminal()
    {
        var value = NativeBaseLanguage.Parse("(int 7)");
        var datum = SExpr.List(SExpr.Parse("hold"), SExpr.FromValue(value));
        OpaqueBaseExpressionLanguage.Expr expected = new OpaqueBaseExpressionLanguage.Hold(value);

        Assert.AreEqual(expected, OpaqueBaseExpressionLanguage.Parse(datum));
        Assert.AreEqual(datum, OpaqueBaseExpressionLanguage.Unparse(expected, raw: true));
    }

    [TestMethod]
    public void TypeFirstSyntax_UsesTheSelectedTerminalWithTheSameClrType()
    {
        SharedTerminalTypeLanguage.Expr expected = new SharedTerminalTypeLanguage.Prim(
            new Symbol("add"));
        var parsed = SharedTerminalTypeLanguage.Parse("(prim add)");

        Assert.AreEqual(expected, parsed);
        Assert.AreEqual(
            "(prim add)", SharedTerminalTypeLanguage.UnparseToString(parsed, raw: true));
        Assert.AreEqual("(prim add!)", SharedTerminalTypeLanguage.UnparseToString(parsed));
        Assert.ThrowsExactly<NanopassParseException>(
            () => SharedTerminalTypeLanguage.Parse("(prim other)"));
    }

    [TestMethod]
    public void Prune_UsesResolvedDependenciesInsteadOfInferringFromSyntaxAliases()
    {
        var result = NativeBaseLanguage.Prune().Format();
        Assert.Contains("(Auxiliary (a) (unit))", result);
        Assert.DoesNotContain("(Dead (d)", result);
        var term = NativeBaseLanguage.Parse("(endpoint (unit))");
        Assert.AreEqual("(endpoint (unit))", NativeBaseLanguage.UnparseToString(term));
    }
}

[Language, Extends(typeof(NativeBaseLanguage))]
[Terminal(typeof(NativeBaseLanguage.Expr), "old")]
internal static partial class OpaqueBaseExpressionLanguage
{
    public sealed record Hold(NativeBaseLanguage.Expr Value);
}

[Language]
[Terminal(typeof(Symbol), "x", Name = "variable")]
[Terminal(typeof(Symbol), "p", Name = "primitive", Predicate = nameof(IsPrimitive),
    Prettifier = nameof(PrintPrimitive))]
internal static partial class SharedTerminalTypeLanguage
{
    public partial union Expr(Prim);

    [Syntax("(prim p)")]
    public sealed record Prim(Symbol Name);

    private static bool IsPrimitive(Symbol value) => value == new Symbol("add");

    private static Symbol PrintPrimitive(Symbol value) => new(value.Name + "!");
}
