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

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Hcoona.Nanopass.Tests;

/// <summary>
/// Exercises parameter matching at each generated call boundary.
/// </summary>
[TestClass]
public sealed class ProcessorArgumentTests
{
    internal const string Foo = "I am not bar";
    internal const string Bar = "I am bar";
    internal const string Ignore = "extra stuff";

    /// <summary>
    /// Verifies context matching, fallback selection, and default arguments on inclusion calls.
    /// </summary>
    [TestMethod]
    public void SubNonterminal_P1ThroughP4_BindByNameOrUseSatisfiableFallback()
    {
        var term = ArgumentLanguage.Parse("q");

        Check("P1", Bar, new P1().Run(term));
        Check("P2", Bar, new P2().Run(term));
        Check("P3", "calling B2", new P3().Run(term));
        Check("P4", Bar, new P4().Run(term));
    }

    /// <summary>
    /// Verifies the same matching rules on calls to explicit terminal processors.
    /// </summary>
    [TestMethod]
    public void SubTerminal_P5ThroughP8_BindByNameOrUseSatisfiableFallback()
    {
        var term = ArgumentLanguage.ParseB("q");

        Check("P5", Bar, new P5().Run(term));
        Check("P6", Bar, new P6().Run(term));
        Check("P7", "calling symbol2", new P7().Run(term));
        Check("P8", Bar, new P8().Run(term));
    }

    /// <summary>
    /// Verifies name matching and optional parameters at the generated Run boundary.
    /// </summary>
    [TestMethod]
    public void PassEntry_P9ThroughP12_BindByNameOrUseSatisfiableFallback()
    {
        var term = ArgumentLanguage.Parse("q");
        Check("P9", Bar, new P9().Run(term, Foo, Bar, Ignore));
        Check("P10", Bar, new P10().Run(term, Foo, Bar, Ignore));
        Check("P11", "calling A2", new P11().Run(term, Foo, Bar, Ignore));
        Check("P12", Bar, ((IPass<ArgumentLanguage.A, ArgumentLanguage.A>)new P12()).Run(term));
    }

    private static void Check(string name, string expected, ArgumentLanguage.A actual) =>
        Assert.AreEqual(new SExpr.Sym(expected), ArgumentLanguage.Unparse(actual), name);

    private static void Check(string name, string expected, ArgumentLanguage.B actual) =>
        Assert.AreEqual(new SExpr.Sym(expected), ArgumentLanguage.Unparse(actual), name);
}

[Language]
[Terminal(typeof(Symbol), "x", Name = "symbol")]
[NonTerminalDef("A", ["a"], "b")]
[NonTerminalDef("B", ["b"], "x")]
internal static partial class ArgumentLanguage;

[Pass(typeof(ArgumentLanguage), typeof(ArgumentLanguage))]
internal sealed partial class P1
{
    [Processor("A", "A")]
    private partial ArgumentLanguage.A MapA(
        ArgumentLanguage.A ir,
        string foo = ProcessorArgumentTests.Foo,
        string bar = ProcessorArgumentTests.Bar,
        string ignore = ProcessorArgumentTests.Ignore);

    [Processor("B", "B")]
    private partial ArgumentLanguage.B MapB(ArgumentLanguage.B ir, string foo, string bar);

    [Rewrite(Processor = nameof(MapB), IsElse = true)]
    private ArgumentLanguage.B ElseB(ArgumentLanguage.B ir, string bar) => new Symbol(bar);
}

[Pass(typeof(ArgumentLanguage), typeof(ArgumentLanguage))]
internal sealed partial class P2
{
    [Processor("A", "A")]
    private partial ArgumentLanguage.A MapA(
        ArgumentLanguage.A ir,
        string foo = ProcessorArgumentTests.Foo,
        string bar = ProcessorArgumentTests.Bar,
        string ignore = ProcessorArgumentTests.Ignore);

    [Processor("B", "B")]
    private partial ArgumentLanguage.B MapB(ArgumentLanguage.B ir, string bar);

    [Rewrite(Processor = nameof(MapB), IsElse = true)]
    private ArgumentLanguage.B ElseB(ArgumentLanguage.B ir, string bar) => new Symbol(bar);
}

[Pass(typeof(ArgumentLanguage), typeof(ArgumentLanguage))]
internal sealed partial class P3
{
    [Processor("A", "A")]
    private partial ArgumentLanguage.A MapA(
        ArgumentLanguage.A ir,
        string xxfoo = ProcessorArgumentTests.Foo,
        string xxbar = ProcessorArgumentTests.Bar,
        string ignore = ProcessorArgumentTests.Ignore);

    [Processor("B", "B")]
    private partial ArgumentLanguage.B MapB(ArgumentLanguage.B ir, string foo, string bar);

    [Processor("B", "B")]
    private partial ArgumentLanguage.B MapB2(ArgumentLanguage.B ir);

    [Rewrite(Processor = nameof(MapB), IsElse = true)]
    private ArgumentLanguage.B ElseB(ArgumentLanguage.B ir, string bar) => new Symbol(bar);

    [Rewrite(Processor = nameof(MapB2), IsElse = true)]
    private ArgumentLanguage.B ElseB2(ArgumentLanguage.B ir) => new Symbol("calling B2");
}

[Pass(typeof(ArgumentLanguage), typeof(ArgumentLanguage))]
internal sealed partial class P4
{
    [Processor("A", "A")]
    private partial ArgumentLanguage.A MapA(ArgumentLanguage.A ir);

    [Processor("B", "B")]
    private partial ArgumentLanguage.B MapB(
        ArgumentLanguage.B ir,
        string foo = ProcessorArgumentTests.Foo,
        string bar = ProcessorArgumentTests.Bar);

    [Rewrite(Processor = nameof(MapB), IsElse = true)]
    private ArgumentLanguage.B ElseB(ArgumentLanguage.B ir, string bar) => new Symbol(bar);
}

[Pass(typeof(ArgumentLanguage), typeof(ArgumentLanguage), InputEntry = "B", OutputEntry = "B")]
internal sealed partial class P5
{
    [Processor("B", "B")]
    private partial ArgumentLanguage.B MapB(
        ArgumentLanguage.B ir,
        string foo = ProcessorArgumentTests.Foo,
        string bar = ProcessorArgumentTests.Bar,
        string ignore = ProcessorArgumentTests.Ignore);

    [Processor("symbol", "symbol")]
    private static Symbol MapSymbol(Symbol ir, string foo, string bar) => new(bar);
}

[Pass(typeof(ArgumentLanguage), typeof(ArgumentLanguage), InputEntry = "B", OutputEntry = "B")]
internal sealed partial class P6
{
    [Processor("B", "B")]
    private partial ArgumentLanguage.B MapB(
        ArgumentLanguage.B ir,
        string foo = ProcessorArgumentTests.Foo,
        string bar = ProcessorArgumentTests.Bar,
        string ignore = ProcessorArgumentTests.Ignore);

    [Processor("symbol", "symbol")]
    private static Symbol MapSymbol(Symbol ir, string bar) => new(bar);
}

[Pass(typeof(ArgumentLanguage), typeof(ArgumentLanguage), InputEntry = "B", OutputEntry = "B")]
internal sealed partial class P7
{
    [Processor("B", "B")]
    private partial ArgumentLanguage.B MapB(
        ArgumentLanguage.B ir,
        string foo = ProcessorArgumentTests.Foo,
        string bar = ProcessorArgumentTests.Bar,
        string ignore = ProcessorArgumentTests.Ignore);

    [Processor("symbol", "symbol")]
    private static Symbol MapSymbol(Symbol ir, string xxfoo, string xxbar) => new(xxbar);

    [Processor("symbol", "symbol")]
    private static Symbol MapSymbol2(Symbol ir) => new("calling symbol2");
}

[Pass(typeof(ArgumentLanguage), typeof(ArgumentLanguage), InputEntry = "B", OutputEntry = "B")]
internal sealed partial class P8
{
    [Processor("B", "B")]
    private partial ArgumentLanguage.B MapB(ArgumentLanguage.B ir);

    [Processor("symbol", "symbol")]
    private static Symbol MapSymbol(
        Symbol ir,
        string foo = ProcessorArgumentTests.Foo,
        string bar = ProcessorArgumentTests.Bar) => new(bar);
}

[Pass(typeof(ArgumentLanguage), typeof(ArgumentLanguage))]
internal sealed partial class P9
{
    internal partial ArgumentLanguage.A Run(
        ArgumentLanguage.A ir, string foo, string bar, string ignore);

    [Processor("A", "A")]
    private partial ArgumentLanguage.A MapA(ArgumentLanguage.A ir, string foo, string bar);

    [Rewrite(Processor = nameof(MapA), IsElse = true)]
    private ArgumentLanguage.A ElseA(ArgumentLanguage.A ir, string bar) =>
        (ArgumentLanguage.B)new Symbol(bar);
}

[Pass(typeof(ArgumentLanguage), typeof(ArgumentLanguage))]
internal sealed partial class P10
{
    internal partial ArgumentLanguage.A Run(
        ArgumentLanguage.A ir, string foo, string bar, string ignore);

    [Processor("A", "A")]
    private partial ArgumentLanguage.A MapA(ArgumentLanguage.A ir, string bar);

    [Rewrite(Processor = nameof(MapA), IsElse = true)]
    private ArgumentLanguage.A ElseA(ArgumentLanguage.A ir, string bar) =>
        (ArgumentLanguage.B)new Symbol(bar);
}

[Pass(typeof(ArgumentLanguage), typeof(ArgumentLanguage))]
internal sealed partial class P11
{
    internal partial ArgumentLanguage.A Run(
        ArgumentLanguage.A ir, string foo, string bar, string ignore);

    [Processor("A", "A")]
    private partial ArgumentLanguage.A MapA(ArgumentLanguage.A ir, string xxfoo, string xxbar);

    [Processor("A", "A")]
    private partial ArgumentLanguage.A MapA2(ArgumentLanguage.A ir);

    [Rewrite(Processor = nameof(MapA2), IsElse = true)]
    private ArgumentLanguage.A ElseA2(ArgumentLanguage.A ir) =>
        (ArgumentLanguage.B)new Symbol("calling A2");
}

[Pass(typeof(ArgumentLanguage), typeof(ArgumentLanguage))]
internal sealed partial class P12
{
    [Processor("A", "A")]
    private partial ArgumentLanguage.A MapA(
        ArgumentLanguage.A ir,
        string foo = ProcessorArgumentTests.Foo,
        string bar = ProcessorArgumentTests.Bar);

    [Rewrite(Processor = nameof(MapA), IsElse = true)]
    private ArgumentLanguage.A ElseA(ArgumentLanguage.A ir, string bar) =>
        (ArgumentLanguage.B)new Symbol(bar);
}
