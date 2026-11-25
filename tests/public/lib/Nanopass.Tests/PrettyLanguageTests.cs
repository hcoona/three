using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Hcoona.Nanopass.Tests;

[TestClass]
public sealed class PrettyLanguageTests
{
    [TestMethod]
    [DataRow("(quoted 5)", "(quote 5)")]
    [DataRow("(seq (quoted 7) (quoted 8))", "(begin (quote 7) (quote 8))")]
    [DataRow("(var x.0)", "x.0")]
    [DataRow(
        "(binding (x.0 y.1) ((quoted 1) (quoted 2)) (var x.0) (var y.1))",
        "(let ([x.0 '1] [y.1 '2]) x.0 y.1)")]
    public void PrettyTemplates_TransformRecordsAndTerminals(string text, string expected)
    {
        SExpr raw = BoxVariables(SExpr.Parse(text));
        var term = PrettyLanguage.Parse(raw);
        Assert.AreEqual(raw, PrettyLanguage.Unparse(term, raw: true));
        Assert.AreEqual(SExpr.Parse(expected), PrettyLanguage.Unparse(term));
    }

    [TestMethod]
    public void PrettyTemplates_PreserveCharacterLiteralDatums()
    {
        Assert.AreEqual(
            new SExpr.Char('a'),
            CharacterPrettyLanguage.Unparse(CharacterPrettyLanguage.Parse("(letter)")));
        Assert.AreEqual(
            SExpr.List(
                new SExpr.Char('('), new SExpr.Char(')'), new SExpr.Char(' '),
                new SExpr.Char('\\'), new SExpr.Char('\n')),
            CharacterPrettyLanguage.Unparse(CharacterPrettyLanguage.Parse("(delimiters)")));
    }

    [TestMethod]
    public void PrettyTemplates_RecognizeValidLiteralsAndExplicitlyEscapedSymbols()
    {
        Assert.AreEqual(
            SExpr.List(
                new SExpr.Bool(true), new SExpr.Int(long.MaxValue), new SExpr.Float(-0.5),
                new SExpr.Str("line\n"), new SExpr.Sym("1/2"), new SExpr.Sym("#unknown")),
            CharacterPrettyLanguage.Unparse(CharacterPrettyLanguage.Parse("(constants)")));
    }

    [TestMethod]
    public void PrettyTemplates_RecursivelyZipFactorialBindingsWithoutLosingOpaqueTerminals()
    {
        SExpr raw = BoxVariables(SExpr.Parse("""
            (recbinding (zero?.5 *.6 f.7)
              ((lambda (x.1) (primapp = (var x.1) (quoted 0)))
               (lambda (x.2 y.3)
                 (if (app (var zero?.5) (var x.2))
                     (quoted 0)
                     (if (primapp = (var x.2) (quoted 1))
                         (var y.3)
                         (primapp + (var y.3)
                                  (app (var *.6) (primapp - (var x.2) (quoted 1)) (var y.3))))))
               (lambda (x.4)
                 (if (app (var zero?.5) (var x.4))
                     (quoted 1)
                     (app (var *.6) (var x.4) (app (var f.7) (primapp - (var x.4) (quoted 1)))))))
              (app (var f.7) (quoted 10)))
            """));
        SExpr pretty = SExpr.Parse("""
            (letrec ([zero?.5 (lambda (x.1) (= x.1 '0))]
                     [*.6 (lambda (x.2 y.3)
                            (if (zero?.5 x.2)
                                '0
                                (if (= x.2 '1) y.3 (+ y.3 (*.6 (- x.2 '1) y.3)))))]
                     [f.7 (lambda (x.4)
                            (if (zero?.5 x.4) '1 (*.6 x.4 (f.7 (- x.4 '1)))))])
              (f.7 '10))
            """);

        var term = PrettyLanguage.Parse(raw);
        Assert.AreEqual(raw, PrettyLanguage.Unparse(term, raw: true));
        Assert.AreEqual(pretty, PrettyLanguage.Unparse(term));
        Assert.AreEqual(term, PrettyLanguage.Parse(PrettyLanguage.Unparse(term, raw: true)));
        Assert.Contains(
            "(=> (var (x)) PrintVariable)",
            PrettyLanguage.ToSExpression(includePrettifiers: true).Format());
        Assert.DoesNotContain("PrintVariable", PrettyLanguage.ToSExpression().Format());
    }

    [TestMethod]
    public void PrettyTemplates_RejectInconsistentParallelFields()
    {
        var binding = new PrettyLanguage.Binding(
            [new PrettyLanguage.VariableName("x.0")],
            [],
            [],
            PrettyLanguage.Parse("(quoted 1)"));

        var error = Assert.ThrowsExactly<NanopassException>(() => PrettyLanguage.Unparse(binding));
        Assert.Contains("same length", error.Message);
    }

    private static SExpr BoxVariables(SExpr datum) => datum switch
    {
        SExpr.Sym symbol when symbol.Value.Name.Contains('.') =>
            SExpr.FromValue(new PrettyLanguage.VariableName(symbol.Value)),
        SExpr.Pair pair => new SExpr.Pair(BoxVariables(pair.Car), BoxVariables(pair.Cdr)),
        _ => datum,
    };
}

[Language]
[NonTerminalDef(
    "Expr",
    ["e"],
    @"(letter) => #\a",
    @"(delimiters) => (#\( #\) #\  #\\ #\newline)",
    """(constants) => (#true 9223372036854775807 -.5 "line\n" |1/2| |#unknown|)""")]
internal static partial class CharacterPrettyLanguage;
