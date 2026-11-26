using System;
using System.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Hcoona.Nanopass.Tests;

[TestClass]
public sealed class LanguageServicesTests
{
    [TestMethod]
    public void IsLanguageTerm_RejectsInvalidTerminalUnderNestedOptionalSequences()
    {
        MembershipLanguage.Expr valid =
            new MembershipLanguage.Batch([[null, new MembershipLanguage.Leaf(2)]]);
        MembershipLanguage.Expr invalid =
            new MembershipLanguage.Batch([[null], [new MembershipLanguage.Leaf(-1)]]);

        Assert.IsTrue(MembershipLanguage.IsExpr(valid));
        Assert.IsFalse(MembershipLanguage.IsExpr(invalid));
        Assert.IsFalse(MembershipLanguage.IsLanguageTerm(invalid.Value));
    }

    [TestMethod]
    public void Parse_SharedPrefixConditionalsDoNotBacktrackRecursively()
    {
        string source = "#t";
        const int depth = 28;
        for (int i = 0; i < depth; i++)
        {
            source = $"(if {source} 1 2)";
        }

        var datum = SExpr.Parse(source);
        _ = MembershipLanguage.Parse("#t");
        long start = Stopwatch.GetTimestamp();
        var term = MembershipLanguage.Parse(datum);

        Assert.IsLessThan(TimeSpan.FromSeconds(1), Stopwatch.GetElapsedTime(start));
        Assert.AreEqual(datum, MembershipLanguage.Unparse(term));
    }

    [TestMethod]
    public void Parse_SharedPrefixWithCyclicInclusionsReusesEachDatum()
    {
        SExpr datum = new SExpr.Bool(true);
        const int depth = 28;
        for (int i = 0; i < depth; i++)
        {
            datum = SExpr.List(new SExpr.Sym("if"), datum, new SExpr.Bool(true));
        }

        GraphLanguage.ResetChecks();
        var term = GraphLanguage.Parse(datum);

        Assert.IsLessThanOrEqualTo(2 * depth, GraphLanguage.Checks);
        Assert.AreEqual(datum, GraphLanguage.Unparse(term));
    }

    [TestMethod]
    public void IsTerm_SharedDagChecksTheLeafOnce()
    {
        GraphLanguage.Expr term = new GraphLanguage.Leaf(1);
        for (int i = 0; i < 28; i++)
        {
            term = new GraphLanguage.Fork(term, term);
        }

        GraphLanguage.ResetChecks();

        Assert.IsTrue(GraphLanguage.IsExpr(term));
        Assert.AreEqual(1, GraphLanguage.Checks);
    }

    [TestMethod]
    [DataRow("(tag . #f)", null)]
    [DataRow("(tag . 1)", 1L)]
    public void Parse_DottedMaybeTailRoundTrips(string text, long? expected)
    {
        var term = MembershipLanguage.Parse(text);
        var tag = Assert.IsInstanceOfType<MembershipLanguage.Tag>(term.Value);

        Assert.AreEqual(expected, (long?)tag.E?.Value);
        Assert.AreEqual(text, MembershipLanguage.UnparseToString(term));
    }

    [TestMethod]
    public void Parse_RepetitionAcceptsDottedTailSplitAndRoundTripsEmptySequence()
    {
        var dotted = MembershipLanguage.ParseDotted(SExpr.Parse("(a end)"));
        var pair = Assert.IsInstanceOfType<MembershipLanguage.Pair>(dotted.Value);
        Assert.AreEqual<Seq<Symbol>>(["a"], pair.SList);
        Assert.AreEqual(SExpr.Parse("(end)"), pair.T);
        Assert.AreEqual(dotted, MembershipLanguage.ParseDotted(MembershipLanguage.Unparse(dotted)));

        var empty = MembershipLanguage.ParseRepeating(SExpr.Nil.Instance);
        Assert.AreSame(SExpr.Nil.Instance, MembershipLanguage.Unparse(empty));
        Assert.AreEqual(
            empty, MembershipLanguage.ParseRepeating(MembershipLanguage.Unparse(empty)));
    }

    [TestMethod]
    public void Parse_FloatNarrowingRejectsOverflowButAcceptsExplicitInfinity()
    {
        Assert.ThrowsExactly<NanopassParseException>(
            () => MembershipLanguage.ParseReal(SExpr.Parse("1e39")));
        var infinity = MembershipLanguage.ParseReal(SExpr.Parse("-inf.0"));
        Assert.AreEqual(float.NegativeInfinity, Assert.IsInstanceOfType<float>(infinity.Value));
    }

    [TestMethod]
    public void ToSExpression_IncludesPrettifiersOnlyWhenRequested()
    {
        LanguageDescriptor language = new(
            "L", "Expr",
            [new("variable", ["x"], typeof(Symbol), Prettifier: SExpr.Parse("print-variable"))],
            [new("Expr", ["e"], [new(SExpr.Parse("(var x)"), Pretty: SExpr.Parse("x"))])]);

        Assert.AreEqual(
            SExpr.Parse("(define-language L (entry Expr) "
                + "(terminals (variable (x))) (Expr (e) (var x)))"),
            LanguageServices.ToSExpression(language));
        Assert.AreEqual(
            SExpr.Parse("""
                (define-language L (entry Expr)
                  (terminals (=> (variable (x)) print-variable))
                  (Expr (e) (=> (var x) x)))
                """),
            LanguageServices.ToSExpression(language, includePrettifiers: true));
    }

    [TestMethod]
    public void Diff_Extension_ReportsChangedClausesByNameAndStructuralSyntax()
    {
        LanguageDescriptor source = new(
            "L0", "Expr", [new("symbol", ["x"]), new("number", ["n"])],
            [
                NonTerminal("Expr", "e", "(use x)", "(old e)", "(old2 e)"),
                NonTerminal("Old", "o", "n"),
            ]);
        LanguageDescriptor target = new(
            "L1", "New", [new("symbol", ["x"]), new("string", ["s"])],
            [
                NonTerminal("Expr", "expr", "[use x]", "(new expr)"),
                NonTerminal("New", "f", "s", "(next expr)"),
            ]);

        Assert.AreEqual(
            SExpr.Parse("""
                (define-language L1 (extends L0) (entry New)
                  (terminals (- (number (n))) (+ (string (s))))
                  (New (f) (+ s (next expr)))
                  (Old (o) (- n))
                  (Expr (expr) (- (old2 e) (old e)) (+ (new expr))))
                """),
            LanguageServices.Diff(source, target));
    }

    [TestMethod]
    public void Diff_UnchangedGrammar_EmitsOnlyExtendsAndEntry()
    {
        LanguageDescriptor source = new("L0", "Expr", [new("symbol", ["x"])],
            [NonTerminal("Expr", "e", "(var x)")]);
        LanguageDescriptor target = source with { Name = "L1" };

        Assert.AreEqual(SExpr.Parse("(define-language L1 (extends L0) (entry Expr))"),
            LanguageServices.Diff(source, target));
    }

    [TestMethod]
    public void Diff_ChangedTerminalMetavariables_EmitsRemovalAndAddition()
    {
        LanguageDescriptor source = new("L0", "Expr", [new("symbol", ["x", "y"])], []);
        LanguageDescriptor target = new("L1", "Expr", [new("symbol", ["y", "x"])], []);

        Assert.AreEqual(
            SExpr.Parse("(define-language L1 (extends L0) (entry Expr) "
                + "(terminals (- (symbol (x y))) (+ (symbol (y x)))))"),
            LanguageServices.Diff(source, target));
    }

    [TestMethod]
    public void Prune_FollowsImplicitOptionalDottedAndCyclicReferencesToFixedPoint()
    {
        LanguageDescriptor language = new(
            "L", "Expr", [new("constant", ["c"]), new("variable", ["x"]), new("unused", ["u"])],
            [
                NonTerminal("Expr", "e", "(s1^ ... . x2?)"),
                NonTerminal("Unused", "z", "(z2 . u)"),
                NonTerminal("Step", "s", "(next ((maybe l) ...) x)"),
                NonTerminal("Leaf", "l", "(again e)", "c"),
            ]);

        Assert.AreEqual(
            SExpr.Parse("""
                (define-language L (entry Expr)
                  (terminals (constant (c)) (variable (x)))
                  (Leaf (l) (again e) c)
                  (Step (s) (next ((maybe l) ...) x))
                  (Expr (e) (s1^ ... . x2?)))
                """),
            LanguageServices.Prune(language));
        var pruned = LanguageServices.PruneDescriptor(language);
        Assert.AreEqual(pruned, LanguageServices.PruneDescriptor(pruned));
    }

    [TestMethod]
    public void Prune_UnreachableTerminalAndProductionPrettifier_AreNotDependencies()
    {
        LanguageDescriptor language = new("L", "Expr", [new("symbol", ["x"])],
            [new("Expr", ["e"], [new(SExpr.Parse("(self e)"), Pretty: SExpr.Parse("x"))])]);

        Assert.AreEqual(SExpr.Parse("(define-language L (entry Expr) (Expr (e) (self e)))"),
            LanguageServices.Prune(language));
    }

    [TestMethod]
    public void Prune_VisitsAnOptionalFieldAtTheHeadOfAnImplicitProduction()
    {
        LanguageDescriptor language = new(
            "L", "Expr", [new("variable", ["x"]), new("unused", ["u"])],
            [NonTerminal("Expr", "e", "((maybe x) e)")]);

        Assert.AreEqual(
            SExpr.Parse("(define-language L (entry Expr) "
                + "(terminals (variable (x))) (Expr (e) ((maybe x) e)))"),
            LanguageServices.Prune(language));
    }

    [TestMethod]
    public void PruneDescriptor_RetainsTerminalsReferencedThroughMixedMetavariableSuffixes()
    {
        LanguageDescriptor language = new(
            "L", "Expr", [new("variable", ["x"]), new("unused", ["u"])],
            [NonTerminal("Expr", "e", "(use x*1)")]);

        LanguageDescriptor pruned = LanguageServices.PruneDescriptor(language);

        Assert.AreEqual(new Symbol("variable"), Assert.ContainsSingle(pruned.Terminals).Name);
    }

    [TestMethod]
    public void Prune_MissingEntry_RejectsInvalidDescriptor()
    {
        LanguageDescriptor invalid = new("L", "Missing", [], [NonTerminal("Found", "f", "(atom)")]);

        var error = Assert.ThrowsExactly<NanopassException>(() => LanguageServices.Prune(invalid));

        Assert.Contains("Missing", error.Message);
    }

    private static NonTerminalDescriptor NonTerminal(
        string name, string metaVariable, params string[] syntax) =>
        new(name, [metaVariable],
            [.. syntax.Select(static text => new ProductionDescriptor(SExpr.Parse(text)))]);
}

[Language, Entry("Expr")]
[Terminal(typeof(long), "n", Predicate = nameof(IsNonNegative))]
[Terminal(typeof(bool), "b")]
[Terminal(typeof(Symbol), "s")]
[Terminal(typeof(SExpr), "t", Predicate = nameof(HasEndMarker))]
[Terminal(typeof(float), "r")]
[NonTerminalDef("Expr", ["e"], "b", "n", "(leaf n)", "(if e1 e2)", "(if e1 e2 e3)",
    "(batch (((maybe e) ...) ...))", "(tag . (maybe e))")]
[NonTerminalDef("Repeating", ["items"], "(e ...)")]
[NonTerminalDef("Dotted", ["dotted"], "(s ... . t)")]
[NonTerminalDef("Real", ["real"], "r")]
internal static partial class MembershipLanguage
{
    private static bool IsNonNegative(long n) => n >= 0;

    private static bool HasEndMarker(SExpr datum) =>
        datum is SExpr.Pair { Car: SExpr.Sym(var marker), Cdr: SExpr.Nil } && marker.Name == "end";
}

[Language, Entry("Expr")]
[Terminal(typeof(long), "n", Predicate = nameof(CheckNumber))]
[Terminal(typeof(bool), "b", Predicate = nameof(CheckBoolean))]
[NonTerminalDef("Expr", ["e"], "f", "(if e1 n)", "(if e1 b)", "(fork e1 e2)", "(leaf n)")]
[NonTerminalDef("Form", ["f"], "e", "b")]
internal static partial class GraphLanguage
{
    [ThreadStatic]
    private static int checks;

    internal static int Checks => checks;

    internal static void ResetChecks() => checks = 0;

    private static bool CheckNumber(long value) => CountCheck() && value >= 0;

    private static bool CheckBoolean(bool value) => CountCheck() && value;

    private static bool CountCheck() => ++checks <= 256
        ? true
        : throw new InvalidOperationException("Traversal repeated work beyond the test budget.");
}
