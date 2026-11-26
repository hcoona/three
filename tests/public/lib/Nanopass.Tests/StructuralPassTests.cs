// Copyright (c) 2022 Zhang Shuai<zhangshuai.ustc@gmail.com>.
// All rights reserved.
//
// This file is part of OneDotNet.
//
// OneDotNet is free software: you can redistribute it and/or modify it under
// the terms of the GNU General Public License as published by the Free
// Software Foundation, either version 3 of the License, or (at your option)
// any later version.
//
// OneDotNet is distributed in the hope that it will be useful, but WITHOUT ANY
// WARRANTY; without even the implied warranty of MERCHANTABILITY or FITNESS
// FOR A PARTICULAR PURPOSE. See the GNU General Public License for more
// details.
//
// You should have received a copy of the GNU General Public License along with
// OneDotNet. If not, see <https://www.gnu.org/licenses/>.

// Scenarios adapted from the MIT-licensed Nanopass Framework Racket suite.
// Upstream copyright and license: src/public/lib/Nanopass/THIRD-PARTY-NOTICES.txt.

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Hcoona.Nanopass.Tests;

/// <summary>
/// Exercises structural pass synthesis over the Racket grammar examples.
/// </summary>
[TestClass]
public sealed class StructuralPassTests
{
    /// <summary>
    /// Verifies that a pass-local predicate does not replace the language's predicate.
    /// </summary>
    [TestMethod]
    public void BreakVariable_PreservesLanguagePredicate()
    {
        var pass = new BreakVariable();
        var input = PredicateVariableLanguage.Parse("(var x)");

        Assert.AreEqual("(var x)", PredicateVariableLanguage.UnparseToString(pass.Run(input)));
        Assert.AreEqual(new Symbol("x"), pass.Found);
        Assert.ThrowsExactly<NanopassParseException>(
            () => PredicateVariableLanguage.Parse("(var +)"));
    }

    /// <summary>
    /// Verifies synthesized and explicit traversal over scalar optional fields.
    /// </summary>
    /// <param name="input">The original datum.</param>
    /// <param name="intOnly">The result when only Int productions change.</param>
    /// <param name="all">The result when all integer fields change.</param>
    [TestMethod]
    [DataRow("(Int 3)", "(Int 4)", "(Int 4)")]
    [DataRow("(Foo 4 (Int 3))", "(Foo 4 (Int 4))", "(Foo 5 (Int 4))")]
    [DataRow("(Foo 4 (Foo 5 (Int 2)))", "(Foo 4 (Foo 5 (Int 3)))", "(Foo 5 (Foo 6 (Int 3)))")]
    [DataRow("(Foo 3 #f)", "(Foo 3 #f)", "(Foo 4 #f)")]
    [DataRow(
        "(Foo 3 (Bar 5 (Foo 6 #f)))",
        "(Foo 3 (Bar 5 (Foo 6 #f)))",
        "(Foo 4 (Bar 6 (Foo 7 #f)))")]
    [DataRow(
        "(Foo 3 (Bar #f (Foo 6 #f)))",
        "(Foo 3 (Bar #f (Foo 6 #f)))",
        "(Foo 4 (Bar #f (Foo 7 #f)))")]
    [DataRow("(Bar #f (Int 4))", "(Bar #f (Int 5))", "(Bar #f (Int 5))")]
    public void AddOne_PreservesAbsentScalarFields(string input, string intOnly, string all)
    {
        var term = MaybeLanguage.Parse(input);

        Assert.AreEqual(intOnly, MaybeLanguage.UnparseToString(new AddOneInt().Run(term)));
        Assert.AreEqual(all, MaybeLanguage.UnparseToString(new AddOne().Run(term)));
    }

    /// <summary>
    /// Verifies that absent sequence elements retain their positions under recursion.
    /// </summary>
    /// <param name="input">The original datum.</param>
    /// <param name="intOnly">The result when only Int productions change.</param>
    /// <param name="all">The result when all integer fields change.</param>
    [TestMethod]
    [DataRow("(Int 3)", "(Int 4)", "(Int 4)")]
    [DataRow("(Foo 3)", "(Foo 3)", "(Foo 4)")]
    [DataRow(
        "(Foo 4 (Int 3) (Int 4) (Int 6) #f #f (Int 7))",
        "(Foo 4 (Int 4) (Int 5) (Int 7) #f #f (Int 8))",
        "(Foo 5 (Int 4) (Int 5) (Int 7) #f #f (Int 8))")]
    [DataRow(
        "(Foo 4 (Foo 5 (Int 2) #f (Int 3) (Int 4)))",
        "(Foo 4 (Foo 5 (Int 3) #f (Int 4) (Int 5)))",
        "(Foo 5 (Foo 6 (Int 3) #f (Int 4) (Int 5)))")]
    [DataRow("(Foo 3 #f (Int 3))", "(Foo 3 #f (Int 4))", "(Foo 4 #f (Int 4))")]
    [DataRow("(Bar 3 #f 4 #f (Int 3))", "(Bar 3 #f 4 #f (Int 4))", "(Bar 4 #f 5 #f (Int 4))")]
    [DataRow(
        "(Foo 4 (Foo 5 (Int 2) (Bar 3 2 1 #f 0 (Foo 2 (Int 7) (Int 8)))))",
        "(Foo 4 (Foo 5 (Int 3) (Bar 3 2 1 #f 0 (Foo 2 (Int 8) (Int 9)))))",
        "(Foo 5 (Foo 6 (Int 3) (Bar 4 3 2 #f 1 (Foo 3 (Int 8) (Int 9)))))")]
    [DataRow(
        "(Foo 3 (Bar 5 #f 7 #f 8 (Foo 6 #f)) (Bool #t) #f)",
        "(Foo 3 (Bar 5 #f 7 #f 8 (Foo 6 #f)) (Bool #t) #f)",
        "(Foo 4 (Bar 6 #f 8 #f 9 (Foo 7 #f)) (Bool #t) #f)")]
    [DataRow(
        "(Foo 3 (Bar #f (Foo 6 #f)) (Bool #t) #f)",
        "(Foo 3 (Bar #f (Foo 6 #f)) (Bool #t) #f)",
        "(Foo 4 (Bar #f (Foo 7 #f)) (Bool #t) #f)")]
    public void AddOne_PreservesAbsentSequenceElements(string input, string intOnly, string all)
    {
        var term = MaybeSequenceLanguage.Parse(input);

        Assert.AreEqual(
            intOnly, MaybeSequenceLanguage.UnparseToString(new AddOneIntSequence().Run(term)));
        Assert.AreEqual(all, MaybeSequenceLanguage.UnparseToString(new AddOneSequence().Run(term)));
    }

    /// <summary>
    /// Verifies that a rewrite may exchange the recursively mapped head and tail.
    /// </summary>
    /// <param name="input">The original datum.</param>
    /// <param name="expected">The datum with all pairs reversed.</param>
    [TestMethod]
    [DataRow("(a)", "(() . a)")]
    [DataRow("(a . b)", "(b . a)")]
    [DataRow("(a b c . d)", "(((d . c) . b) . a)")]
    [DataRow("(((a b . c) d e) f . g)", "((g . f) ((() . e) . d) (c . b) . a)")]
    public void ReversePairs_RecursOverDottedHeadAndTail(string input, string expected)
    {
        var term = PairsLanguage.Parse(input);

        Assert.AreEqual(SExpr.Parse(expected), PairsLanguage.Unparse(new ReversePairs().Run(term)));
    }

    /// <summary>
    /// Verifies that an improper list's tail may move to the front of a proper result list.
    /// </summary>
    /// <param name="input">The original datum.</param>
    /// <param name="expected">The datum with each tail moved to the front.</param>
    [TestMethod]
    [DataRow("(a b c)", "(() a b c)")]
    [DataRow("(a b c . d)", "(d a b c)")]
    [DataRow("(((a b) (c d)) e . f)", "(f (() (() a b) (() c d)) e)")]
    public void SwapParts_RecursOverDottedEllipsis(string input, string expected)
    {
        var term = DottedListLanguage.Parse(input);

        Assert.AreEqual(
            SExpr.Parse(expected), DottedListLanguage.Unparse(new SwapParts().Run(term)));
    }

    /// <summary>
    /// Verifies that shorthand definitions become lambdas with their original formals.
    /// </summary>
    /// <param name="input">The original datum.</param>
    /// <param name="expected">The expanded datum.</param>
    [TestMethod]
    [DataRow("(define (f x . y) x)", "(define f (lambda (x . y) x))")]
    [DataRow("(define (g x y z . w) w)", "(define g (lambda (x y z . w) w))")]
    [DataRow(
        "(define (h x y . z) (define (i a b c . d) d))",
        "(define h (lambda (x y . z) (define i (lambda (a b c . d) d))))")]
    [DataRow(
        "(define (f . x) (define (g . y) x))",
        "(define f (lambda x (define g (lambda y x))))")]
    public void Px1_ExpandsNestedDefinitionsWithImproperFormals(string input, string expected)
    {
        var term = ImproperLambdaLanguage.Parse(input);

        Assert.AreEqual(SExpr.Parse(expected), ImproperLambdaLanguage.Unparse(new Px1().Run(term)));
    }

    /// <summary>
    /// Verifies that generated processors close a cycle and transform expressions below it.
    /// </summary>
    [TestMethod]
    public void MultiLanguage_SynthesizesMutuallyRecursiveProcessors()
    {
        const string input =
            "(letrec ([x (case-lambda (clause (y) (var y)) (clause () (quote 1)))]) (app (var x)))";
        const string expected =
            "(letrec ([x (case-lambda (clause (y) (var y)) (clause () (quote 2)))]) (app (var x)))";
        var pass = new IncrementQuotedNumber();
        var result = pass.Run(MultiLanguage.Parse(input));
        var processors = PassServices.GetDescriptor(pass).Processors;

        Assert.AreEqual(SExpr.Parse(expected), MultiLanguage.Unparse(result));
        var inputKinds = processors.Select(static processor => processor.InputKind);
        Assert.Contains<Symbol?>(new Symbol("LambdaExpr"), inputKinds);
        Assert.Contains<Symbol?>(new Symbol("CaseLambdaClause"), inputKinds);
    }
}

[Language]
[Terminal(typeof(Symbol), "x", Name = "var", Predicate = nameof(IsVar))]
[NonTerminalDef("Expr", ["e"], "(var x)")]
internal static partial class PredicateVariableLanguage
{
    private static bool IsVar(Symbol value) => value != new Symbol("+");
}

[Pass(typeof(PredicateVariableLanguage), typeof(PredicateVariableLanguage))]
internal sealed partial class BreakVariable
{
    internal Symbol Found { get; private set; }

    private static bool IsVar(Symbol value) => value == new Symbol("+");

    [Rewrite]
    private PredicateVariableLanguage.Expr KeepVariable(PredicateVariableLanguage.Var node)
    {
        this.Found = node.X;
        if (IsVar(node.X))
        {
            throw new InvalidOperationException("The pass helper misidentified the variable.");
        }

        return node;
    }
}

[Pass(typeof(MaybeLanguage), typeof(MaybeLanguage))]
internal sealed partial class AddOneInt
{
    [Rewrite]
    private static MaybeLanguage.Exp Increment(MaybeLanguage.Int node) =>
        new MaybeLanguage.Int(node.I + 1);
}

[Pass(typeof(MaybeLanguage), typeof(MaybeLanguage))]
internal sealed partial class AddOne
{
    [Rewrite]
    private static MaybeLanguage.Exp Increment(MaybeLanguage.Int node) =>
        new MaybeLanguage.Int(node.I + 1);

    [Rewrite]
    private static MaybeLanguage.Exp MapFoo(MaybeLanguage.Foo node, [Recur] MaybeLanguage.Exp? e) =>
        new MaybeLanguage.Foo(node.I + 1, e);

    [Rewrite]
    private static MaybeLanguage.Exp MapBar(MaybeLanguage.Bar node, [Recur] MaybeLanguage.Exp e) =>
        new MaybeLanguage.Bar(node.I + 1, e);
}

[Pass(typeof(MaybeSequenceLanguage), typeof(MaybeSequenceLanguage))]
internal sealed partial class AddOneIntSequence
{
    [Rewrite]
    private static MaybeSequenceLanguage.Exp Increment(MaybeSequenceLanguage.Int node) =>
        new MaybeSequenceLanguage.Int(node.I + 1);
}

[Pass(typeof(MaybeSequenceLanguage), typeof(MaybeSequenceLanguage))]
internal sealed partial class AddOneSequence
{
    [Rewrite]
    private static MaybeSequenceLanguage.Exp Increment(MaybeSequenceLanguage.Int node) =>
        new MaybeSequenceLanguage.Int(node.I + 1);

    [Rewrite]
    private static MaybeSequenceLanguage.Exp MapFoo(
        MaybeSequenceLanguage.Foo node,
        [Recur(Field = "EList")] Seq<MaybeSequenceLanguage.Exp?> children) =>
        new MaybeSequenceLanguage.Foo(node.I + 1, children);

    [Rewrite]
    private static MaybeSequenceLanguage.Exp MapBar(
        MaybeSequenceLanguage.Bar node, [Recur] MaybeSequenceLanguage.Exp e) =>
        new MaybeSequenceLanguage.Bar(node.IList.Select(static value => value + 1), e);
}

[Pass(typeof(PairsLanguage), typeof(PairsLanguage))]
internal sealed partial class ReversePairs
{
    [Rewrite(typeof(PairsLanguage.Pair))]
    private static PairsLanguage.Expr Reverse(
        [Recur] PairsLanguage.Expr e0, [Recur] PairsLanguage.Expr e1) =>
        new PairsLanguage.Pair(e1, e0);
}

[Pass(typeof(DottedListLanguage), typeof(DottedListLanguage))]
internal sealed partial class SwapParts
{
    [Rewrite(typeof(DottedListLanguage.Pair))]
    private static DottedListLanguage.Expr Swap(
        [Recur(Field = "E0List")] Seq<DottedListLanguage.Expr> prefix,
        [Recur] DottedListLanguage.Expr e1) =>
        new DottedListLanguage.Pair([e1, .. prefix], SExpr.Nil.Instance);
}

[Pass(typeof(ImproperLambdaLanguage), typeof(ImproperLambdaLanguage))]
internal sealed partial class Px1
{
    [Rewrite]
    private static ImproperLambdaLanguage.Expr Expand(
        ImproperLambdaLanguage.Define4 node, [Recur] ImproperLambdaLanguage.Expr e) =>
        new ImproperLambdaLanguage.Define2(
            node.X, new ImproperLambdaLanguage.Lambda(node.XList, node.X1, e));
}

[Pass(typeof(MultiLanguage), typeof(MultiLanguage))]
internal sealed partial class IncrementQuotedNumber
{
    [Rewrite]
    private static MultiLanguage.Expr Increment(MultiLanguage.Quote node) =>
        new MultiLanguage.Quote(
            node.D is SExpr.Int number ? new SExpr.Int(number.Value + 1) : node.D);
}
