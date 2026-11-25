using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Hcoona.Nanopass.Tests;

[TestClass]
public sealed class LanguageServicesTests
{
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
