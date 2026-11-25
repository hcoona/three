using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Hcoona.Nanopass.Tests;

[TestClass]
public sealed class SExprTests
{
    [TestMethod]
    [DataRow("()", "()")]
    [DataRow("(a . b)", "(a . b)")]
    [DataRow("(a b . c)", "(a b . c)")]
    [DataRow("(a b . ())", "(a b)")]
    [DataRow("(a . (b . (c)))", "(a b c)")]
    [DataRow("(((a b . c) d e) f . g)", "(((a b . c) d e) f . g)")]
    [DataRow("[x e]", "(x e)")]
    [DataRow("'x", "(quote x)")]
    [DataRow("`x", "(quasiquote x)")]
    [DataRow(",x", "(unquote x)")]
    [DataRow(",@x", "(unquote-splicing x)")]
    [DataRow("#(1 [x . y] #true {z})", "#(1 (x . y) #t (z))")]
    [DataRow("\"a\\\"b\\\\c\\n\\r\\t\"", "\"a\\\"b\\\\c\\n\\r\\t\"")]
    [DataRow(
        "(#true #false -5 +6 3.0 -0.0 .5 1e2 -2.5E-1 +inf.0 +nan.0)",
        "(#t #f -5 6 3.0 -0.0 0.5 100.0 -0.25 +inf.0 +nan.0)")]
    public void Format_ParsedDatum_ProducesCanonicalText(string input, string expected)
    {
        SExpr parsed = SExpr.Parse(input);

        Assert.AreEqual(expected, parsed.Format());
        Assert.AreEqual(expected, parsed.ToString());
        Assert.AreEqual(parsed, SExpr.Parse(expected));
    }

    [TestMethod]
    public void ParseMany_SchemeIdentifiers_AreSymbolsRatherThanNumbersOrOperators()
    {
        Seq<SExpr> expected =
        [
            new SExpr.Sym("+"), new SExpr.Sym("-"), new SExpr.Sym("+i"), new SExpr.Sym("->"),
            new SExpr.Sym("..."), new SExpr.Sym("*.6"), new SExpr.Sym("zero?.5"),
            new SExpr.Sym("λ"), new SExpr.Sym("set!"), new SExpr.Sym("/"), new SExpr.Sym("a/b"),
        ];

        Assert.AreEqual(expected, SExpr.ParseMany("+ - +i -> ... *.6 zero?.5 λ set! / a/b"));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    public void Symbol_DefaultAndEmptyName_HaveIdenticalValueSemantics(string? name)
    {
        Symbol uninitialized = default;
        var constructed = new Symbol(name!);

        Assert.AreEqual(string.Empty, uninitialized.Name);
        Assert.AreEqual(uninitialized.Name, constructed.Name);
        Assert.IsTrue(uninitialized.Equals(constructed));
        Assert.IsTrue(uninitialized.Equals((object)constructed));
        Assert.AreEqual(uninitialized.GetHashCode(), constructed.GetHashCode());
        Assert.AreEqual(0, uninitialized.CompareTo(constructed));
    }

    [TestMethod]
    [DataRow(@"abc\)", "abc)")]
    [DataRow(@"a\ b\;c", "a b;c")]
    [DataRow(@"|a\b|", @"a\b")]
    [DataRow(@"|a\|", @"a\")]
    [DataRow(@"ab| cd|ef|g|\|h\\ij", @"ab cdefg|h\ij")]
    [DataRow(@"1\23", "123")]
    [DataRow(@"1|/|2", "1/2")]
    [DataRow(@"\#t", "#t")]
    public void Parse_EscapedSymbolSegments_CombineIntoOneSymbol(string input, string name)
    {
        Assert.AreEqual(new SExpr.Sym(name), SExpr.Parse(input));
    }

    [TestMethod]
    public void Parse_EscapedClosingDelimiterAndDot_AreListElements()
    {
        Assert.AreEqual(
            SExpr.List(new SExpr.Sym("abc)"), new SExpr.Sym("."), new SExpr.Sym(".")),
            SExpr.Parse(@"(abc\) \. .||)"));
    }

    [TestMethod]
    [DataRow("", "||")]
    [DataRow("a b\t\n", "|a b\t\n|")]
    [DataRow("()[]{}\"',;`", "|()[]{}\"',;`|")]
    [DataRow(@"(abc\)", @"|(abc\)|")]
    [DataRow(@"a|b\c", @"a\|b\\c")]
    [DataRow("#|a b;c", @"\#\|a\ b\;c")]
    [DataRow(".", "|.|")]
    [DataRow("#t", "|#t|")]
    [DataRow("123", "|123|")]
    [DataRow("12abc", "|12abc|")]
    [DataRow("9223372036854775808", "|9223372036854775808|")]
    [DataRow("-1.25e2", "|-1.25e2|")]
    [DataRow("+nan.0", "|+nan.0|")]
    [DataRow("1/2", "|1/2|")]
    [DataRow("1+2i", "|1+2i|")]
    public void Format_SymbolWithSpecialSpelling_RoundTripsAsSymbol(string name, string expected)
    {
        SExpr symbol = new SExpr.Sym(name);

        string text = symbol.Format();

        Assert.AreEqual(expected, text);
        Assert.AreEqual(symbol, SExpr.Parse(text));
    }

    [TestMethod]
    [DataRow(@"#\ ", ' ')]
    [DataRow(@"#\\", '\\')]
    [DataRow(@"#\|", '|')]
    [DataRow(@"#\(", '(')]
    [DataRow(@"#\)", ')')]
    [DataRow(@"#\#", '#')]
    [DataRow(@"#\-", '-')]
    [DataRow(@"#\.", '.')]
    [DataRow(@"#\0", '0')]
    [DataRow(@"#\1", '1')]
    [DataRow(@"#\2", '2')]
    [DataRow(@"#\3", '3')]
    [DataRow(@"#\4", '4')]
    [DataRow(@"#\5", '5')]
    [DataRow(@"#\6", '6')]
    [DataRow(@"#\7", '7')]
    [DataRow(@"#\8", '8')]
    [DataRow(@"#\9", '9')]
    [DataRow(@"#\newline", '\n')]
    [DataRow(@"#\space", ' ')]
    [DataRow(@"#\tab", '\t')]
    [DataRow(@"#\return", '\r')]
    public void Parse_SchemeCharacterLiteral_RoundTripsAsCharacter(string input, char value)
    {
        SExpr expected = SExpr.List(new SExpr.Char(value));
        SExpr parsed = SExpr.Parse($"({input})");

        Assert.AreEqual(expected, parsed);
        Assert.AreEqual(expected, SExpr.Parse(parsed.Format()));
    }

    [TestMethod]
    public void ParseMany_Comments_IgnoreLineNestedBlockAndCompleteDatums()
    {
        const string Input = """
            ; discarded to the end of the line
            #| outer #| inner |# outer |#
            1 #;(not #;this kept) (#;drop keep)
            #;#;drop another
            [x #f] ; comment at EOF
            """;
        Seq<SExpr> expected = [new SExpr.Int(1), SExpr.List(new SExpr.Sym("keep")),
            SExpr.List(new SExpr.Sym("x"), new SExpr.Bool(false))];

        Assert.AreEqual(expected, SExpr.ParseMany(Input));
        Assert.IsEmpty(SExpr.ParseMany("#;2 ; only comments"));
    }

    [TestMethod]
    [DataRow("a#;b", "a#")]
    [DataRow("a#|b|#c", "a#b#c")]
    public void Parse_MidSymbolHash_DoesNotStartHashComment(string input, string name)
    {
        Assert.AreEqual(new SExpr.Sym(name), SExpr.Parse(input));
    }

    [TestMethod]
    public void Parse_LiteralForms_PreserveDatumTypesAndValues()
    {
        var vector = Assert.IsInstanceOfType<SExpr.Vector>(
            SExpr.Parse(@"#(-9223372036854775808 9223372036854775807 1.0 #\u03bb ""a\nb"" #f)"));
        Seq<SExpr> expected =
        [
            new SExpr.Int(long.MinValue), new SExpr.Int(long.MaxValue), new SExpr.Float(1),
            new SExpr.Char('λ'), new SExpr.Str("a\nb"), new SExpr.Bool(false),
        ];

        Assert.AreEqual(expected, vector.Items);
        Assert.AreNotEqual(new SExpr.Int(1), (SExpr)new SExpr.Float(1));
    }

    [TestMethod]
    public void SplitList_ImproperList_ReturnsOrderedItemsAndRemainingTail()
    {
        SExpr.Sym a = new("a");
        SExpr.Sym b = new("b");
        SExpr.Sym tail = new("tail");
        SExpr list = SExpr.ImproperList([a, b], tail);

        list.SplitList(out Seq<SExpr> items, out SExpr remainder);

        Assert.AreEqual<Seq<SExpr>>([a, b], items);
        Assert.AreEqual(tail, remainder);
        Assert.IsFalse(list.TryToProperList(out Seq<SExpr> invalid));
        Assert.IsEmpty(invalid);
        SExpr normalized = SExpr.ImproperList([a], SExpr.List(b));
        Assert.IsTrue(normalized.TryToProperList(out Seq<SExpr> proper));
        Assert.AreEqual<Seq<SExpr>>([a, b], proper);
    }

    [TestMethod]
    public void FromValue_TypedClrValues_UseCanonicalCasesAndCannotBeBoxed()
    {
        (object Value, SExpr Datum)[] cases =
        [
            (true, new SExpr.Bool(true)),
            (42, new SExpr.Int(42)),
            (42L, new SExpr.Int(42)),
            ((short)42, new SExpr.Int(42)),
            ((byte)42, new SExpr.Int(42)),
            (1.25f, new SExpr.Float(1.25)),
            (1.25, new SExpr.Float(1.25)),
            ('x', new SExpr.Char('x')),
            ("x", new SExpr.Str("x")),
            (new Symbol("x"), new SExpr.Sym("x")),
        ];

        foreach (var (value, datum) in cases)
        {
            Assert.AreEqual(datum, SExpr.FromValue(value));
            Assert.Contains(
                "SExpr.FromValue",
                Assert.ThrowsExactly<ArgumentException>(() => new SExpr.Boxed(value)).Message);
        }

        SExpr existing = new SExpr.Int(42);
        Assert.AreSame(existing, SExpr.FromValue(existing));
        Assert.ThrowsExactly<ArgumentException>(() => new SExpr.Boxed(existing));
        Assert.ThrowsExactly<ArgumentNullException>(() => SExpr.FromValue(null));
        Assert.ThrowsExactly<ArgumentNullException>(() => new SExpr.Boxed(null!));
    }

    [TestMethod]
    public void Format_OpaqueTerminal_FailsForTextButAllowsDiagnostics()
    {
        var value = new Var("x");
        SExpr boxed = SExpr.FromValue(value);
        SExpr nested = new SExpr.Vector([SExpr.ImproperList([new SExpr.Int(1)], boxed)]);

        Assert.AreEqual(new SExpr.Boxed(value), boxed);
        Assert.AreNotEqual(new SExpr.Boxed(new Var("y")), boxed);
        Assert.ThrowsExactly<InvalidOperationException>(() => boxed.Format());
        Assert.ThrowsExactly<InvalidOperationException>(() => nested.Format());
        Assert.AreEqual("#<boxed Var>", boxed.ToString());
        Assert.AreEqual("#((1 . #<boxed Var>))", nested.ToString());
    }

    [TestMethod]
    [DataRow("", 0)]
    [DataRow("a b", 2)]
    [DataRow("(a]", 2)]
    [DataRow("[a)", 2)]
    [DataRow("(a", 2)]
    [DataRow("(a .)", 4)]
    [DataRow("(a . b c)", 7)]
    [DataRow("(. x)", 1)]
    [DataRow("#(a . b)", 4)]
    [DataRow(".", 0)]
    [DataRow("'", 1)]
    [DataRow("\"unterminated", 13)]
    [DataRow("\"\\q\"", 3)]
    [DataRow(@"abc\", 4)]
    [DataRow("ab|open", 7)]
    [DataRow(@"|a\", 3)]
    [DataRow("#| open", 7)]
    [DataRow("#;", 2)]
    [DataRow("#unknown", 0)]
    [DataRow("#&42", 0)]
    [DataRow("#x2a", 0)]
    [DataRow("#b101", 0)]
    [DataRow("#o42", 0)]
    [DataRow("1/2", 0)]
    [DataRow("(-3/4)", 1)]
    [DataRow("1f3", 0)]
    [DataRow("1##", 0)]
    [DataRow("12abc", 0)]
    [DataRow("1+", 0)]
    [DataRow("-.2wat", 0)]
    [DataRow("1\0", 0)]
    [DataRow("1.0\0", 0)]
    [DataRow("1+2i", 0)]
    [DataRow("1e-2-2/3i", 0)]
    [DataRow("1@2.5", 0)]
    [DataRow("9223372036854775808", 0)]
    public void Parse_InvalidSyntax_ReportsInputOffset(string text, int offset)
    {
        var error = Assert.ThrowsExactly<NanopassParseException>(() => SExpr.Parse(text));

        Assert.AreEqual(offset, error.Offset);
    }

    private readonly record struct Var(string Name);
}
