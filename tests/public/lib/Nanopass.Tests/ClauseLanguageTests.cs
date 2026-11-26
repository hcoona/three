using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Hcoona.Nanopass.Tests;

[TestClass]
public sealed class ClauseLanguageTests
{
    [TestMethod]
    [DataRow("#t", true)]
    [DataRow("#f", false)]
    public void BooleanCases_RetainFalseInBothUnionRepresentations(string text, bool expected)
    {
        var single = BooleanLanguage.Parse(text);
        var value = BooleanLambdaLanguage.ParseValue(text);
        Assert.AreEqual(expected, Assert.IsInstanceOfType<bool>(single.Value));
        Assert.IsTrue(value.TryGetValue(out bool actual));
        Assert.AreEqual(expected, actual);
        Assert.AreEqual(text, BooleanLambdaLanguage.UnparseToString(value));
        Assert.ThrowsExactly<NanopassParseException>(() => BooleanLanguage.Parse("a"));
    }

    [TestMethod]
    public void TermPredicates_UnwrapInclusionsAndValidateTerminalPredicates()
    {
        Assert.IsTrue(BooleanLambdaLanguage.IsValue(BooleanLambdaLanguage.Parse("#t")));

        InclusionPrettyLanguage.Atom invalid = new Symbol("reserved");
        InclusionPrettyLanguage.Value wrapped = invalid;
        Assert.IsFalse(InclusionPrettyLanguage.IsAtom(invalid));
        Assert.IsFalse(InclusionPrettyLanguage.IsValue(wrapped));
        Assert.IsFalse(InclusionPrettyLanguage.IsLanguageTerm(wrapped));
        Assert.IsTrue(InclusionPrettyLanguage.IsAtom(InclusionPrettyLanguage.Parse("source")));
    }

    [TestMethod]
    public void Unparse_PrettyInclusionsWrapTheRecursivelyPrettifiedTarget()
    {
        const string input = "(pair source (unit))";
        var term = InclusionPrettyLanguage.Parse(input);

        Assert.AreEqual(input, InclusionPrettyLanguage.UnparseToString(term, raw: true));
        Assert.AreEqual(
            "(pair (expr (value (id source))) (expr cooked))",
            InclusionPrettyLanguage.UnparseToString(term));
    }

    [TestMethod]
    public void Unparse_PrettyInclusionsPreserveTheSelectedSiblingAndRepeatedCaptures()
    {
        TwoInclusionsLanguage.W sibling = new Symbol("datum");
        TwoInclusionsLanguage.Expr included = sibling;
        TwoInclusionsLanguage.V intermediate = sibling;
        TwoInclusionsLanguage.Expr repeated = intermediate;

        Assert.AreEqual("(from-w (w-symbol datum))",
            TwoInclusionsLanguage.UnparseToString(included));
        Assert.AreEqual(
            SExpr.Parse("(w-symbol datum)"), TwoInclusionsLanguage.UnparseObject(sibling));
        Assert.AreEqual(
            "(dup (v-form (w-symbol datum)) (v-form (w-symbol datum)))",
            TwoInclusionsLanguage.UnparseToString(repeated));
        Assert.AreEqual("datum", TwoInclusionsLanguage.UnparseToString(repeated, raw: true));
    }

    [TestMethod]
    [DataRow("(lambda (x) #f)")]
    [DataRow("(lambda (f) (f #f))")]
    [DataRow("(lambda (f) (not (f #f)))")]
    [DataRow("(and + (or x #t))")]
    public void BooleanLambda_InclusionsAndSameClrTypeAlternativesRoundTrip(string text)
    {
        var term = BooleanLambdaLanguage.Parse(text);
        Assert.AreEqual(text, BooleanLambdaLanguage.UnparseToString(term));
        Assert.IsTrue(BooleanLambdaLanguage.IsExpr(term));
        Assert.IsTrue(BooleanLambdaLanguage.IsLanguageTerm(term));
    }

    [TestMethod]
    [DataRow("(Int 72)")]
    [DataRow("(Bool #t)")]
    [DataRow("(Bool #f)")]
    [DataRow("(Bar 5 (Bool #t))")]
    [DataRow("(Bar #f (Bool #t))")]
    [DataRow("(Foo 5 #f)")]
    [DataRow("(Foo 5 (Foo 4 (Foo 3 #f)))")]
    [DataRow("(Foo 5 (Bar 3 (Foo 1 #f)))")]
    [DataRow("(Foo 5 (Int 3))")]
    public void Maybe_ScalarFieldsRoundTrip(string text) =>
        Assert.AreEqual(text, MaybeLanguage.UnparseToString(MaybeLanguage.Parse(text)));

    [TestMethod]
    [DataRow("(Foo 3)")]
    [DataRow("(Bar (Int 72))")]
    [DataRow("(Int 72)")]
    [DataRow("(Bool #t)")]
    [DataRow("(Bar 5 (Bool #t))")]
    [DataRow("(Bar #f (Bool #t))")]
    [DataRow("(Bar #f 1 #f 2 #f 3 (Bool #t))")]
    [DataRow("(Bar 1 #f 2 #f 3 #f (Bool #t))")]
    [DataRow("(Foo 5 #f)")]
    [DataRow("(Foo 5 #f #f (Bar 3 (Foo 2 #f)) (Bool #t) #f #f (Int 2) #f)")]
    [DataRow("(Foo 5 (Foo 4 (Foo 3 #f (Bool #t) (Int 3))))")]
    [DataRow("(Foo 5 (Bar 3 (Foo 1 (Bar 2 (Bool #t)) #f #f)))")]
    [DataRow("(Foo 5 (Int 3) (Bool #f))")]
    public void MaybeEllipsis_SeparatesFalseValuesFromAbsentLeaves(string text)
    {
        var term = MaybeSequenceLanguage.Parse(text);
        Assert.AreEqual(text, MaybeSequenceLanguage.UnparseToString(term));
    }

    [TestMethod]
    public void MaybeEllipsis_EmitsNullableSlotsRatherThanNullableSequences()
    {
        var bar = Assert.IsInstanceOfType<MaybeSequenceLanguage.Bar>(
            MaybeSequenceLanguage.Parse("(Bar #f 1 (Bool #f))").Value);
        Assert.AreEqual<Seq<long?>>([null, 1], bar.IList);
        Assert.IsFalse(Assert.IsInstanceOfType<MaybeSequenceLanguage.Bool>(bar.E.Value).B);

        var foo = Assert.IsInstanceOfType<MaybeSequenceLanguage.Foo>(
            MaybeSequenceLanguage.Parse("(Foo 0 #f (Bool #f))").Value);
        Assert.AreEqual<Seq<MaybeSequenceLanguage.Exp?>>(
            [null, MaybeSequenceLanguage.Parse("(Bool #f)")], foo.EList);
    }

    [TestMethod]
    [DataRow("(foo x 10)")]
    [DataRow("(bar (foo x #f) x)")]
    [DataRow("(bar (bar (foo y #f) y) z)")]
    [DataRow("(bar (bar (bar #f x) y) z)")]
    public void Maybe_AbsentFieldsDoNotRequireABooleanTerminal(string text)
    {
        var term = MaybeWithoutBooleanLanguage.Parse(text);
        Assert.AreEqual(text, MaybeWithoutBooleanLanguage.UnparseToString(term));
    }

    [TestMethod]
    [DataRow("(foo ([x 10] [y 12]) (ref x))")]
    [DataRow("(foo ([x #f] [y 12] [z #f]) (ref y))")]
    [DataRow("(bar #f #f (num 10) (ref x) #f (foo ([x #f] [y 10] [z 5] [w #f]) (bar #f z)) #f w)")]
    public void MaybeEllipsis_ParallelFieldsRoundTripWithoutBooleanTerminal(string text)
    {
        var term = MaybeParallelLanguage.Parse(text);
        Assert.AreEqual(SExpr.Parse(text), MaybeParallelLanguage.Unparse(term));
    }

    [TestMethod]
    [DataRow("()")]
    [DataRow("a")]
    [DataRow("(a)")]
    [DataRow("(a . b)")]
    [DataRow("(a b c . d)")]
    [DataRow("(((a b . c) d e) f . g)")]
    public void Pairs_ImplicitDottedAlternativeRoundTrips(string text) =>
        Assert.AreEqual(text, PairsLanguage.UnparseToString(PairsLanguage.Parse(text)));

    [TestMethod]
    public void Pairs_ConstructedDottedRecordsPreserveBothSubtrees()
    {
        PairsLanguage.Expr a = new Symbol("a");
        PairsLanguage.Expr b = new Symbol("b");
        PairsLanguage.Expr c = new Symbol("c");
        PairsLanguage.Expr inner = new PairsLanguage.Pair(c, b);
        PairsLanguage.Expr reversed = new PairsLanguage.Pair(inner, a);

        Assert.AreEqual("((c . b) . a)", PairsLanguage.UnparseToString(reversed));
        Assert.AreEqual(reversed, PairsLanguage.Parse("((c . b) . a)"));
    }

    [TestMethod]
    [DataRow("()")]
    [DataRow("x")]
    [DataRow("(a b c)")]
    [DataRow("(a b c . d)")]
    [DataRow("(((a b) (c d)) e . f)")]
    public void DottedEllipsis_CapturesTheImproperTail(string text) =>
        Assert.AreEqual(text, DottedListLanguage.UnparseToString(DottedListLanguage.Parse(text)));

    [TestMethod]
    public void PrefixEllipsis_ReservesRequiredSuffix()
    {
        var parsed = PrefixListLanguage.Parse("(a b c)");
        var term = Assert.IsInstanceOfType<PrefixListLanguage.Implicit>(parsed.Value);
        Assert.AreEqual<Seq<PrefixListLanguage.Expr>>(
            [PrefixListLanguage.Parse("a"), PrefixListLanguage.Parse("b")], term.E0List);
        Assert.AreEqual(PrefixListLanguage.Parse("c"), term.E1);
        Assert.ThrowsExactly<NanopassParseException>(() => PrefixListLanguage.Parse("(a . b)"));
    }

    [TestMethod]
    [DataRow("x")]
    [DataRow("(lambda (x . z) x)")]
    [DataRow("(lambda (x y . z) x)")]
    [DataRow("(lambda x x)")]
    [DataRow("(define (x y . z) z)")]
    [DataRow("(define x x)")]
    [DataRow("(define (f . x) (define (g . y) x))")]
    [DataRow("(define (l m . n) (define g (lambda (x . z) (lambda (a . b) (lambda (c . d) l)))))")]
    public void ImproperLambda_ParsesZeroLengthEllipsesAndAtomicDefinition(string text)
    {
        var term = ImproperLambdaLanguage.Parse(text);
        Assert.AreEqual(text, ImproperLambdaLanguage.UnparseToString(term));
    }

    [TestMethod]
    public void ClauseGrammar_RoundTripsOriginalBindingAndPrimitiveApplication()
    {
        const string input = "(let ([x '1] [y '2]) (primapp + (var x) (var y)))";
        var term = ClauseBaseLanguage.Parse(input);
        var let = Assert.IsInstanceOfType<ClauseBaseLanguage.Let>(term.Value);

        Assert.AreEqual<Seq<Symbol>>(["x", "y"], let.XList);
        Assert.AreEqual(SExpr.Parse(input), ClauseBaseLanguage.Unparse(term));
        Assert.ThrowsExactly<NanopassParseException>(() => ClauseBaseLanguage.Parse("(let ())"));
    }

    [TestMethod]
    public void EmptyLists_RoundTripWithoutTreatingAtomsAsPairProductions()
    {
        EmptyListLanguage.Expr empty = new EmptyListLanguage.Implicit([]);
        Assert.AreEqual("()", EmptyListLanguage.UnparseToString(empty));
        Assert.AreEqual(empty, EmptyListLanguage.Parse("()"));
        var atom = EmptyDottedListLanguage.Parse("c");
        Assert.AreEqual("c", EmptyDottedListLanguage.UnparseToString(atom));
        Assert.ThrowsExactly<NanopassParseException>(() => EmptyDottedListLanguage.Parse("#f"));
    }

    [TestMethod]
    [DataRow("(locals ((x y z) (w k v l)) x)", 2, 7)]
    [DataRow("(locals () x)", 0, 0)]
    [DataRow("(locals (() ()) x)", 2, 0)]
    public void EachAny_NestedTerminalSequencesRetainEmptyRows(
        string text, int rows, int symbols)
    {
        var term = EachAnyLanguage.Parse(text);
        var locals = Assert.IsInstanceOfType<EachAnyLanguage.Locals>(term.Value);

        Assert.HasCount(rows, locals.XList);
        Assert.AreEqual(symbols, locals.XList.Sum(static row => row.Count));
        Assert.AreEqual(text, EachAnyLanguage.UnparseToString(term));
    }

    [TestMethod]
    public void MultiNonTerminal_ParsesDirectEntryAndNestedBindings()
    {
        const string input =
            "(letrec ([x (case-lambda (clause (y) (var y)) (clause () (quote 1)))]) (app (var x)))";
        var expr = MultiLanguage.Parse(input);
        Assert.AreEqual(SExpr.Parse(input), MultiLanguage.Unparse(expr));
        const string lambdaText = "(lambda () (quote 1))";
        var lambda = MultiLanguage.ParseLambdaExpr(lambdaText);
        Assert.AreEqual(lambdaText, MultiLanguage.UnparseToString(lambda));
        Assert.IsFalse(MultiLanguage.IsLambdaExpr(expr));
        Assert.ThrowsExactly<NanopassParseException>(() => MultiLanguage.Parse(lambdaText));

        var binding = TaggedListLanguage.ParseBinding("[(x y) (x y)]");
        Assert.AreEqual("((x y) (x y))", TaggedListLanguage.UnparseToString(binding));
        const string tagged = "(let-values source ([(x y) (x y)]) x y)";
        var taggedTerm = TaggedListLanguage.Parse(tagged);
        Assert.AreEqual(SExpr.Parse(tagged), TaggedListLanguage.Unparse(taggedTerm));
    }

    [TestMethod]
    [DataRow("a", 1)]
    [DataRow("(int 3)", 1)]
    [DataRow("(lambda (a b) (int 1))", 2)]
    [DataRow("(app a b c)", 4)]
    [DataRow("(app a)", 2)]
    [DataRow("(nest ((a b) (c) ()))", 4)]
    [DataRow("(opt #f)", 1)]
    [DataRow("(opt a)", 2)]
    [DataRow("(app (int 1) (int 2))", 3)]
    public void NodeCounter_CountsOnlyNonterminalLeavesAndRecords(string text, int expected) =>
        Assert.AreEqual(expected, CountingLanguage.CountNodes(CountingLanguage.ParseExpr(text)));

    [TestMethod]
    public void NodeCounter_HandlesAliasesAndRejectsAnUnrecognizedObject()
    {
        Assert.AreEqual(1, CountingLanguage.CountNodes(new Symbol("a")));
        var prog = CountingLanguage.Parse("(prog (set! a (app f (int 1))) (int 2) c)");
        Assert.AreEqual(7, CountingLanguage.CountNodes(prog));
        var error = Assert.ThrowsExactly<NanopassException>(
            () => CountingLanguage.CountNodes("nope"));
        Assert.Contains("unrecognized CountingLanguage language record", error.Message);
        Assert.IsFalse(CountingLanguage.IsLanguageTerm("nope"));
    }

    [TestMethod]
    public void ClauseAlgebra_PreservesNamespacedTypesAndReachability()
    {
        const string input = "(parallel ([x (var x)] [y (var y)]) (var x) (var y))";
        var parsed = ClauseExtendedLanguage.Parse(input);
        var node = Assert.IsInstanceOfType<ClauseExtendedLanguage.Parallel>(parsed.Value);
        Assert.AreEqual<Seq<Symbol>>(["x", "y"], node.XList);
        Assert.AreEqual<Seq<ClauseExtendedLanguage.Expr>>(
            [ClauseExtendedLanguage.Parse("(var x)"), ClauseExtendedLanguage.Parse("(var y)")],
            node.EList);
        var pruned = ClausePrunedLanguage.Parse(input);
        Assert.AreEqual(SExpr.Parse(input), ClausePrunedLanguage.Unparse(pruned));
        Assert.ThrowsExactly<NanopassParseException>(
            () => ClauseExtendedLanguage.Parse("(if (var x) (var y) (var z))"));
        Assert.ThrowsExactly<NanopassParseException>(() => ClausePrunedLanguage.Parse("(var +)"));
        var unused = ClauseExtendedLanguage.ParseUnused("(if (unused) (unused))");
        var included = ClauseExtendedLanguage.Parse("(if (var x) (var y))");
        Assert.IsInstanceOfType<ClauseExtendedLanguage.Unused.If>(unused.Value);
        Assert.IsInstanceOfType<ClauseExtendedLanguage.Expr.If>(included.Value);
        Assert.AreEqual(1, ClausePrunedLanguage.Descriptor.NonTerminals.Count);
        string delta = ClauseBaseLanguage.DiffTo(ClauseExtendedLanguage.Descriptor).Format();
        Assert.Contains("(- (if e0 e1 e2))", delta);
        Assert.Contains("(+ (wrap out) (parallel ((x* e*) ...) e4*? ... e^) (if e0 e1))", delta);
        var wrapped = ClausePrunedLanguage.Parse("(wrap (var x))");
        Assert.AreEqual("(wrap (var x))", ClausePrunedLanguage.UnparseToString(wrapped));
        Assert.DoesNotContain("Unused", ClauseExtendedLanguage.Prune().Format());
        Assert.Contains("(Expr (e out)", ClausePrunedLanguage.ToSExpression().Format());
    }

    [TestMethod]
    public void ClauseParser_RawRoundTripIsAnIdentityOnBoundedRecursiveTerms()
    {
        string[] leaves = ["a", "(int 1)", "(opt #f)"];
        foreach (string left in leaves)
        {
            foreach (string right in leaves)
            {
                string text = $"(nest (() ({left} {right}) ((opt {left}))))";
                var term = CountingLanguage.ParseExpr(text);
                var raw = CountingLanguage.Unparse(term, raw: true);
                Assert.AreEqual(SExpr.Parse(text), raw);
                Assert.AreEqual(term, CountingLanguage.ParseExpr(raw));
                Assert.AreEqual(term.GetHashCode(), CountingLanguage.ParseExpr(raw).GetHashCode());
            }
        }
    }

    [TestMethod]
    public void Parse_PairsUseProductionsButTerminalFieldsAcceptPairs()
    {
        var tagged = PairDispatchLanguage.Parse("(if 1 2)");
        var application = PairDispatchLanguage.Parse("(1 2 3)");
        var quote = PairDispatchLanguage.Parse("'(1 2 3)");

        var conditional = Assert.IsInstanceOfType<PairDispatchLanguage.If>(tagged.Value);
        Assert.AreEqual(new SExpr.Int(1), conditional.E1.Value);
        var form = Assert.IsInstanceOfType<PairDispatchLanguage.Implicit>(application.Value);
        Assert.AreEqual(new SExpr.Int(1), form.E0.Value);
        Assert.HasCount(2, form.E1List);
        var quotation = Assert.IsInstanceOfType<PairDispatchLanguage.Quote>(quote.Value);
        Assert.AreEqual(SExpr.Parse("(1 2 3)"), quotation.D);
        Assert.AreEqual(SExpr.Parse("'(1 2 3)"), PairDispatchLanguage.Unparse(quote));
        Assert.ThrowsExactly<NanopassParseException>(() => PairDispatchLanguage.Parse("(1 . 2)"));
    }

    [TestMethod]
    public void Parse_ReservedProductionNamesDoNotShadowLanguageMembers()
    {
        var parse = ReservedNameLanguage.Parse("(parse datum)");
        var prune = ReservedNameLanguage.Parse("(prune datum)");
        var parseExpr = ReservedNameLanguage.ParseExpr("(parse-expr datum)");
        var getType = ReservedNameLanguage.Parse("(get-type datum)");

        Assert.AreEqual(
            new Symbol("datum"),
            Assert.IsInstanceOfType<ReservedNameLanguage.ParseNode2>(parse.Value).X);
        Assert.IsInstanceOfType<ReservedNameLanguage.PruneNode>(prune.Value);
        Assert.IsInstanceOfType<ReservedNameLanguage.ParseExprNode>(parseExpr.Value);
        Assert.IsInstanceOfType<ReservedNameLanguage.GetTypeNode>(getType.Value);
        Assert.AreEqual("(parse datum)", ReservedNameLanguage.UnparseToString(parse));
    }

    [TestMethod]
    public void Parse_EscapedMetavariablesAndTagsUseCanonicalSymbolNames()
    {
        var term = EscapedSymbolLanguage.Parse("(var source)");
        var variable = Assert.IsInstanceOfType<EscapedSymbolLanguage.Var>(term.Value);
        Assert.AreEqual(new Symbol("source"), variable.X);
        Assert.AreEqual("source", EscapedSymbolLanguage.UnparseToString(term));
        Assert.AreEqual(
            SExpr.Parse("(var x)"),
            EscapedSymbolLanguage.Descriptor.NonTerminals[0].Productions[0].Syntax);

        var tagged = EscapedSymbolLanguage.Parse("""(tag\ with\ space source)""");
        Assert.AreEqual("(|tag with space| source)", EscapedSymbolLanguage.UnparseToString(tagged));
        var dot = EscapedSymbolLanguage.Parse("""(\. source)""");
        Assert.AreEqual("(|.| source)", EscapedSymbolLanguage.UnparseToString(dot));
    }
}
