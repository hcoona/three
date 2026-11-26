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

// Scenarios adapted from the MIT-licensed Nanopass Framework Scheme suite.
// Upstream copyright and license: src/public/lib/Nanopass/THIRD-PARTY-NOTICES.txt.

using System.Globalization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Hcoona.Nanopass.Tests;

/// <summary>
/// Exercises the generated conversions at both typed and raw pass boundaries.
/// </summary>
[TestClass]
public sealed class PassParserUnparserTests
{
    /// <summary>
    /// Verifies the parser or identity conversion selected for each pass input.
    /// </summary>
    [TestMethod]
    public void ParseInput_UsesTheDeclaredLanguageOrPassesThroughRawData()
    {
        SExpr ski = SExpr.Parse("((S K) I)");
        const string lc = "(lambda (x) (x x))";
        var transform = PassServices.GetDescriptor<SkiToLc>();
        var emit = PassServices.GetDescriptor<LcOut>();

        Assert.AreSame(ski, PassServices.GetDescriptor<SkiIn>().ParseInput(ski));
        Assert.AreEqual(SkiLanguage.Parse(ski),
            Assert.IsInstanceOfType<SkiLanguage.Expr>(transform.ParseInput(ski)));
        Assert.AreEqual(LambdaKernelLanguage.Parse(lc),
            Assert.IsInstanceOfType<LambdaKernelLanguage.Expr>(emit.ParseInput(lc)));
    }

    /// <summary>
    /// Verifies the unparser or raw conversion selected for each pass output.
    /// </summary>
    [TestMethod]
    public void UnparseOutput_UsesTheDeclaredLanguageOrPassesThroughRawData()
    {
        const string ski = "((S I) K)";
        const string lc = "((lambda (x) (x x)) (lambda (y) (y y)))";
        const string raw = "bob";
        var parse = PassServices.GetDescriptor<SkiIn>();
        var transform = PassServices.GetDescriptor<SkiToLc>();
        var emit = PassServices.GetDescriptor<LcOut>();

        Assert.AreEqual(ski, parse.UnparseOutputToString(SkiLanguage.Parse(ski)));
        Assert.AreEqual(lc, transform.UnparseOutputToString(LambdaKernelLanguage.Parse(lc)));
        Assert.AreEqual(raw, emit.UnparseOutputToString(new Symbol(raw)));
    }

    /// <summary>
    /// Verifies left-to-right recursion and composition of all three passes.
    /// </summary>
    [TestMethod]
    public void Pipeline_TranslatesCombinatorsToLambdaTerms()
    {
        const string input = "((K I) I)";
        const string expected =
            "(((lambda (x.0) (lambda (y.1) x.0)) (lambda (x.2) x.2)) (lambda (x.3) x.3))";
        var transform = new SkiToLc();
        var descriptor = PassServices.GetDescriptor(transform);
        var term = Assert.IsInstanceOfType<SkiLanguage.Expr>(descriptor.ParseInput(input));
        var pipeline = Pipeline.Start<SExpr>()
            .Then(new SkiIn()).Then(new SkiToLc()).Then(new LcOut());

        Assert.AreSame(SkiLanguage.Descriptor, descriptor.InputLanguage);
        Assert.AreSame(LambdaKernelLanguage.Descriptor, descriptor.OutputLanguage);
        Assert.AreEqual(expected, descriptor.UnparseOutputToString(transform.Run(term)));
        Assert.AreEqual(SExpr.Parse(expected), pipeline.Run(SExpr.Parse(input)));
    }
}

[Language]
[Terminal(typeof(Symbol), "c", Name = "ski-combinator", Predicate = nameof(IsCombinator))]
[NonTerminalDef("Expr", ["e"], "c", "(e0 e1)")]
internal static partial class SkiLanguage
{
    internal static bool IsCombinator(Symbol value) => value.Name is "S" or "K" or "I";
}

[Language]
[Terminal(typeof(Symbol), "x")]
[NonTerminalDef("Expr", ["e"], "x", "(lambda (x) e)", "(e0 e1)")]
internal static partial class LambdaKernelLanguage;

[Pass(typeof(SkiLanguage), typeof(LambdaKernelLanguage))]
internal sealed partial class SkiToLc
{
    private int next;

    [Rewrite]
    private LambdaKernelLanguage.Expr Expand(Symbol combinator)
    {
        Symbol x = this.Fresh("x");
        switch (combinator.Name)
        {
            case "I":
                return new LambdaKernelLanguage.Lambda(x, x);
            case "K":
                return new LambdaKernelLanguage.Lambda(
                    x, new LambdaKernelLanguage.Lambda(this.Fresh("y"), x));
            case "S":
                Symbol y = this.Fresh("y");
                Symbol z = this.Fresh("z");
                var body = new LambdaKernelLanguage.Implicit(
                    new LambdaKernelLanguage.Implicit(x, z),
                    new LambdaKernelLanguage.Implicit(y, z));
                return new LambdaKernelLanguage.Lambda(
                    x, new LambdaKernelLanguage.Lambda(
                        y, new LambdaKernelLanguage.Lambda(z, body)));
            default:
                throw new NanopassException($"Unrecognized combinator {combinator}.");
        }
    }

    private Symbol Fresh(string stem) =>
        new(stem + "." + (this.next++).ToString(CultureInfo.InvariantCulture));
}

[Pass(typeof(RawTerm), typeof(SkiLanguage))]
internal sealed partial class SkiIn
{
    [Processor("*", "Expr")]
    private static SkiLanguage.Expr Parse(SExpr ir)
    {
        if (ir is SExpr.Sym(var name) && SkiLanguage.IsCombinator(name))
        {
            return name;
        }

        if (ir.TryToProperList(out var elements) && elements.Count == 2)
        {
            return new SkiLanguage.Implicit(Parse(elements[0]), Parse(elements[1]));
        }

        throw new NanopassException($"Unrecognized combinator term {ir}.");
    }
}

[Pass(typeof(LambdaKernelLanguage), typeof(RawTerm))]
internal sealed partial class LcOut
{
    [Processor("Expr", "*")]
    private static SExpr Emit(LambdaKernelLanguage.Expr ir) => ir.Value switch
    {
        LambdaKernelLanguage.Lambda lambda =>
            SExpr.List(
                new SExpr.Sym("lambda"), SExpr.List(new SExpr.Sym(lambda.X)), Emit(lambda.E)),
        LambdaKernelLanguage.Implicit call => SExpr.List(Emit(call.E0), Emit(call.E1)),
        Symbol symbol => new SExpr.Sym(symbol),
        _ => throw new NanopassException($"Unrecognized lambda term {ir}."),
    };
}
