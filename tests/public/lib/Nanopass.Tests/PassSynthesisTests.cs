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

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Hcoona.Nanopass.Tests;

/// <summary>
/// Tests inferred pass processors, the generated pass surface, and tracing.
/// </summary>
[TestClass]
public sealed class PassSynthesisTests
{
    /// <summary>
    /// Verifies equivalent recursive transformations using a Visit helper and a catamorphism.
    /// </summary>
    [TestMethod]
    public void LowerNegation_VisitAndRecurBothLowerNestedNegation()
    {
        var term = LoweringInput.Parse("(neg (add (lit 2) (neg (lit 3))))");
        var expected = SExpr.Parse("(sub (lit 0) (add (lit 2) (sub (lit 0) (lit 3))))");

        Assert.AreEqual(expected, LoweringOutput.Unparse(new LowerNegationVisit().Run(term)));
        Assert.AreEqual(expected, LoweringOutput.Unparse(new LowerNegationRecur().Run(term)));
    }

    /// <summary>
    /// Verifies a generated pass can form a typed pipeline stage and trace direct execution.
    /// </summary>
    [TestMethod]
    public void LowerNegation_PipelineStagesAndTraceExposeGeneratedDescriptors()
    {
        var lowering = new LowerNegationRecur();
        var identity = new IdentityPass();
        var pipeline = Pipeline.Start<LoweringInput.Expr>().Then(lowering).Then(identity);
        var input = LoweringInput.Parse("(neg (lit 5))");
        var expected = LoweringOutput.Parse("(sub (lit 0) (lit 5))");
        var descriptor = PassServices.GetDescriptor<LowerNegationRecur>();
        var events = new List<PassTraceEvent>();

        Assert.AreEqual(expected, pipeline.Run(input));
        using (PassTrace.Capture(events.Add))
        {
            Assert.AreEqual(expected, lowering.Run(input));
        }

        Assert.AreSame(descriptor, pipeline.Stages[0].Descriptor);
        Assert.AreSame(PassServices.GetDescriptor<IdentityPass>(), pipeline.Stages[1].Descriptor);
        var passEvents = events.Where(static item => item.ProcessorName is null).ToArray();
        Assert.AreSequenceEqual<PassTracePhase>(
            [PassTracePhase.Enter, PassTracePhase.Exit],
            passEvents.Select(static item => item.Phase));
        Assert.AreSame(descriptor, passEvents[0].Descriptor);
        Assert.AreEqual(input, passEvents[0].Value);
        Assert.AreEqual(expected, passEvents[1].Value);
        Assert.AreEqual("(sub (lit 0) (lit 5))", passEvents[1].TryUnparse()?.Format());
    }

    /// <summary>
    /// Checks the identity round-trip property for a reproducible sample of generated trees.
    /// </summary>
    [TestMethod]
    public void IdentityPass_RoundTripsGeneratedTerms()
    {
        var pass = new IdentityPass();
        var random = new Random(1779);
        for (int i = 0; i < 128; i++)
        {
            var input = Generate(random, i % 7);
            var parsed = LoweringOutput.Parse(input);
            var output = pass.Run(parsed);

            Assert.AreEqual(input, LoweringOutput.Unparse(output), $"Trial {i}: {input}");
            Assert.AreEqual(parsed, output, $"Trial {i}: {input}");
        }
    }

    /// <summary>
    /// Verifies synthesis maps each optional child independently.
    /// </summary>
    /// <param name="input">The source term.</param>
    /// <param name="expected">The term with present children incremented.</param>
    [TestMethod]
    [DataRow("(pair (lit 2) (lit 5))", "(pair (lit 3) (lit 6))")]
    [DataRow("(pair #f (lit 5))", "(pair #f (lit 6))")]
    [DataRow("(pair (lit 2) #f)", "(pair (lit 3) #f)")]
    public void Synthesis_MapsTwoScalarOptionalFields(string input, string expected)
    {
        var output = new MapOptionalPairPass().Run(OptionalPairLanguage.Parse(input));

        Assert.AreEqual(expected, OptionalPairLanguage.UnparseToString(output));
    }

    /// <summary>
    /// Verifies synthesis can flatten input inclusions and wrap a chain of output inclusions.
    /// </summary>
    [TestMethod]
    public void Synthesis_MapsInputAndOutputNonterminalInclusions()
    {
        var flat = new FlattenAtom().Run(IncludedInput.Parse("(var a)"));
        var nested = new IncludeTrivial().Run(flat);

        Assert.AreEqual("(var a)", FlatLanguage.UnparseToString(flat));
        Assert.AreEqual("(var a)", IncludedOutput.UnparseToString(nested));
        var processor = Assert.ContainsSingle(FlattenAtom.PassDescriptor.Processors
            .Where(item => item.InputKind == new Symbol("Atom")));
        Assert.AreEqual(new Symbol("Expr"), processor.OutputKind);
    }

    /// <summary>
    /// Verifies flattening an inclusion while a sibling inclusion remains.
    /// </summary>
    [TestMethod]
    public void Synthesis_FlattensComplexAndRetainsTriv()
    {
        var pass = new FlattenComplex();

        Assert.AreEqual("(neg (lit 2))", FlattenedComplexOutput.UnparseToString(
            pass.Run(IncludedComplexInput.Parse("(neg (lit 2))"))));
        Assert.AreEqual("(lit 3)", FlattenedComplexOutput.UnparseToString(
            pass.Run(IncludedComplexInput.Parse("(lit 3)"))));
    }

    /// <summary>
    /// Verifies an included input kind can map to multiple output inclusions.
    /// </summary>
    [TestMethod]
    public void Synthesis_SplitsAtomAcrossOutputInclusions()
    {
        var pass = new SplitAtom();

        Assert.AreEqual("(var x)", SplitAtomOutput.UnparseToString(
            pass.Run(SplitAtomInput.Parse("(var x)"))));
        Assert.AreEqual("(lit 4)", SplitAtomOutput.UnparseToString(
            pass.Run(SplitAtomInput.Parse("(lit 4)"))));
    }

    /// <summary>
    /// Verifies synthesis preserves one alternative in Atom while lifting the other to Expr.
    /// </summary>
    [TestMethod]
    public void Synthesis_PartiallyFlattensAtom()
    {
        var pass = new PartiallyFlattenAtom();
        var variable = pass.Run(SplitAtomInput.Parse("(var x)"));
        var literal = pass.Run(SplitAtomInput.Parse("(lit 4)"));

        var atom = Assert.IsInstanceOfType<PartialAtomOutput.Atom>(variable.Value);
        Assert.AreEqual(new PartialAtomOutput.Var(new Symbol("x")), atom.Value);
        Assert.AreEqual(new PartialAtomOutput.Lit(4), literal.Value);
    }

    /// <summary>
    /// Verifies an explicit processor takes precedence over a same-name inclusion.
    /// </summary>
    [TestMethod]
    public void Synthesis_PrefersExplicitAtomToExprProcessor()
    {
        var result = new RewriteIncludedAtom().Run(IncludedInput.Parse("(var original)"));

        Assert.AreEqual("(var renamed)", IncludedInput.UnparseToString(result));
    }

    /// <summary>
    /// Verifies a registered transitive destination takes precedence over structural preservation.
    /// </summary>
    [TestMethod]
    public void Synthesis_PrefersExplicitTrivToAltTrivProcessor()
    {
        var result = new RewriteTrivial().Run(IncludedComplexInput.Parse("(lit 3)"));

        Assert.AreEqual("(alternate 3)", AlternateTrivOutput.UnparseToString(result));
    }

    /// <summary>
    /// Verifies rewrite-seeded processors are called for both outer and included destinations.
    /// </summary>
    /// <param name="wrapLeaf">Whether the inferred processor returns an included Leaf.</param>
    [TestMethod]
    [DataRow(false, DisplayName = "Atom to Expr")]
    [DataRow(true, DisplayName = "Atom to Leaf")]
    public void Synthesis_InvokesRewriteSeededAtomProcessor(bool wrapLeaf)
    {
        var input = IncludedInput.Parse("(var original)");

        string result = wrapLeaf
            ? IncludedOutput.UnparseToString(new RenameAtomToLeaf().Run(input))
            : IncludedInput.UnparseToString(new RenameAtomToExpr().Run(input));

        Assert.AreEqual("(var renamed)", result);
    }

    /// <summary>
    /// Verifies exact rewrite return types select the subprocessor, not an outer union.
    /// </summary>
    [TestMethod]
    public void Rewrite_ExactReturnTypeSelectsIncludedProcessor()
    {
        var pass = new RenameIncludedVariable();
        var input = FlatLanguage.Parse("(var a)");

        Assert.AreEqual("(var a)", IncludedOutput.UnparseToString(pass.Run(input)));
        Assert.AreEqual("(var renamed)", IncludedOutput.UnparseToString(pass.RenameLeaf(input)));
    }

    /// <summary>
    /// Verifies an implicitly convertible rewrite runs on the entry without creating an orphan.
    /// </summary>
    [TestMethod]
    public void Rewrite_ConvertibleIncludedReturnBindsToEntry()
    {
        var result = new RenameEntryVariable().Run(FlatLanguage.Parse("(var a)"));

        Assert.AreEqual("(var renamed)", IncludedOutput.UnparseToString(result));
        Assert.HasCount(1, RenameEntryVariable.PassDescriptor.Processors);
    }

    /// <summary>
    /// Verifies a compatible subtype processor forwards its tuple without being called twice.
    /// </summary>
    /// <param name="useDefaults">Whether the outer processor declares defaults.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Synthesis_ForwardsCompatibleSubtypeTupleOnce(bool useDefaults)
    {
        var pass = new ForwardSubtypeTuple();
        var term = IncludedInput.Parse("(var a)");
        var (expression, count, label) = useDefaults
            ? pass.TransformWithDefaults(term, "forwarded")
            : pass.Transform(term);

        Assert.AreEqual("(var tuple)", IncludedOutput.UnparseToString(expression));
        Assert.AreEqual(1L, count);
        Assert.AreEqual("forwarded", label);
        Assert.AreEqual(1, pass.Calls);
    }

    /// <summary>
    /// Verifies a registered subtype processor can return a standalone terminal with a shared name.
    /// </summary>
    [TestMethod]
    public void Synthesis_FindsTerminalDespiteSameNamedNonterminal()
    {
        var result = new LowerAtomToTerminal().Run(IncludedInput.Parse("(var a)"));

        Assert.AreEqual(new SExpr.Sym("lowered"), TerminalInclusionOutput.Unparse(result));
    }

    /// <summary>
    /// Verifies a custom raw parser is usable directly and in typed pipelines.
    /// </summary>
    [TestMethod]
    public void Run_CustomRawBodyIsUsedByTypedInterface()
    {
        var input = SExpr.Parse("7");
        var pass = new CustomRawPass(offset: 2);
        var pipeline = Pipeline.Start<SExpr>().Then(pass).Then(new CustomRenderPass());

        Assert.AreEqual("(lit 9)", LoweringOutput.UnparseToString(pass.Run(input)));
        Assert.AreEqual(SExpr.Parse("\"(lit 9)\""), pipeline.Run(input));
    }

    /// <summary>
    /// Verifies a typed Run can omit params and select a processor with unbound params.
    /// </summary>
    [TestMethod]
    public void Run_SelectsParamsProcessorWithoutContext()
    {
        IPass<LoweringOutput.Expr, LoweringOutput.Expr> pass = new ParamsEntryProcessorPass();
        var input = LoweringOutput.Parse("(lit 9)");

        Assert.AreEqual("(lit 0)", LoweringOutput.UnparseToString(pass.Run(input)));
    }

    /// <summary>
    /// Verifies a custom Run wraps the child result without a synthesized entry processor.
    /// </summary>
    [TestMethod]
    public void Run_CustomWrapperUsesOnlyChildProcessor()
    {
        var result = new WrapRenamedVariable().Run(FlatLanguage.Parse("(var original)"));

        Assert.AreEqual("(wrap (var renamed))", WrappedVariableOutput.UnparseToString(result));
        var processor = Assert.ContainsSingle(WrapRenamedVariable.PassDescriptor.Processors);
        Assert.AreEqual(new Symbol("Expr"), processor.OutputKind);
    }

    /// <summary>
    /// Verifies an else guard receives same-type context and falls through when false.
    /// </summary>
    [TestMethod]
    public void Rewrite_GuardedElseBindsSameTypeContextAndKeepsIdentity()
    {
        var pass = new GuardedElsePass();
        var zero = LoweringOutput.Parse("(lit 0)");
        var one = LoweringOutput.Parse("(lit 1)");
        var fallback = LoweringOutput.Parse("(lit 99)");

        Assert.AreEqual(fallback, pass.Run(zero, fallback));
        Assert.AreEqual(one, pass.Run(one, fallback));
    }

    private static SExpr Generate(Random random, int depth)
    {
        if (depth == 0 || random.Next(3) == 0)
        {
            return SExpr.List(new SExpr.Sym("lit"), new SExpr.Int(random.NextInt64(-1000, 1000)));
        }

        return SExpr.List(
            new SExpr.Sym(random.Next(2) == 0 ? "add" : "sub"),
            Generate(random, depth - 1),
            Generate(random, depth - 1));
    }
}

[Language]
[Terminal(typeof(long), "n")]
[NonTerminalDef("Expr", ["e", "operand"], "(lit n)", "(add e0 e1)", "(neg operand)")]
internal static partial class LoweringInput;

[Language]
[Terminal(typeof(long), "n")]
[NonTerminalDef("Expr", ["e"], "(lit n)", "(add e0 e1)", "(sub e0 e1)")]
internal static partial class LoweringOutput;

[Pass(typeof(LoweringInput), typeof(LoweringOutput))]
internal sealed partial class LowerNegationVisit
{
    [Rewrite]
    private LoweringOutput.Expr LowerNeg(LoweringInput.Neg node) =>
        new LoweringOutput.Sub(new LoweringOutput.Lit(0), this.Visit(node.Operand));
}

[Pass(typeof(LoweringInput), typeof(LoweringOutput), Trace = true)]
internal sealed partial class LowerNegationRecur
{
    [Rewrite(typeof(LoweringInput.Neg))]
    private static LoweringOutput.Expr LowerNeg([Recur] LoweringOutput.Expr operand) =>
        new LoweringOutput.Sub(new LoweringOutput.Lit(0), operand);
}

[Pass(typeof(LoweringOutput), typeof(LoweringOutput))]
internal sealed partial class IdentityPass;

[Language, Terminal(typeof(long), "n")]
[NonTerminalDef("Expr", ["e"], "(lit n)", "(pair (maybe e0) (maybe e1))")]
internal static partial class OptionalPairLanguage;

[Pass(typeof(OptionalPairLanguage), typeof(OptionalPairLanguage))]
internal sealed partial class MapOptionalPairPass
{
    [Rewrite]
    private static OptionalPairLanguage.Expr Increment(OptionalPairLanguage.Lit node) =>
        new OptionalPairLanguage.Lit(node.N + 1);
}

[Pass(typeof(LoweringOutput), typeof(LoweringOutput))]
internal sealed partial class ParamsEntryProcessorPass
{
    public partial LoweringOutput.Expr Run(LoweringOutput.Expr ir, params int[] arguments);

    [Processor]
    private static LoweringOutput.Expr Map(LoweringOutput.Expr ir, params int[] context) =>
        context.Length == 0 ? new LoweringOutput.Lit(0) : ir;
}

[Language, Terminal(typeof(Symbol), "x")]
[NonTerminalDef("Expr", ["e"], "a")]
[NonTerminalDef("Atom", ["a"], "(var x)")]
internal static partial class IncludedInput;

[Language, Terminal(typeof(Symbol), "x")]
[NonTerminalDef("Expr", ["e"], "(var x)")]
internal static partial class FlatLanguage;

[Language, Terminal(typeof(Symbol), "x")]
[NonTerminalDef("Value", ["v"], "t")]
[NonTerminalDef("Triv", ["t"], "u")]
[NonTerminalDef("Leaf", ["u"], "(var x)")]
internal static partial class IncludedOutput;

[Pass(typeof(IncludedInput), typeof(FlatLanguage))]
internal sealed partial class FlattenAtom;

[Pass(typeof(FlatLanguage), typeof(IncludedOutput))]
internal sealed partial class IncludeTrivial;

[Language, Terminal(typeof(long), "n")]
[NonTerminalDef("Expr", ["e"], "t", "c")]
[NonTerminalDef("Triv", ["t"], "(lit n)")]
[NonTerminalDef("Complex", ["c"], "(neg t)")]
internal static partial class IncludedComplexInput;

[Language, Terminal(typeof(long), "n")]
[NonTerminalDef("Expr", ["e"], "t", "(neg t)")]
[NonTerminalDef("Triv", ["t"], "(lit n)")]
internal static partial class FlattenedComplexOutput;

[Pass(typeof(IncludedComplexInput), typeof(FlattenedComplexOutput))]
internal sealed partial class FlattenComplex;

[Language, Terminal(typeof(Symbol), "x"), Terminal(typeof(long), "n")]
[NonTerminalDef("Expr", ["e"], "a")]
[NonTerminalDef("Atom", ["a"], "(var x)", "(lit n)")]
internal static partial class SplitAtomInput;

[Language, Terminal(typeof(Symbol), "x"), Terminal(typeof(long), "n")]
[NonTerminalDef("Expr", ["e"], "v", "l")]
[NonTerminalDef("VarAtom", ["v"], "(var x)")]
[NonTerminalDef("LitAtom", ["l"], "(lit n)")]
internal static partial class SplitAtomOutput;

[Pass(typeof(SplitAtomInput), typeof(SplitAtomOutput))]
internal sealed partial class SplitAtom;

[Language, Terminal(typeof(Symbol), "x"), Terminal(typeof(long), "n")]
[NonTerminalDef("Expr", ["e"], "a", "(lit n)")]
[NonTerminalDef("Atom", ["a"], "(var x)")]
internal static partial class PartialAtomOutput;

[Pass(typeof(SplitAtomInput), typeof(PartialAtomOutput))]
internal sealed partial class PartiallyFlattenAtom;

[Pass(typeof(IncludedInput), typeof(IncludedInput))]
internal sealed partial class RewriteIncludedAtom
{
    [Processor("Atom", "Expr")]
    private static IncludedInput.Expr RewriteAtom(IncludedInput.Atom ir) =>
        (IncludedInput.Atom)new IncludedInput.Var(new Symbol("renamed"));
}

[Language, Terminal(typeof(long), "n")]
[NonTerminalDef("Expr", ["e"], "t", "w", "(neg t)")]
[NonTerminalDef("Triv", ["t"], "(lit n)")]
[NonTerminalDef("Wrapper", ["w"], "a")]
[NonTerminalDef("AltTriv", ["a"], "(alternate n)")]
internal static partial class AlternateTrivOutput;

[Pass(typeof(IncludedComplexInput), typeof(AlternateTrivOutput))]
internal sealed partial class RewriteTrivial
{
    [Processor("Triv", "AltTriv")]
    private static AlternateTrivOutput.AltTriv Rewrite(IncludedComplexInput.Triv ir) =>
        ir.Value is IncludedComplexInput.Lit literal
            ? new AlternateTrivOutput.Alternate(literal.N)
            : throw new InvalidOperationException();
}

[Pass(typeof(IncludedInput), typeof(IncludedInput))]
internal sealed partial class RenameAtomToExpr
{
    [Rewrite]
    private static IncludedInput.Expr Rename(IncludedInput.Var node) =>
        (IncludedInput.Atom)new IncludedInput.Var(new Symbol("renamed"));
}

[Pass(typeof(IncludedInput), typeof(IncludedOutput))]
internal sealed partial class RenameAtomToLeaf
{
    [Rewrite]
    private static IncludedOutput.Leaf Rename(IncludedInput.Var node) =>
        new IncludedOutput.Var(new Symbol("renamed"));
}

[Pass(typeof(FlatLanguage), typeof(IncludedOutput))]
internal sealed partial class RenameIncludedVariable
{
    [Processor]
    internal partial IncludedOutput.Triv RenameLeaf(FlatLanguage.Expr ir);

    [Rewrite]
    private static IncludedOutput.Triv Rename(FlatLanguage.Var node) =>
        (IncludedOutput.Leaf)new IncludedOutput.Var(new Symbol("renamed"));
}

[Pass(typeof(FlatLanguage), typeof(IncludedOutput))]
internal sealed partial class RenameEntryVariable
{
    [Rewrite]
    private static IncludedOutput.Triv Rename(FlatLanguage.Var node) =>
        (IncludedOutput.Leaf)new IncludedOutput.Var(new Symbol("renamed"));
}

[Pass(typeof(IncludedInput), typeof(IncludedOutput))]
internal sealed partial class ForwardSubtypeTuple
{
    internal int Calls { get; private set; }

    [Processor]
    internal partial (IncludedOutput.Value, long, string) Transform(IncludedInput.Expr ir);

    [Processor(DefaultExtraReturns = new object[] { 0L, "default" })]
    internal partial (IncludedOutput.Value, long, string) TransformWithDefaults(
        IncludedInput.Expr ir, string label);

    [Processor]
    private (IncludedOutput.Leaf, int, string) Forward(
        IncludedInput.Atom ir, string label = "forwarded") =>
        (new IncludedOutput.Var(new Symbol("tuple")), ++this.Calls, label);

    [Processor]
    private static (IncludedOutput.Leaf, string, string) Incompatible(IncludedInput.Atom ir) =>
        throw new NotSupportedException();

    [Processor]
    private static (IncludedOutput.Leaf, int) WrongArity(IncludedInput.Atom ir) =>
        throw new NotSupportedException();

    [Processor]
    private static IncludedOutput.Leaf NoExtras(IncludedInput.Atom ir) =>
        throw new NotSupportedException();
}

[Language, Terminal(typeof(Symbol), "x", Name = "Symbol")]
[NonTerminalDef("Expr", ["e"], "s", "x")]
[NonTerminalDef("Symbol", ["s"], "(other)")]
internal static partial class TerminalInclusionOutput;

[Pass(typeof(IncludedInput), typeof(TerminalInclusionOutput))]
internal sealed partial class LowerAtomToTerminal
{
    [Processor("Atom", "Symbol")]
    private static Symbol Lower(IncludedInput.Atom ir) => new("lowered");
}

[Pass(typeof(RawTerm), typeof(LoweringOutput))]
internal sealed partial class CustomRawPass(long offset)
{
    internal LoweringOutput.Expr Run(SExpr ir) =>
        new LoweringOutput.Lit(((SExpr.Int)ir).Value + offset);
}

[Pass(typeof(LoweringOutput), typeof(RawTerm))]
internal sealed partial class CustomRenderPass
{
    internal static SExpr.Str Run(LoweringOutput.Expr ir) =>
        new(LoweringOutput.UnparseToString(ir));
}

[Language, Terminal(typeof(Symbol), "x")]
[NonTerminalDef("Wrapper", ["w"], "(wrap e)")]
[NonTerminalDef("Expr", ["e"], "(var x)")]
internal static partial class WrappedVariableOutput;

[Pass(typeof(FlatLanguage), typeof(WrappedVariableOutput))]
internal sealed partial class WrapRenamedVariable
{
    internal WrappedVariableOutput.Wrapper Run(FlatLanguage.Expr ir) =>
        new WrappedVariableOutput.Wrap(this.RenameExpr(ir));

    [Processor]
    private partial WrappedVariableOutput.Expr RenameExpr(FlatLanguage.Expr ir);

    [Rewrite]
    private static WrappedVariableOutput.Expr Rename(FlatLanguage.Var node) =>
        new WrappedVariableOutput.Var(new Symbol("renamed"));
}

[Pass(typeof(LoweringOutput), typeof(LoweringOutput))]
internal sealed partial class GuardedElsePass
{
    internal partial LoweringOutput.Expr Run(LoweringOutput.Expr ir, LoweringOutput.Expr fallback);

    [Processor]
    private partial LoweringOutput.Expr Process(
        LoweringOutput.Expr ir, LoweringOutput.Expr fallback);

    [Rewrite(IsElse = true, Guard = nameof(HasFallback))]
    private static LoweringOutput.Expr Else(LoweringOutput.Expr ir, LoweringOutput.Expr fallback) =>
        fallback;

    private static bool HasFallback(LoweringOutput.Expr ir, LoweringOutput.Expr fallback) =>
        ir.Value is LoweringOutput.Lit { N: 0 } && fallback.Value is LoweringOutput.Lit { N: 99 };
}
