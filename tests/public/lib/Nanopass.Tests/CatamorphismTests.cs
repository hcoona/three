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

using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Hcoona.Nanopass.Tests;

/// <summary>
/// Tests guarded, parameterized, and multi-result recursion.
/// </summary>
[TestClass]
public sealed class CatamorphismTests
{
    /// <summary>
    /// Verifies that four overlapping guards run in priority order before the selected recursion.
    /// </summary>
    [TestMethod]
    public void GuardedRewrite_RejectedGuardsDoNotEvaluateCatamorphisms()
    {
        var pass = new GuardedPrimitivePass();
        var input = GuardInput.Parse("(primapp value-two (num 2) (num 3))");

        Assert.AreEqual("(value 5)", GuardOutput.UnparseToString(pass.Run(input)));
        Assert.AreSequenceEqual<string>(
            ["guard1", "guard2", "guard3", "guard4", "value 2", "value 3"], pass.Log);
    }

    /// <summary>
    /// Verifies that a selected guard forwards an explicitly renamed context argument.
    /// </summary>
    [TestMethod]
    public void GuardedRewrite_UsesExplicitArgumentsAndSkipsLaterGuards()
    {
        var pass = new GuardedPrimitivePass();
        var input = GuardInput.Parse("(primapp predicate-two (num 2) (num 3))");

        Assert.AreEqual("(value 1)", GuardOutput.UnparseToString(pass.Run(input)));
        Assert.AreSequenceEqual<string>(["guard1", "guard2", "guard3", "ctx 2", "ctx 3"], pass.Log);
    }

    /// <summary>
    /// Verifies that an else rewrite suppresses synthesis after all guards decline a production.
    /// </summary>
    [TestMethod]
    public void GuardedRewrite_AllGuardsDeclineBeforeElseRuns()
    {
        var pass = new GuardedPrimitivePass();
        var input = GuardInput.Parse("(primapp unknown (num 2) (num 3))");

        Assert.AreEqual("(value -1)", GuardOutput.UnparseToString(pass.Run(input)));
        Assert.AreSequenceEqual<string>(["guard1", "guard2", "guard3", "guard4", "else"], pass.Log);
    }

    /// <summary>
    /// Verifies that scalar, sequence, and nested catamorphisms retain every extra return.
    /// </summary>
    /// <param name="input">The source term.</param>
    /// <param name="expected">The decorated term carrying the extra returns.</param>
    [TestMethod]
    [DataRow("(bundle (count 2) (name a) (count 4))",
        "(bundle (binding 1 (count 3)) (binding 0 (name a)) (binding 1 (count 5)))")]
    [DataRow("(bundle (name a))", "(bundle (binding 0 (name a)))")]
    [DataRow("(grid (((name a) (count 5)) () ((count 2))))",
        "(grid (((binding 0 (name a)) (binding 1 (count 6))) () ((binding 1 (count 3)))))")]
    public void TupleCatamorphism_MapsScalarAndNestedSequences(string input, string expected)
    {
        var pass = new TupleBindingPass();
        var output = pass.Run(TupleInput.Parse(input));

        Assert.AreEqual(SExpr.Parse(expected), TupleOutput.Unparse(output));
    }

    /// <summary>
    /// Verifies a rewrite receives two independently recursed optional children.
    /// </summary>
    /// <param name="input">The source term.</param>
    /// <param name="expected">The term with incremented children in reverse order.</param>
    [TestMethod]
    [DataRow("(pair (lit 2) (lit 5))", "(pair (lit 6) (lit 3))")]
    [DataRow("(pair #f (lit 5))", "(pair (lit 6) #f)")]
    [DataRow("(pair (lit 2) #f)", "(pair #f (lit 3))")]
    public void Recur_BindsTwoScalarOptionalArguments(string input, string expected)
    {
        var output = new SwapOptionalPairPass().Run(OptionalPairLanguage.Parse(input));

        Assert.AreEqual(expected, OptionalPairLanguage.UnparseToString(output));
    }

    /// <summary>
    /// Verifies helper rewrites are shared by automatic recursion and explicit catamorphisms.
    /// </summary>
    [TestMethod]
    public void InferredProcessor_IgnoresCallersExtraParameters()
    {
        var term = SharedHelpersInput.Parse("(pair (raw 1) (nest (raw 2)))");
        var output = new SharedHelpersPass().Run(term);

        Assert.AreEqual(
            "(pair (boxed 1) (nest (boxed 2)))", SharedHelpersOutput.UnparseToString(output));
        var helper = Assert.ContainsSingle(SharedHelpersPass.PassDescriptor.Processors
            .Where(item => item.InputKind == new Symbol("Box")));
        Assert.AreEqual(0, helper.ExtraParameterCount);
    }

    /// <summary>
    /// Verifies recursion forwards a named field and ambient parameters retain priority.
    /// </summary>
    [TestMethod]
    public void Recur_UsesNamedProductionFieldsAndAmbientParameters()
    {
        var input = ContextLanguage.Parse("(assign target old)");
        var pass = new FieldForwardingPass();

        Assert.AreEqual("(assign target target)", ContextLanguage.UnparseToString(pass.Run(input)));
    }

    /// <summary>
    /// Verifies extra return constants retain float, enum, and array values in synthesized clauses.
    /// </summary>
    [TestMethod]
    public void Recur_DefaultExtraReturnsPreserveDeclaredConstantTypes()
    {
        var input = DefaultsLanguage.Parse("(sum (lit 2))");
        var pass = new ConstantDefaultsPass();

        Assert.AreEqual("(value 10)", DefaultsLanguage.UnparseToString(pass.Run(input)));
    }

    /// <summary>
    /// Verifies camelCase [Recur] matches PascalCase properties without shadowing whole-input ir.
    /// </summary>
    [TestMethod]
    public void Recur_CamelCaseParameterMatchesPascalCaseRecordProperty()
    {
        var pipeline = Pipeline.Start<QuickStartL0.Expr>().Then(new QuickStartLowerNegation());
        QuickStartL1.Expr lowered = pipeline.Run(
            new QuickStartL0.Add(
                new QuickStartL0.Neg(new QuickStartL0.Int(3)),
                new QuickStartL0.Int(5)));

        Assert.AreEqual(
            new QuickStartL1.Add(
                new QuickStartL1.Sub(new QuickStartL1.Int(0), new QuickStartL1.Int(3)),
                new QuickStartL1.Int(5)),
            lowered);

        QuickStartL0.Expr outer = new QuickStartL0.Hold(new QuickStartL0.Int(7));
        Assert.AreEqual(outer, new WholeInputPrecedencePass().Run(outer));
    }
}

[Pass(typeof(OptionalPairLanguage), typeof(OptionalPairLanguage))]
internal sealed partial class SwapOptionalPairPass
{
    [Rewrite]
    private static OptionalPairLanguage.Expr Increment(OptionalPairLanguage.Lit node) =>
        new OptionalPairLanguage.Lit(node.N + 1);

    [Rewrite(typeof(OptionalPairLanguage.Pair))]
    private static OptionalPairLanguage.Expr Swap(
        [Recur] OptionalPairLanguage.Expr? e0, [Recur] OptionalPairLanguage.Expr? e1) =>
        new OptionalPairLanguage.Pair(e1, e0);
}

[Language]
[Terminal(typeof(Symbol), "x")]
[Terminal(typeof(long), "n")]
[NonTerminalDef("Expr", ["e"], "(num n)", "(primapp x e ...)")]
internal static partial class GuardInput;

[Language]
[Terminal(typeof(long), "n")]
[Terminal(typeof(bool), "b")]
[NonTerminalDef("Value", ["v"], "(value n)")]
[NonTerminalDef("Predicate", ["p"], "(predicate b)")]
internal static partial class GuardOutput;

[Pass(typeof(GuardInput), typeof(GuardOutput))]
internal sealed partial class GuardedPrimitivePass
{
    internal List<string> Log { get; } = [];

    [Processor("Expr", "Value")]
    private partial GuardOutput.Value Evaluate(GuardInput.Expr ir, string context = "ctx");

    [Processor("Expr", "Predicate")]
    private GuardOutput.Predicate EvaluatePredicate(GuardInput.Expr ir, string seen)
    {
        var number = (GuardInput.Num)ir.Value;
        this.Log.Add(seen + " " + number.N.ToString(CultureInfo.InvariantCulture));
        return new GuardOutput.Predicate.PredicateNode(number.N % 2 == 0);
    }

    [Rewrite(
        typeof(GuardInput.Primapp),
        Processor = nameof(Evaluate),
        Guard = nameof(Guard4),
        Order = 4)]
    private GuardOutput.Value ValueTwo([Recur] Seq<GuardOutput.Value> e) =>
        new GuardOutput.Value.ValueNode(e.Sum(
            static term =>
                ((GuardOutput.Value.ValueNode)((GuardOutput.Value.IUnionMembers)term).Value!).N));

    [Rewrite(Processor = nameof(Evaluate), Guard = nameof(Guard3), Order = 3)]
    private GuardOutput.Value PredicateTwo(
        GuardInput.Primapp node,
        Symbol x,
        string context,
        [Recur(Field = "EList", Processor = nameof(EvaluatePredicate), Arguments = ["context"])]
        Seq<GuardOutput.Predicate> children)
    {
        if (x != node.X || !string.Equals(context, "ctx", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The rewrite did not match its arguments.");
        }

        return new GuardOutput.Value.ValueNode(children.Count(static term =>
            ((GuardOutput.Predicate.PredicateNode)term.Value).B));
    }

    [Rewrite(Processor = nameof(Evaluate), Guard = nameof(Guard2), Order = 2)]
    private GuardOutput.Value ValueOne(GuardInput.Primapp node, [Recur] Seq<GuardOutput.Value> e) =>
        new GuardOutput.Value.ValueNode(e.Count);

    [Rewrite(Processor = nameof(Evaluate), Guard = nameof(Guard1), Order = 1)]
    private GuardOutput.Value PredicateOne(
        GuardInput.Primapp node,
        [Recur(Processor = nameof(EvaluatePredicate), Arguments = ["context"])]
        Seq<GuardOutput.Predicate> e) =>
        new GuardOutput.Value.ValueNode(e.Count);

    [Rewrite(Processor = nameof(Evaluate))]
    private GuardOutput.Value Number(GuardInput.Num node, string context)
    {
        if (!string.Equals(context, "ctx", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Generated recursion did not forward the context.");
        }

        this.Log.Add("value " + node.N.ToString(CultureInfo.InvariantCulture));
        return new GuardOutput.Value.ValueNode(node.N);
    }

    [Rewrite(Processor = nameof(Evaluate), IsElse = true)]
    private GuardOutput.Value Otherwise(GuardInput.Expr ir)
    {
        this.Log.Add("else");
        return new GuardOutput.Value.ValueNode(-1);
    }

    private bool Guard1(GuardInput.Primapp node) => this.Match(node, "predicate-one", "guard1");

    private bool Guard2(GuardInput.Primapp node) => this.Match(node, "value-one", "guard2");

    private bool Guard3(GuardInput.Primapp node) => this.Match(node, "predicate-two", "guard3");

    private bool Guard4(GuardInput.Primapp node, string context) =>
        string.Equals(context, "ctx", StringComparison.Ordinal)
        && this.Match(node, "value-two", "guard4");

    private bool Match(GuardInput.Primapp node, string name, string message)
    {
        this.Log.Add(message);
        return string.Equals(node.X.Name, name, StringComparison.Ordinal);
    }
}

[Language]
[Terminal(typeof(Symbol), "x")]
[Terminal(typeof(long), "n")]
[NonTerminalDef("Expr", ["e"], "(bundle t0 t1 ...)", "(grid ((t ...) ...))")]
[NonTerminalDef("Term", ["t"], "(name x)", "(count n)")]
internal static partial class TupleInput;

[Language]
[Terminal(typeof(Symbol), "x")]
[Terminal(typeof(long), "n")]
[NonTerminalDef("Expr", ["e"], "(bundle t0 t1 ...)", "(grid ((t ...) ...))")]
[NonTerminalDef("Triv", ["t"], "(name x)", "(count n)", "(binding n t)")]
internal static partial class TupleOutput;

[Pass(typeof(TupleInput), typeof(TupleOutput))]
internal sealed partial class TupleBindingPass
{
    private enum BindingState
    {
        Preserved,
        Transformed,
    }

    [Processor("Term", "Triv", DefaultExtraReturns = new object[] { BindingState.Preserved })]
    private partial (TupleOutput.Triv Term, BindingState Binding) ToTriv(TupleInput.Term ir);

    [Rewrite(Processor = nameof(ToTriv))]
    private (TupleOutput.Triv Term, BindingState Binding) Increment(TupleInput.Count node) =>
        (new TupleOutput.Count(node.N + 1), BindingState.Transformed);

    [Rewrite(typeof(TupleInput.Bundle))]
    private static TupleOutput.Expr MapBundle(
        [Recur] (TupleOutput.Triv Term, BindingState Binding) t0,
        [Recur] Seq<(TupleOutput.Triv Term, BindingState Binding)> t1) =>
        new TupleOutput.Bundle(Decorate(t0), t1.Select(Decorate));

    [Rewrite(typeof(TupleInput.Grid))]
    private static TupleOutput.Expr MapGrid(
        [Recur] Seq<Seq<(TupleOutput.Triv Term, BindingState Binding)>> t) =>
        new TupleOutput.Grid(t.Select(static row => row.Select(Decorate)));

    private static TupleOutput.Triv Decorate(
        (TupleOutput.Triv Term, BindingState Binding) result) =>
        new TupleOutput.Binding((long)result.Binding, result.Term);
}

[Language, Terminal(typeof(long), "n")]
[NonTerminalDef("Expr", ["e"], "(pair b c)")]
[NonTerminalDef("Box", ["b"], "(raw n)")]
[NonTerminalDef("Container", ["c"], "(nest b)")]
internal static partial class SharedHelpersInput;

[Language, Terminal(typeof(long), "n")]
[NonTerminalDef("Expr", ["e"], "(pair b c)")]
[NonTerminalDef("Box", ["b"], "(boxed n)")]
[NonTerminalDef("Container", ["c"], "(nest b)")]
internal static partial class SharedHelpersOutput;

[Pass(typeof(SharedHelpersInput), typeof(SharedHelpersOutput))]
internal sealed partial class SharedHelpersPass
{
    [Processor]
    private partial SharedHelpersOutput.Expr Process(SharedHelpersInput.Expr ir, long depth = 1);

    [Rewrite]
    private static SharedHelpersOutput.Box Convert(SharedHelpersInput.Raw node) =>
        new SharedHelpersOutput.Boxed(node.N);

    [Rewrite]
    private static SharedHelpersOutput.Container Nest(
        SharedHelpersInput.Nest node, [Recur] SharedHelpersOutput.Box b) =>
        new SharedHelpersOutput.Nest(b);
}

[Language, Terminal(typeof(Symbol), "x"), Terminal(typeof(long), "n")]
[NonTerminalDef("Expr", ["e"], "x", "(assign x e)")]
[NonTerminalDef("Unrelated", ["u"], "n")]
internal static partial class ContextLanguage;

[Pass(typeof(ContextLanguage), typeof(ContextLanguage))]
internal sealed partial class FieldForwardingPass
{
    private static readonly Symbol DefaultTarget = new("default");

    [Processor]
    private partial ContextLanguage.Expr Process(ContextLanguage.Expr ir, long depth = 1);

    [Processor]
    private partial ContextLanguage.Expr MapSymbol(ContextLanguage.Expr ir, Symbol target);

    [Rewrite(Processor = nameof(MapSymbol))]
    private static ContextLanguage.Expr Map(Symbol x, Symbol target) =>
        x.Name == "old" ? target : x;

    [Rewrite]
    private static ContextLanguage.Expr OnAssign(
        ContextLanguage.Assign node,
        long depth,
        [Recur(Field = "e", Processor = nameof(MapSymbol), Arguments = ["x"])]
        ContextLanguage.Expr mapped) =>
        new ContextLanguage.Assign(depth == 1 ? node.X : DefaultTarget, mapped);
}

[Language, Terminal(typeof(long), "n")]
[NonTerminalDef("Expr", ["e"], "(sum t)", "(value n)")]
[NonTerminalDef("Term", ["t"], "(lit n)")]
internal static partial class DefaultsLanguage;

[Pass(typeof(DefaultsLanguage), typeof(DefaultsLanguage))]
internal sealed partial class ConstantDefaultsPass
{
    private enum State
    {
        Default = 2,
    }

    [Processor(DefaultExtraReturns = new object[] { 1.5f, State.Default, new long[] { 3, 4 } })]
    private partial (DefaultsLanguage.Term Term, float Weight, State Status, long[] Values)
        Defaults(DefaultsLanguage.Term ir);

    [Rewrite(typeof(DefaultsLanguage.Sum))]
    private static DefaultsLanguage.Expr GetDefaults(
        [Recur] (DefaultsLanguage.Term Term, float Weight, State Status, long[] Values) t) =>
        new DefaultsLanguage.Value((long)(t.Weight * (int)t.Status) + t.Values.Sum());
}

[Language]
internal static partial class QuickStartL0
{
    [NonTerminal("e")]
    [Entry]
    public partial union Expr(Int, Add, Sub, Neg, Hold);

    public sealed record Int(int Value);

    public sealed record Add(Expr Left, Expr Right);

    public sealed record Sub(Expr Left, Expr Right);

    public sealed record Neg(Expr Operand);

    public sealed record Hold(Expr Ir);
}

[Language]
[Extends(typeof(QuickStartL0))]
[Remove(typeof(QuickStartL0.Neg))]
internal static partial class QuickStartL1;

[Pass(typeof(QuickStartL0), typeof(QuickStartL1))]
internal sealed partial class QuickStartLowerNegation
{
    [Rewrite(typeof(QuickStartL0.Neg))]
    private static QuickStartL1.Expr LowerNeg([Recur] QuickStartL1.Expr operand) =>
        new QuickStartL1.Sub(new QuickStartL1.Int(0), operand);
}

[Pass(typeof(QuickStartL0), typeof(QuickStartL0))]
internal sealed partial class WholeInputPrecedencePass
{
    [Rewrite(typeof(QuickStartL0.Hold))]
    private static QuickStartL0.Expr KeepWhole(QuickStartL0.Expr ir) => ir;
}
