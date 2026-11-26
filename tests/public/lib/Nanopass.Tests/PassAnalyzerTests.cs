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

// Regression scenarios adapted from the MIT-licensed Nanopass Framework Racket suite.
// Upstream copyright and license: src/public/lib/Nanopass/THIRD-PARTY-NOTICES.txt.

using System.Collections.Immutable;
using System.Runtime.Loader;
using Hcoona.Nanopass.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Hcoona.Nanopass.Tests;

/// <summary>
/// Tests pass diagnostics and the application of actual Roslyn code actions.
/// </summary>
/// <param name="testContext">The current test run's cancellation and context.</param>
[TestClass]
public sealed class PassAnalyzerTests(TestContext testContext)
{
    private const string BasicGrammar = """
        [Language, Terminal(typeof(long), "n")]
        [NonTerminalDef("Expr", ["e"], "(lit n)")]
        public static partial class Basic;
        """;

    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Preview);
    private static readonly CSharpCompilationOptions CompilationOptions = new(
        OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable);
    private static readonly ImmutableArray<MetadataReference> PlatformReferences =
    [
        .. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Append(typeof(SExpr).Assembly.Location)
            .Distinct(StringComparer.Ordinal)
            .Select(static path => MetadataReference.CreateFromFile(path)),
    ];

    /// <summary>
    /// Verifies that the analyzer identifies a missing rewrite and a fix adds it.
    /// </summary>
    /// <returns>A task that completes after the edited document is compiled.</returns>
    [TestMethod]
    public async Task MissingRewrite_SingleFixCompilesWithoutCascadingErrors()
    {
        using var workspace = new AdhocWorkspace();
        Document document = CreateDocument(workspace, MissingRewriteSource("(neg e)"));
        Diagnostic diagnostic = Assert.ContainsSingle(await this.GetDiagnosticsAsync(document));
        Assert.AreEqual("NP1001", diagnostic.Id);
        Assert.Contains("LowerNegation", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("neg", diagnostic.GetMessage(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Basic", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Contains("Expr", diagnostic.GetMessage(), StringComparison.Ordinal);

        var provider = new MissingRewriteRuleCodeFixProvider();
        CodeAction action = await this.GetCodeActionAsync(provider, document, diagnostic);
        Document fixedDocument = await this.ApplyAsync(document, action);

        Assert.IsEmpty(await this.GetDiagnosticsAsync(fixedDocument));
        Assert.HasCount(1, await this.FindRewriteStubsAsync(fixedDocument));
    }

    /// <summary>
    /// Verifies that BatchFixer inserts both missing rewrite stubs into a single pass class.
    /// </summary>
    /// <returns>A task that completes after the batch-edited document is compiled.</returns>
    [TestMethod]
    public async Task MissingRewrite_BatchFixerRepairsAllMissingProductions()
    {
        using var workspace = new AdhocWorkspace();
        Document document = CreateDocument(
            workspace, MissingRewriteSource("(neg e)", "(pos e)"));
        ImmutableArray<Diagnostic> diagnostics = await this.GetDiagnosticsAsync(document);
        Assert.HasCount(2, diagnostics);
        Assert.IsTrue(diagnostics.All(static diagnostic => diagnostic.Id == "NP1001"));
        var provider = new MissingRewriteRuleCodeFixProvider();
        var actions = new List<CodeAction>();
        await provider.RegisterCodeFixesAsync(new CodeFixContext(
            document,
            diagnostics[0].Location.SourceSpan,
            diagnostics,
            (action, _) => actions.Add(action),
            testContext.CancellationToken));
        CodeAction action = Assert.ContainsSingle(actions);
        var fixAll = provider.GetFixAllProvider();
        Assert.AreSame(WellKnownFixAllProviders.BatchFixer, fixAll);
        Assert.IsNotNull(fixAll);
        Assert.IsNotNull(action.EquivalenceKey);
        var context = new FixAllContext(
            document, provider, FixAllScope.Document, action.EquivalenceKey,
            provider.FixableDiagnosticIds, new FixAllDiagnostics(diagnostics),
            testContext.CancellationToken);

        CodeAction? batch = await fixAll.GetFixAsync(context);
        Assert.IsNotNull(batch);
        Document fixedDocument = await this.ApplyAsync(document, batch);

        Assert.IsEmpty(await this.GetDiagnosticsAsync(fixedDocument));
        var stubs = await this.FindRewriteStubsAsync(fixedDocument);
        Assert.HasCount(2, stubs);
        Assert.HasCount(
            2, stubs.Select(static method => method.Identifier.ValueText)
                .Distinct(StringComparer.Ordinal));
    }

    /// <summary>
    /// Verifies malformed attribute targets report a diagnostic without crashing.
    /// </summary>
    /// <param name="declaration">The invalid attribute target.</param>
    /// <returns>A task that completes after analyzer execution.</returns>
    [TestMethod]
    [DataRow("public partial struct Broken;")]
    [DataRow("public partial record Broken;")]
    [DataRow("public partial interface Broken;")]
    [DataRow("public enum Broken { One }")]
    [DataRow("public delegate void Broken();")]
    public async Task PassOnNonClass_ReportsInvalidPass(string declaration)
    {
        var compilation = this.Compile(
            "MalformedPass", BasicGrammar + "[Pass(typeof(Basic), typeof(Basic))]" + declaration);
        var driver = CSharpGeneratorDriver.Create(
            [new NanopassGenerator().AsSourceGenerator()], parseOptions: ParseOptions);
        driver = (CSharpGeneratorDriver)driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var output,
            out var generationDiagnostics,
            testContext.CancellationToken);

        Assert.IsEmpty(generationDiagnostics, Describe(generationDiagnostics));
        Assert.HasCount(1, driver.GetRunResult().GeneratedTrees);
        var diagnostics = await WithPassAnalyzer(output)
            .GetAnalyzerDiagnosticsAsync(testContext.CancellationToken);
        Assert.AreEqual("NP1003", Assert.ContainsSingle(diagnostics).Id);
    }

    /// <summary>
    /// Verifies edits with no impact on the pass signature do not rerun synthesis or emission.
    /// </summary>
    [TestMethod]
    public void PassPipeline_IgnoresUnrelatedAndMethodBodyEdits()
    {
        const string pass = """
            [Pass(typeof(Basic), typeof(Basic))]
            public partial class Identity
            {
                [Rewrite]
                private Basic.Expr Keep(Basic.Lit node) => new Basic.Lit(node.N + 1);
            }
            """;
        var compilation = this.Compile("IncrementalPass", BasicGrammar + pass);
        var passTree = Assert.ContainsSingle(compilation.SyntaxTrees);
        var otherTree = Parse("public class Other { public int Get() => 0; }", "Other.cs");
        compilation = compilation.AddSyntaxTrees(otherTree);
        var options = new GeneratorDriverOptions(
            IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new NanopassGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions,
            driverOptions: options);
        driver = driver.RunGenerators(compilation, testContext.CancellationToken);

        var otherReplacement = Parse(
            "public class Other { public int Get() => 1337; }", otherTree.FilePath);
        var otherEdit = compilation.ReplaceSyntaxTree(otherTree, otherReplacement);
        Assert.AreEqual(IncrementalStepRunReason.Cached, AssertCached(otherEdit));
        var bodyTree = Parse(
            "using Hcoona.Nanopass;\n" + BasicGrammar + pass.Replace(
                "node.N + 1", "node.N * 75", StringComparison.Ordinal), passTree.FilePath);
        var bodyEdit = otherEdit.ReplaceSyntaxTree(passTree, bodyTree);
        AssertCached(bodyEdit);
        var shiftedTree = Parse("\n\n" + bodyTree.ToString(), bodyTree.FilePath);
        var shifted = bodyEdit.ReplaceSyntaxTree(bodyTree, shiftedTree);
        AssertCached(shifted);
        var resolution = Assert.ContainsSingle(Assert.ContainsSingle(
            driver.GetRunResult().Results.Single().TrackedSteps["LanguageResolution"]).Outputs);
        Assert.AreEqual(IncrementalStepRunReason.Cached, resolution.Reason);

        var otherGrammar = BasicGrammar.Replace("Basic", "Other", StringComparison.Ordinal);
        var grammarTree = Parse("using Hcoona.Nanopass;\n" + otherGrammar, otherTree.FilePath);
        var grammarEdit = shifted.ReplaceSyntaxTree(otherReplacement, grammarTree);
        AssertCached(grammarEdit);
        object identity = Assert.ContainsSingle(driver.GetRunResult().Results.Single()
            .TrackedSteps["PassInputs"].SelectMany(static step => step.Outputs)
            .Where(static output => output.Reason != IncrementalStepRunReason.Removed)).Value;

        var inserted = Parse(grammarTree.ToString() + """
            [Pass(typeof(Other), typeof(Other))]
            public partial class Inserted;
            """, grammarTree.FilePath);
        var insertion = grammarEdit.ReplaceSyntaxTree(grammarTree, inserted);
        driver = driver.RunGenerators(insertion, testContext.CancellationToken);
        var emitted = driver.GetRunResult().Results.Single().TrackedSteps["PassEmission"]
            .SelectMany(static step => step.Outputs)
            .Where(static item => item.Reason != IncrementalStepRunReason.Removed)
            .Select(static output =>
                (Source: ((string Hint, string Source))output.Value, output.Reason));
        Assert.AreEqual(IncrementalStepRunReason.Cached,
            Assert.ContainsSingle(emitted.Where(static output =>
                output.Source.Hint == "Identity.NanopassPass.g.cs")).Reason);

        var editedInsertion = Parse(inserted.ToString().Replace(
            "[Pass(typeof(Other), typeof(Other))]",
            "[Pass(typeof(Other), typeof(Other), Trace = true)]",
            StringComparison.Ordinal), inserted.FilePath);
        driver = driver.RunGenerators(insertion.ReplaceSyntaxTree(inserted, editedInsertion),
            testContext.CancellationToken);
        var inputs = driver.GetRunResult().Results.Single().TrackedSteps["PassInputs"]
            .SelectMany(static step => step.Outputs)
            .Where(static output => output.Reason != IncrementalStepRunReason.Removed).ToArray();
        Assert.HasCount(2, inputs);
        Assert.AreEqual(IncrementalStepRunReason.Cached,
            Assert.ContainsSingle(
                inputs.Where(output => ReferenceEquals(identity, output.Value))).Reason);

        SyntaxTree Parse(string source, string path) =>
            CSharpSyntaxTree.ParseText(
                source, ParseOptions, path, cancellationToken: testContext.CancellationToken);

        IncrementalStepRunReason AssertCached(Compilation updated)
        {
            driver = driver.RunGeneratorsAndUpdateCompilation(
                updated, out var output, out var diagnostics, testContext.CancellationToken);
            Assert.IsEmpty(diagnostics, Describe(diagnostics));
            Assert.IsEmpty(output.GetDiagnostics(testContext.CancellationToken)
                .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
            var steps = Assert.ContainsSingle(driver.GetRunResult().Results).TrackedSteps;
            var input = Assert.ContainsSingle(steps["PassInputs"]
                .SelectMany(static step => step.Outputs)
                .Where(static item => item.Reason != IncrementalStepRunReason.Removed));
            IncrementalStepRunReason[] stableReasons =
                [IncrementalStepRunReason.Cached, IncrementalStepRunReason.Unchanged];
            Assert.Contains(input.Reason, stableReasons);
            foreach (string step in new[] { "PassResolution", "PassEmission" })
            {
                Assert.AreEqual(IncrementalStepRunReason.Cached,
                    Assert.ContainsSingle(steps[step].SelectMany(static item => item.Outputs)
                        .Where(static item => item.Reason != IncrementalStepRunReason.Removed))
                        .Reason,
                    step);
            }

            return input.Reason;
        }
    }

    /// <summary>Resolves generated language types for nested and imported passes.</summary>
    /// <returns>A task that completes after the generated compilation is analyzed.</returns>
    [TestMethod]
    public async Task PassInput_ResolvesNestedAndImportedTypesAndNullableDefaults()
    {
        const string source = """
            namespace Demo;

            using GrammarNamespace = Demo;
            using Neg = global::Demo.L0.Neg;
            using static Demo.L0;

            [Language, Terminal(typeof(long), "n")]
            [NonTerminalDef("Expr", ["e"], "(lit n)", "(neg e)")]
            public static partial class L0
            {
                [Pass(typeof(L0), typeof(L0))]
                public partial class Nested
                {
                    [Processor(DefaultExtraReturns = new object?[] { null })]
                    private partial (Expr Term, Expr? Extra) Lower(Expr __np_val);

                    [Rewrite(typeof(Neg))]
                    private static (Expr, Expr?) LowerNeg(Neg node) => (node.E, null);
                }
            }

            [Pass(typeof(L0), typeof(L0))]
            public partial class Imported
            {
                [Processor(DefaultExtraReturns = new object?[] { null })]
                private partial (Expr Term, Expr? Extra) Lower(GrammarNamespace::L0.Expr __np_node);

                [Rewrite]
                private static (Expr, Expr?) LowerNeg(Neg node) => (node.E, null);

                public static string Check() =>
                    UnparseToString(new Imported().Run(Parse("(neg (lit 2))")));
            }
            """;
        var output = this.Generate(this.Compile("ScopedPass", source));
        var diagnostics = await WithPassAnalyzer(output)
            .GetAnalyzerDiagnosticsAsync(testContext.CancellationToken);
        Assert.IsEmpty(diagnostics, Describe(diagnostics));

        var loader = new AssemblyLoadContext("ScopedPass", isCollectible: true);
        try
        {
            using var stream = new MemoryStream(this.Emit(output));
            var imported = loader.LoadFromStream(stream)
                .GetType("Demo.Imported", throwOnError: true)!;
            Assert.AreEqual("(lit 2)", imported.GetMethod("Check")!.Invoke(null, null));
        }
        finally
        {
            loader.Unload();
        }
    }

    /// <summary>Validates generated defaults and adapts a Run with omittable params.</summary>
    /// <param name="paramsType">The type of the array or collection parameter.</param>
    /// <returns>A task that completes after the generated compilation is analyzed.</returns>
    [TestMethod]
    [DataRow("int[]")]
    [DataRow("System.ReadOnlySpan<int>")]
    public async Task PassInput_AcceptsGeneratedDefaultsAndOmittableParams(string paramsType)
    {
        string source = $$"""
            [Language, Terminal(typeof(long), "n")]
            [NonTerminalDef("Expr", ["e"], "n", "(lit n)")]
            public static partial class L0;

            [Pass(typeof(L0), typeof(L0))]
            public partial class Lowering
            {
                public partial L0.Expr Run(L0.Expr node, params {{paramsType}} context);

                [Processor(DefaultExtraReturns = new object?[] { null, 2L })]
                private partial (L0.Expr Term, L0.Lit? Record, L0.Expr Union) Lower(L0.Expr node);

                public static string Check()
                {
                    var pass = new Lowering();
                    var input = L0.Parse("(lit 1)");
                    var output = Pipeline.Start<L0.Expr>().Then(pass).Run(input);
                    var (_, record, union) = pass.Lower(input);
                    return $"{L0.UnparseToString(output)}|{record is null}|"
                        + $"{L0.UnparseToString(union)}";
                }
            }
            """;
        var output = this.Generate(this.Compile("GeneratedDefaultsAndParams", source));
        var diagnostics = await WithPassAnalyzer(output)
            .GetAnalyzerDiagnosticsAsync(testContext.CancellationToken);
        Assert.IsEmpty(diagnostics, Describe(diagnostics));

        Assert.AreEqual("(lit 1)|True|2", this.InvokeCheck(output, "Lowering"));
    }

    /// <summary>
    /// Reads inherited language metadata without binding syntax from a referenced compilation.
    /// </summary>
    [TestMethod]
    public async Task PassAnalyzer_ResolvesCompilationReferenceBaseLanguage()
    {
        var parent = this.Generate(this.Compile("ParentLanguage", BasicGrammar));
        var child = this.Compile("ChildLanguage", """
            [Language, Extends(typeof(Basic))]
            public static partial class Child;

            [Pass(typeof(Child), typeof(Child))]
            public partial class Identity;
            """, parent.ToMetadataReference());

        foreach (var compilation in new[] { child, this.Generate(child) })
        {
            var diagnostics = await WithPassAnalyzer(compilation)
                .GetAnalyzerDiagnosticsAsync(testContext.CancellationToken);
            Assert.IsEmpty(diagnostics, Describe(diagnostics));
        }
    }

    /// <summary>
    /// Selects a compatible processor when several have the same endpoints and tuple arity.
    /// </summary>
    /// <param name="countType">
    /// The exact or implicitly convertible extra return type expected by Recur.
    /// </param>
    [TestMethod]
    [DataRow("int")]
    [DataRow("long")]
    public async Task Recur_SelectsProcessorByExtraReturnType(string countType)
    {
        string source = $$"""
            [Language, Terminal(typeof(long), "n")]
            [NonTerminalDef("Expr", ["e"], "(lit n)", "(wrap e ...)")]
            public static partial class L0;

            [Pass(typeof(L0), typeof(L0))]
            public partial class Lowering
            {
                public L0.Expr Run(L0.Expr node) => Lower(node);

                [Processor]
                private partial L0.Expr Lower(L0.Expr node);

                [Processor]
                private static (L0.Expr, int) WithCount(L0.Expr node) => (node, 42);

                [Processor]
                private static (L0.Expr, string) WithText(L0.Expr node) => (node, "wrong");

                [Rewrite(typeof(L0.Wrap), Processor = nameof(Lower))]
                private static L0.Expr Wrap(
                    [Recur(Field = "EList")] Seq<(L0.Expr Term, {{countType}} Count)> items)
                    => new L0.Lit(items[0].Count);

                public static string Check() =>
                    L0.UnparseToString(new Lowering().Run(L0.Parse("(wrap (lit 2))")));
            }
            """;
        var output = this.Generate(this.Compile("RecurExtraReturns", source));
        var diagnostics = await WithPassAnalyzer(output)
            .GetAnalyzerDiagnosticsAsync(testContext.CancellationToken);
        Assert.IsEmpty(diagnostics, Describe(diagnostics));
        Assert.AreEqual("(lit 42)", this.InvokeCheck(output, "Lowering"));
    }

    /// <summary>
    /// Forwards type-compatible extras when synthesizing a nonterminal inclusion.
    /// </summary>
    [TestMethod]
    public async Task Inclusion_SelectsProcessorByExtraReturnType()
    {
        var output = this.Generate(this.Compile("InclusionExtraReturns", """
            [Language, Terminal(typeof(long), "n")]
            [NonTerminalDef("Expr", ["e"], "t")]
            [NonTerminalDef("Term", ["t"], "(lit n)")]
            public static partial class L0;

            [Pass(typeof(L0), typeof(L0))]
            public partial class Lowering
            {
                [Processor]
                private partial (L0.Expr, long Count) Lower(L0.Expr node);

                [Processor]
                private static (L0.Term, int) WithCount(L0.Term node) => (node, 42);

                [Processor]
                private static (L0.Term, string) WithText(L0.Term node) => (node, "wrong");

                public static long Check() => new Lowering().Lower(L0.Parse("(lit 2)")).Count;
            }
            """));
        var diagnostics = await WithPassAnalyzer(output)
            .GetAnalyzerDiagnosticsAsync(testContext.CancellationToken);
        Assert.IsEmpty(diagnostics, Describe(diagnostics));
        Assert.AreEqual(42L, this.InvokeCheck(output, "Lowering"));
    }

    [TestMethod]
    [DataRow("(L0.Lit, int)?", "(L0.Expr, long)?", "(new L0.Lit(7), 42)", "extra?.Item2 ?? -1")]
    [DataRow("(string, int)?", "(string, long)?", "(\"safe\", 42)", "extra?.Item2 ?? -1")]
    [DataRow(
        "List<Lit>", "IEnumerable<L0.Lit>", "[new Lit(42)]",
        "System.Linq.Enumerable.Single(extra).N")]
    [DataRow("List<Lit>?", "IEnumerable<L0.Lit>?", "null", "extra is null ? 42 : -1")]
    [DataRow("Bucket<Lit>", "List<L0.Lit>", "new() { new Lit(42) }", "extra[0].N")]
    [DataRow("LitBucket", "List<L0.Lit>", "new() { new Lit(42) }", "extra[0].N")]
    [DataRow("L0.Lit[]", "IReadOnlyList<L0.Lit?>", "[new L0.Lit(42)]", "extra[0]?.N ?? -1")]
    [DataRow("IComparer<object>", "IComparer<L0.Lit>", "Comparer<object>.Create((_, _) => 42)",
        "extra.Compare(new L0.Lit(1), new L0.Lit(2))")]
    [DataRow(
        "IComparer<System.IComparable>", "IComparer<string>",
        "Comparer<System.IComparable>.Create((_, _) => 42)",
        "extra.Compare(\"a\", \"b\")")]
    [DataRow("L0.Lit[]", "IList<object>", "[new L0.Lit(42)]", "((L0.Lit)extra[0]).N")]
    [DataRow("L0.Lit[]?", "IList<object>?", "[new L0.Lit(42)]", "(extra?[0] as L0.Lit)?.N ?? -1")]
    [DataRow("L0.Lit[]", "object[]", "[new L0.Lit(42)]", "((L0.Lit)extra[0]).N")]
    [DataRow("L0.Lit[]", "L0.Lit?[]", "[new L0.Lit(42)]", "extra[0]?.N ?? -1")]
    [DataRow("L0.Lit[,]", "object[,]", "new[,] { { new L0.Lit(42) } }", "((L0.Lit)extra[0, 0]).N")]
    [DataRow(
        "List<string>", "IEnumerable<string>", "[\"safe\"]",
        "System.Linq.Enumerable.Single(extra) == \"safe\" ? 42 : -1")]
    [DataRow(
        "Bucket<string>", "List<string>", "new() { \"safe\" }", "extra[0] == \"safe\" ? 42 : -1")]
    [DataRow(
        "List<string>", "Converted<string>", "[\"safe\"]", "extra.Value == \"safe\" ? 42 : -1")]
    [DataRow("string[]", "object[]", "[\"safe\"]", "extra[0] is \"safe\" ? 42 : -1")]
    [DataRow("LitPayload", "Payload<L0.Lit>", "new(new Lit(42))", "extra.Value.N")]
    [DataRow("LitPayload", "IPayload<L0.Lit>", "new(new Lit(42))", "extra.Value.N")]
    [DataRow(
        "List<L0.Lit>", "IEnumerable<object>", "[new L0.Lit(42)]",
        "((L0.Lit)System.Linq.Enumerable.Single(extra)).N")]
    [DataRow(
        "List<L0.Expr?>", "IEnumerable<L0.Expr?>", "[(L0.Expr)new L0.Lit(42)]",
        "System.Linq.Enumerable.Single(extra)?.Value is L0.Lit lit ? lit.N : -1")]
    public async Task Inclusion_SelectsProcessorByStructuralExtraConversion(
        string actualExtra, string expectedExtra, string value, string check)
    {
        var compilation = this.Compile("StructuralInclusionExtra", $$"""
            using System.Collections.Generic;
            using static L0;

            public class Bucket<T> : List<T>;
            public sealed class LitBucket : Bucket<Lit>;
            public interface IPayload<T> { T Value { get; } }
            public class Payload<T>(T value) { public T Value { get; } = value; }
            public sealed class LitPayload(Lit value) : Payload<Lit>(value), IPayload<Lit>;
            public sealed class Converted<T>(T value) : Payload<T>(value)
            {
                public static implicit operator Converted<T>(List<T> items) => new(items[0]);
            }

            [Language, Terminal(typeof(long), "n")]
            [NonTerminalDef("Expr", ["e"], "t", "(lit n)")]
            [NonTerminalDef("Term", ["t"], "(term n)")]
            public static partial class L0;

            [Pass(typeof(L0), typeof(L0))]
            public partial class Lowering
            {
                [Processor]
                private partial (L0.Expr, {{expectedExtra}}) Lower(L0.Expr node);

                [Processor]
                private static (L0.Term, {{actualExtra}}) Compatible(L0.Term node) =>
                    (node, {{value}});

                [Processor]
                private static (L0.Term, (string?, int)?) NullableText(L0.Term node) =>
                    (node, (null, 1));

                [Processor]
                private static (L0.Term, List<string?>) NullableList(L0.Term node) =>
                    (node, [null]);

                [Processor]
                private static (L0.Term, string?[]) NullableArray(L0.Term node) => (node, [null]);

                [Processor]
                private static (L0.Term, List<L0.Expr>) UnionList(L0.Term node) => (node, []);

                [Rewrite(Processor = nameof(Lower))]
                private static (L0.Expr, {{expectedExtra}}) RewriteLit(L0.Lit node) =>
                    (node, default!);

                public static long Check()
                {
                    var (_, extra) = new Lowering().Lower(L0.Parse("(term 2)"));
                    return {{check}};
                }
            }
            """);
        var output = this.Generate(compilation);
        foreach (var candidate in new[] { compilation, output })
        {
            var diagnostics = await WithPassAnalyzer(candidate)
                .GetAnalyzerDiagnosticsAsync(testContext.CancellationToken);
            Assert.IsEmpty(diagnostics, Describe(diagnostics));
        }

        Assert.AreEqual(42L, this.InvokeCheck(output, "Lowering"));
    }

    /// <summary>
    /// Rejects cyclic, linear, branching, and tuple-doubling expansive contravariance.
    /// </summary>
    /// <param name="declaration">The contravariant type declaration under test.</param>
    /// <param name="targetType">The target interface type used on the outer processor.</param>
    /// <param name="sourceType">The candidate class type returned by the inner processor.</param>
    /// <returns>A task that completes after analyzer execution.</returns>
    [TestMethod]
    [DataRow("public sealed class A : IContr<IContr<A>>;", "IContr<A>", "A")]
    [DataRow(
        "public sealed class A<T> : IContr<IContr<A<System.Collections.Generic.List<T>>>>;",
        "IContr<A<L0.Lit>>",
        "A<L0.Lit>")]
    [DataRow(
        "public sealed class A<T> : IContr<IContr<A<(T, T)>>>;",
        "IContr<A<L0.Lit>>",
        "A<L0.Lit>")]
    [DataRow(
        """
        public interface IContr2<in T, in U>;
        public sealed class A<T> : IContr2<
            IContr2<A<System.Collections.Generic.List<T>>, A<T[]>>,
            IContr2<A<T[]>, A<System.Collections.Generic.List<T>>>>;
        """,
        "IContr2<A<L0.Lit>, A<L0.Lit>>",
        "A<L0.Lit>")]
    public async Task Inclusion_RejectsRecursiveContravariance(
        string declaration, string targetType, string sourceType)
    {
        var compilation = this.Compile("RecursiveContravariance", $$"""
            public interface IContr<in T>;
            {{declaration}}

            [Language, Terminal(typeof(long), "n")]
            [NonTerminalDef("Expr", ["e"], "t")]
            [NonTerminalDef("Term", ["t"], "(lit n)")]
            public static partial class L0;

            [Pass(typeof(L0), typeof(L0))]
            public partial class Lowering
            {
                [Processor]
                private partial (L0.Expr, {{targetType}}) Lower(L0.Expr node);

                [Processor]
                private static (L0.Term, {{sourceType}}) Recursive(L0.Term node) => (node, new());
            }
            """);
        var output = this.Generate(compilation);
        foreach (var candidate in new[] { compilation, output })
        {
            var diagnostics = await WithPassAnalyzer(candidate)
                .GetAnalyzerDiagnosticsAsync(testContext.CancellationToken);
            Assert.AreEqual("NP1001", Assert.ContainsSingle(diagnostics).Id, Describe(diagnostics));
        }
    }

    /// <summary>Matches Roslyn's 50-layer variance conversion boundary.</summary>
    /// <param name="layers">The number of nested covariant interface layers.</param>
    /// <param name="accepted">Whether the variance conversion is within Roslyn's limit.</param>
    /// <returns>A task that completes after analyzer execution.</returns>
    [TestMethod]
    [DataRow(50, true)]
    [DataRow(51, false)]
    public async Task Inclusion_MatchesRoslynCovariantDepthBoundary(int layers, bool accepted)
    {
        static string Nest(string leaf, int depth)
        {
            string type = leaf;
            for (int i = 0; i < depth; i++)
            {
                type = $"System.Collections.Generic.IEnumerable<{type}>";
            }

            return type;
        }

        string sourceExtra = Nest("L0.Lit", layers);
        string targetExtra = Nest("object", layers);
        var compilation = this.Compile("DeepCovariant", $$"""
            [Language, Terminal(typeof(long), "n")]
            [NonTerminalDef("Expr", ["e"], "t")]
            [NonTerminalDef("Term", ["t"], "(lit n)")]
            public static partial class L0;

            [Pass(typeof(L0), typeof(L0))]
            public partial class Lowering
            {
                [Processor]
                private partial (L0.Expr, {{targetExtra}}) Lower(L0.Expr node);

                [Processor]
                private static (L0.Term, {{sourceExtra}}) Leaf(L0.Term node) => (node, null!);
            }
            """);
        var output = this.Generate(compilation);
        var diagnostics = await WithPassAnalyzer(output)
            .GetAnalyzerDiagnosticsAsync(testContext.CancellationToken);
        if (accepted)
        {
            Assert.IsEmpty(diagnostics, Describe(diagnostics));
        }
        else
        {
            Assert.AreEqual("NP1001", Assert.ContainsSingle(diagnostics).Id, Describe(diagnostics));
        }

        Assert.IsEmpty(output.GetDiagnostics(testContext.CancellationToken)
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
    }

    /// <summary>Synthesizes a terminal processor that widens a generic type.</summary>
    [TestMethod]
    public async Task Terminal_SynthesizesGenericProjection()
    {
        var output = this.Generate(this.Compile("GenericTerminalProjection", """
            using System.Collections.Generic;

            [Language, Terminal(typeof(List<string>), "xs", Name = "items")]
            [NonTerminalDef("Expr", ["e"], "xs")]
            public static partial class Source;

            [Language, Terminal(typeof(IEnumerable<string>), "xs", Name = "items")]
            [NonTerminalDef("Expr", ["e"], "xs")]
            public static partial class Target;

            [Pass(typeof(Source), typeof(Target))]
            public partial class Lowering
            {
                [Processor("items", "items")]
                private partial IEnumerable<string> Widen(List<string> xs);

                public static bool Check()
                {
                    List<string> items = ["safe"];
                    return ReferenceEquals(items, new Lowering().Widen(items));
                }
            }
            """));
        var diagnostics = await WithPassAnalyzer(output)
            .GetAnalyzerDiagnosticsAsync(testContext.CancellationToken);
        Assert.IsEmpty(diagnostics, Describe(diagnostics));
        Assert.IsTrue(Assert.IsInstanceOfType<bool>(this.InvokeCheck(output, "Lowering")));
    }

    /// <summary>
    /// Selects processors with convertible generated extras for Recur and inclusion.
    /// </summary>
    [TestMethod]
    public async Task RecurAndInclusion_SelectProcessorWithGeneratedExtraConversions()
    {
        var compilation = this.Compile("GeneratedExtraConversions", """
            [Language, Terminal(typeof(long), "n")]
            [NonTerminalDef("Expr", ["e"], "t", "(lit n)", "(wrap t)")]
            [NonTerminalDef("Term", ["t"], "(value n)")]
            public static partial class L0;

            public partial union Payload(int, string);

            [Pass(typeof(L0), typeof(L0))]
            public partial class Lowering
            {
                [Processor]
                private partial (L0.Expr, L0.Lit?, L0.Expr, object, (L0.Lit?, long, Payload)) Lower(
                    L0.Expr node);

                [Processor]
                private static (L0.Term, L0.Lit, L0.Lit, L0.Lit, (L0.Lit, int, int)) WithLiteral(
                    L0.Term node)
                {
                    var literal = new L0.Lit(((L0.Value)node.Value).N);
                    return (node, literal, literal, literal, (literal, 42, 7));
                }

                [Processor]
                private static (L0.Term, L0.Lit, L0.Lit, L0.Lit, (string, int, int))
                    WrongNestedExtra(L0.Term node) =>
                    throw new System.NotSupportedException();

                [Rewrite(Processor = nameof(Lower))]
                private static (L0.Expr, L0.Lit?, L0.Expr, object, (L0.Lit?, long, Payload))
                    Literal(L0.Lit literal) =>
                    (literal, literal, literal, literal, (literal, 42, 7));

                [Rewrite(typeof(L0.Wrap), Processor = nameof(Lower))]
                private static (L0.Expr, L0.Lit?, L0.Expr, object, (L0.Lit?, long, Payload)) Wrap(
                    [Recur(Field = "t")] (L0.Term Term, L0.Lit? Record, L0.Expr Union, object Boxed,
                        (L0.Lit?, long, Payload) Nested) t) =>
                    (t.Term, t.Record, t.Union, t.Boxed, t.Nested);

                public static bool Check()
                {
                    var pass = new Lowering();
                    L0.Term term = new L0.Value(7);
                    L0.Expr expected = term;
                    return Matches(pass.Lower(term)) && Matches(pass.Lower(new L0.Wrap(term)));

                    bool Matches((L0.Expr Term, L0.Lit? Record, L0.Expr Union, object Boxed,
                        (L0.Lit? Record, long Count, Payload Extra) Nested) result) =>
                        result.Term.Equals(expected) && result.Record is { N: 7 } literal
                        && result.Union.Equals((L0.Expr)literal)
                        && ReferenceEquals(literal, result.Boxed)
                        && ReferenceEquals(literal, result.Nested.Record)
                        && result.Nested.Count == 42
                        && result.Nested.Extra.Value is 7;
                }
            }
            """);
        var output = this.Generate(compilation);
        foreach (var candidate in new[] { compilation, output })
        {
            var diagnostics = await WithPassAnalyzer(candidate)
                .GetAnalyzerDiagnosticsAsync(testContext.CancellationToken);
            Assert.IsEmpty(diagnostics, Describe(diagnostics));
        }

        Assert.IsTrue(Assert.IsInstanceOfType<bool>(this.InvokeCheck(output, "Lowering")));
    }

    /// <summary>
    /// Validates unused defaults against a fully rewritten processor's declared extra types.
    /// </summary>
    /// <param name="extraType">The declared type of the extra result.</param>
    /// <param name="defaultValue">The invalid attribute default.</param>
    /// <param name="rewriteValue">A valid value returned by the rewrite.</param>
    /// <param name="expectedDiagnostic">The expected compiler diagnostic.</param>
    [TestMethod]
    [DataRow("int", "\"invalid\"", "0", "CS0029")]
    [DataRow("Basic.Lit", "null", "node", "CS8619")]
    public void Processor_RejectsInvalidUnusedDefault(
        string extraType, string defaultValue, string rewriteValue, string expectedDiagnostic)
    {
        var output = this.RunGenerator(this.Compile("InvalidUnusedDefault", BasicGrammar + $$"""
            [Pass(typeof(Basic), typeof(Basic))]
            public partial class Lowering
            {
                [Processor(DefaultExtraReturns = [{{defaultValue}}])]
                private partial (Basic.Expr, {{extraType}}) Lower(Basic.Expr node);

                [Rewrite]
                private static (Basic.Expr, {{extraType}}) Lit(Basic.Lit node) =>
                    (node, {{rewriteValue}});
            }
            """));
        var diagnostics = output.GetDiagnostics(testContext.CancellationToken)
            .Where(static diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning)
            .ToArray();
        Assert.HasCount(1, diagnostics, Describe(diagnostics));
        Assert.AreEqual(expectedDiagnostic, diagnostics[0].Id, Describe(diagnostics));
        Assert.EndsWith(".NanopassPass.g.cs", diagnostics[0].Location.SourceTree!.FilePath);
    }

    /// <summary>
    /// Resolves generated element types in generic and tuple aliases used by Recur.
    /// </summary>
    [TestMethod]
    public async Task Recur_ResolvesSequenceAndTupleAliases()
    {
        var compilation = this.Compile("RecurAliases", """
            namespace Demo;

            using ExprSeq = Seq<Expr>;

            [Language, Terminal(typeof(long), "n")]
            [NonTerminalDef("Expr", ["e"], "(lit n)", "(wrap e ...)", "(single e)")]
            public static partial class L0;

            [Pass(typeof(L0), typeof(L0))]
            public partial class Lowering
            {
                public Expr Run(Expr node) => Lower(node);

                [Processor]
                private partial Expr Lower(Expr node);

                [Processor]
                private static Result WithCount(Expr node) => (node, 7);

                [Rewrite(typeof(Wrap), Processor = nameof(Lower))]
                private static Expr Wrap([Recur(Field = "EList")] ExprSeq items) => items[0];

                [Rewrite(typeof(Single), Processor = nameof(Lower))]
                private static Expr Single([Recur] Result e) => new Lit(e.Count);

                public static string Check() =>
                    UnparseToString(new Lowering().Run(Parse("(wrap (single (lit 2)))")));
            }
            """).AddSyntaxTrees(CSharpSyntaxTree.ParseText(
                """
                global using Hcoona.Nanopass;
                global using static Demo.L0;
                global using Result = (Demo.L0.Expr Term, int Count);
                """,
                ParseOptions, "Imports.cs", cancellationToken: testContext.CancellationToken));
        var output = this.Generate(compilation);
        var diagnostics = await WithPassAnalyzer(output)
            .GetAnalyzerDiagnosticsAsync(testContext.CancellationToken);
        Assert.IsEmpty(diagnostics, Describe(diagnostics));
        Assert.AreEqual("(lit 7)", this.InvokeCheck(output, "Demo.Lowering"));
    }

    /// <summary>Preserves generic arguments in types supplied by another generator.</summary>
    /// <param name="contextType">The spelling of the co-generated type.</param>
    [TestMethod]
    [DataRow("Demo.OtherGen<int>")]
    [DataRow("global::OtherGen<int>")]
    [DataRow("Demo.OtherGen<int>.Nested")]
    public async Task PassInput_PreservesCoGeneratedGenericType(string contextType)
    {
        var compilation = this.Compile("CoGeneratedGeneric", BasicGrammar + $$"""
            [Pass(typeof(Basic), typeof(Basic))]
            public partial class Lowering
            {
                public partial Basic.Expr Run(Basic.Expr node, {{contextType}} context);

                [Processor]
                private partial Basic.Expr Lower(Basic.Expr node, {{contextType}} context);

                public static string Check()
                    => Basic.UnparseToString(new Lowering().Run(
                        Basic.Parse("(lit 3)"), new {{contextType}}()));
            }
            """);
        var driver = CSharpGeneratorDriver.Create(
            [new NanopassGenerator().AsSourceGenerator(),
                new GenericTypeGenerator().AsSourceGenerator()],
            parseOptions: ParseOptions);
        _ = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var output,
            out var generationDiagnostics,
            testContext.CancellationToken);
        Assert.IsEmpty(generationDiagnostics, Describe(generationDiagnostics));
        var diagnostics = await WithPassAnalyzer(output)
            .GetAllDiagnosticsAsync(testContext.CancellationToken);
        var warningsAndErrors = diagnostics.Where(static diagnostic =>
            diagnostic.Severity >= DiagnosticSeverity.Warning).ToArray();
        Assert.IsEmpty(warningsAndErrors, Describe(warningsAndErrors));
        Assert.AreEqual("(lit 3)", this.InvokeCheck(output, "Lowering"));
    }

    [TestMethod]
    public async Task PassInput_PreservesSingleElementValueTuple()
    {
        var output = this.Generate(this.Compile("SingleElementValueTuple", BasicGrammar + """
            [Pass(typeof(Basic), typeof(Basic))]
            public partial class Lowering
            {
                [Processor]
                private partial (Basic.Expr, System.ValueTuple<Basic.Expr>) Lower(Basic.Expr node);

                [Rewrite]
                private static (Basic.Expr, System.ValueTuple<Basic.Expr>) Literal(
                    Basic.Lit node) =>
                    (node, new(node));

                public static string Check() =>
                    Basic.UnparseToString(new Lowering().Lower(new Basic.Lit(42)).Item2.Item1);
            }
            """));
        var diagnostics = await WithPassAnalyzer(output)
            .GetAnalyzerDiagnosticsAsync(testContext.CancellationToken);
        Assert.IsEmpty(diagnostics, Describe(diagnostics));
        Assert.AreEqual("(lit 42)", this.InvokeCheck(output, "Lowering"));
    }

    /// <summary>
    /// Preserves conversions whose generic arguments need generator type resolution.
    /// </summary>
    /// <returns>A task that completes after the generated compilation is analyzed.</returns>
    [TestMethod]
    public async Task PassInput_NormalizesConversionsWithGeneratedTypeArguments()
    {
        const string source = """
            public class Payload<T>(T value)
            {
                public T Value { get; } = value;
            }

            namespace Demo
            {
                using static L0;

                [Language, Terminal(typeof(long), "n")]
                [NonTerminalDef("Expr", ["e"], "(lit n)")]
                public static partial class L0;

                public sealed class LitPayload(Lit value) : Payload<Lit>(value)
                {
                    public static implicit operator LitPayload(Lit value) => new(value);
                }

                [Pass(typeof(L0), typeof(L0))]
                public partial class Lowering
                {
                    [Processor]
                    private partial (Expr, global::Payload<Lit>, LitPayload) Lower(Expr node);

                    [Rewrite]
                    private static (Expr, LitPayload, Lit) LowerLit(Lit node) =>
                        (node, new(node), node);

                    public static long Check()
                    {
                        var (_, payload, converted) = new Lowering().Lower(new Lit(42));
                        return payload.Value.N + converted.Value.N;
                    }
                }
            }
            """;
        var output = this.Generate(this.Compile("GeneratedConversion", source));
        var diagnostics = await WithPassAnalyzer(output)
            .GetAnalyzerDiagnosticsAsync(testContext.CancellationToken);
        Assert.IsEmpty(diagnostics, Describe(diagnostics));

        Assert.AreEqual(84L, this.InvokeCheck(output, "Demo.Lowering"));
    }

    [TestMethod]
    [DataRow("(Basic.Lit, int, int)", "(node, 0, 0)")]
    [DataRow("(Basic.Lit, int)?", "null")]
    public Task Rewrite_InvalidTupleShapeReportsNP1003(string result, string expression) =>
        this.AssertInvalidResultAsync($$"""
            [Processor] private partial (Basic.Expr, int) Lower(Basic.Expr ir);
            [Rewrite] private static {{result}} InvalidRewrite(Basic.Lit node) => {{expression}};
            """, "InvalidRewrite");

    [TestMethod]
    [DataRow("[Processor] private partial (Basic.Expr, int)? Lower(Basic.Expr ir);", "Lower")]
    [DataRow(
        "[Processor(DefaultExtraReturns = [])] "
        + "private partial (Basic.Expr, int) Lower(Basic.Expr ir);",
        "Lower")]
    [DataRow(
        "[Processor(DefaultExtraReturns = [0])] "
        + "private static (Basic.Expr, int) Lower(Basic.Expr ir) => (ir, 1);",
        "Lower")]
    [DataRow(
        "[Processor(DefaultExtraReturns = [])] "
        + "private static Basic.Expr Lower(Basic.Expr ir) => ir;",
        "DefaultExtraReturns")]
    [DataRow(
        "[Processor(DefaultExtraReturns = null)] "
        + "private static Basic.Expr Lower(Basic.Expr ir) => ir;",
        "DefaultExtraReturns")]
    public Task Pass_InvalidResultDeclarationReportsNP1003(string members, string invalidMember) =>
        this.AssertInvalidResultAsync(members, invalidMember);

    [TestMethod]
    [DataRow(
        "[Processor] private partial (Basic.Expr, int) Lower(Basic.Expr ir);"
        + "[Rewrite] private static (Basic.Expr, string) Extra(Basic.Lit node) => (node, \"\");",
        "CS0029", DiagnosticSeverity.Error)]
    [DataRow(
        "[Processor] private partial (Basic.Expr, System.Collections.Generic.IList<object>) "
        + "Lower(Basic.Expr ir);"
        + "[Rewrite] private static (Basic.Expr, Basic.Lit?[]) Extra(Basic.Lit node) "
        + "=> (node, [node, null]);",
        "CS8619", DiagnosticSeverity.Warning)]
    [DataRow(
        "[Processor(DefaultExtraReturns = [null])] "
        + "private partial (Basic.Expr, Basic.Numeric) Lower(Basic.Expr ir);",
        "CS0037", DiagnosticSeverity.Error)]
    [DataRow(
        "[Processor(DefaultExtraReturns = [null])] "
        + "private partial (Basic.Expr, int) Lower(Basic.Expr ir);",
        "CS0037", DiagnosticSeverity.Error)]
    public async Task Pass_InvalidExtraConversionsAreReportedByCSharp(
        string members, string id, DiagnosticSeverity severity)
    {
        var output = this.RunGenerator(this.Compile("InvalidExtra", BasicGrammar + $$"""
            [NonTerminalDef("Numeric", ["num"], "n")]
            public static partial class Basic;

            [Pass(typeof(Basic), typeof(Basic))]
            public partial class Broken { {{members}} }
            """));
        var analyzerDiagnostics = await WithPassAnalyzer(output)
            .GetAnalyzerDiagnosticsAsync(testContext.CancellationToken);
        Assert.IsEmpty(analyzerDiagnostics, Describe(analyzerDiagnostics));

        var diagnostics = output.GetDiagnostics(testContext.CancellationToken);
        var diagnostic = Assert.ContainsSingle(
            diagnostics.Where(item => item.Id == id), message: Describe(diagnostics));
        Assert.AreEqual(severity, diagnostic.Severity);
        Assert.EndsWith(
            ".NanopassPass.g.cs",
            diagnostic.Location.SourceTree!.FilePath,
            StringComparison.Ordinal);
    }

    [TestMethod]
    [DataRow(false, "public string Run(Basic.Expr ir) => \"\";")]
    [DataRow(false, "public (Basic.Expr, int) Run(Basic.Expr ir) => (ir, 0);")]
    [DataRow(false, "public Basic.Expr Run(Basic.Expr ir, params int[] values) => ir;"
        + "public Basic.Expr Run(Basic.Expr ir, params string[] values) => ir;")]
    [DataRow(true, "public Basic.Expr Run(Basic.Expr ir) => new Basic.Lit(42);"
        + "public Basic.Expr Run(Basic.Expr ir, params int[] values) => ir;"
        + "public Basic.Expr Run(Basic.Expr ir, params string[] values) => ir;")]
    public async Task CustomRun_EmitsTypedAdapterOnlyForAnUnambiguousCompatibleOverload(
        bool hasAdapter, string members)
    {
        var output = this.Generate(this.Compile("CustomRun", BasicGrammar + $$"""
            [Pass(typeof(Basic), typeof(Basic))]
            public sealed partial class Custom
            {
                {{members}}
                public static string Check() =>
                    (INanopassPass)new Custom() is IPass<Basic.Expr, Basic.Expr> adapter
                        ? Basic.UnparseToString(adapter.Run(new Basic.Lit(7)))
                        : "no adapter";
            }
            """));
        var diagnostics = await WithPassAnalyzer(output)
            .GetAnalyzerDiagnosticsAsync(testContext.CancellationToken);
        Assert.IsEmpty(diagnostics, Describe(diagnostics));
        Assert.AreEqual(hasAdapter ? "(lit 42)" : "no adapter", this.InvokeCheck(output, "Custom"));
    }

    [TestMethod]
    [DataRow("Basic.Lit")]
    [DataRow("Basic.Expr")]
    public async Task Rewrite_NullableGeneratedTypeConvertsToNullableInterface(string type)
    {
        string declaration = $$"""
            [Pass(typeof(Basic), typeof(Basic))]
            public partial class Identity
            {
                [Processor]
                private partial (Basic.Expr, System.IEquatable<{{type}}>?) Lower(Basic.Expr ir);

                [Rewrite]
                private static (Basic.Expr, {{type}}?) LowerLit(Basic.Lit node) =>
                    (node, node.N > 0 ? ({{type}})node : default({{type}}?));

                public static bool Check()
                {
                    var pass = new Identity();
                    var lit = new Basic.Lit(42);
                    var (term, value) = pass.Lower(lit);
                    var (_, missing) = pass.Lower(new Basic.Lit(0));
                    return term.Equals((Basic.Expr)lit) && value?.Equals(({{type}})lit) == true
                        && missing is null;
                }
            }
            """;
        var output = this.Generate(this.Compile("NullableInterface", BasicGrammar + declaration));
        var diagnostics = await WithPassAnalyzer(output)
            .GetAnalyzerDiagnosticsAsync(testContext.CancellationToken);
        Assert.IsEmpty(diagnostics, Describe(diagnostics));

        Assert.IsTrue(Assert.IsInstanceOfType<bool>(this.InvokeCheck(output, "Identity")));
    }

    [TestMethod]
    [DataRow("Basic.Lit", "node", "(Basic.Lit)value")]
    [DataRow("Seq<Basic.Lit>", "[node]", "((Seq<Basic.Lit>)value)[0]")]
    public async Task Rewrite_GeneratedPrimaryResultConvertsToObject(
        string type, string result, string unbox)
    {
        string declaration = $$"""
            [Language, Terminal(typeof(object), "o", Name = "o")]
            [NonTerminalDef("Expr", ["e"], "o")]
            public static partial class Output;

            [Pass(typeof(Basic), typeof(Output))]
            public partial class Lowering
            {
                public Output.Expr Run(Basic.Expr ir) => this.Lower(ir);

                [Processor("Expr", "o")]
                private partial object Lower(Basic.Expr ir);

                [Rewrite(Processor = nameof(Lower))]
                private static {{type}} LowerLit(Basic.Lit node) => {{result}};

                public static long Check() => Unbox(new Lowering().Run(new Basic.Lit(42)).Value!);
                private static long Unbox(object value) => ({{unbox}}).N;
            }
            """;
        var output = this.Generate(this.Compile("PrimaryObject", BasicGrammar + declaration));
        var diagnostics = await WithPassAnalyzer(output)
            .GetAnalyzerDiagnosticsAsync(testContext.CancellationToken);
        Assert.IsEmpty(diagnostics, Describe(diagnostics));

        Assert.AreEqual(42L, this.InvokeCheck(output, "Lowering"));
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task Rewrite_WidenedPrimaryUnionCaseReportsNP1003(bool compiled)
    {
        const string grammar = """
            [Language, Terminal(typeof(long), "n")]
            [NonTerminalDef("Expr", ["e"], "n")]
            public static partial class Output;
            """;
        const string declaration = """
            [Pass(typeof(Basic), typeof(Output))]
            public partial class Broken
            {
                [Processor]
                private partial Output.Expr Lower(Basic.Expr ir);

                [Rewrite]
                private static int LowerLit(Basic.Lit node) => 42;
            }
            """;
        var source = BasicGrammar + declaration;
        var output = this.Generate(compiled
            ? this.Compile("PrimaryWidening", source, MetadataReference.CreateFromImage(
                this.Emit(this.Generate(this.Compile("CompiledOutput", grammar)))))
            : this.Compile("PrimaryWidening", grammar + source));
        return this.AssertInvalidPassAsync(output, "LowerLit");
    }

    /// <summary>
    /// Verifies a removed terminal cannot silently copy into an identically typed terminal.
    /// </summary>
    /// <returns>A task that completes after the analyzer and code fix run.</returns>
    [TestMethod]
    public async Task RemovedSameTypeTerminal_RequiresNamedRewrite()
    {
        const string source = """
            [Language]
            [Terminal(typeof(Symbol), "pr", Name = "primitive")]
            [Terminal(typeof(Symbol), "x", Name = "variable", Predicate = nameof(IsVariable))]
            [NonTerminalDef("Expr", ["e"], "pr", "x")]
            public static partial class Source
            {
                public static bool IsVariable(Symbol x) => x.Name != "primitive";
            }

            [Language, Terminal(typeof(Symbol), "pr", Name = "primitive")]
            [NonTerminalDef("Expr", ["e"], "pr")]
            public static partial class Target;

            [Pass(typeof(Source), typeof(Target))]
            public partial class LowerNegation;
            """;
        using var workspace = new AdhocWorkspace();
        var document = CreateDocument(workspace, source);
        var diagnostic = Assert.ContainsSingle(await this.GetDiagnosticsAsync(document));
        Assert.AreEqual("NP1001", diagnostic.Id);
        var provider = new MissingRewriteRuleCodeFixProvider();
        var action = await this.GetCodeActionAsync(provider, document, diagnostic);
        var fixedDocument = await this.ApplyAsync(document, action);

        Assert.IsEmpty(await this.GetDiagnosticsAsync(fixedDocument));
        var stub = Assert.ContainsSingle(await this.FindRewriteStubsAsync(fixedDocument));
        var parameter = Assert.ContainsSingle(stub.ParameterList.Parameters);
        Assert.AreEqual("x", parameter.Identifier.ValueText);
    }

    /// <summary>
    /// Verifies that an explicit terminal catamorphism requires a declared terminal processor.
    /// </summary>
    /// <returns>A task that completes after analyzer execution.</returns>
    [TestMethod]
    public async Task TerminalCatamorphism_RejectsSynthesizedTerminalProcessor_Issue27()
    {
        const string input = """
            [Language, Terminal(typeof(Symbol), "x", Name = "symbol")]
            [NonTerminalDef("Expr", ["e"], "x", "(x ...)")]
            public static partial class In;

            [Language, Terminal(typeof(Symbol), "x", Name = "symbol")]
            [NonTerminalDef("Expr", ["e"], "x", "(call x ...)")]
            public static partial class Out;

            [Pass(typeof(In), typeof(Out))]
            public partial class Broken
            {
                [Processor("Expr", "Expr")]
                private partial Out.Expr Process(In.Expr ir);

                [Rewrite(typeof(In.Implicit), Processor = nameof(Process))]
                private Out.Expr Call([Recur] Seq<Symbol> x) => new Out.Call(x);
            }
            """;
        using var workspace = new AdhocWorkspace();
        var diagnostics = await this.GetDiagnosticsAsync(CreateDocument(workspace, input));

        Diagnostic actual = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual("NP1002", actual.Id);
        Assert.Contains("symbol", actual.GetMessage(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Verifies that a rewrite on an unused inferred processor is diagnosed on its clause.
    /// </summary>
    /// <returns>A task that completes after analyzer execution.</returns>
    [TestMethod]
    public async Task Rewrite_UncalledInferredProcessorReportsUnreachableClause()
    {
        const string input = """
            [Language, Terminal(typeof(long), "n")]
            [NonTerminalDef("Expr", ["e"], "m")]
            [NonTerminalDef("Middle", ["m"], "l")]
            [NonTerminalDef("Leaf", ["l"], "(lit n)")]
            public static partial class Target;

            [Pass(typeof(Basic), typeof(Target))]
            public partial class Broken
            {
                [Rewrite]
                private static Target.Leaf Unused(Basic.Lit node) => new Target.Lit(node.N + 1);
            }
            """;
        using var workspace = new AdhocWorkspace();
        var diagnostics = await this.GetDiagnosticsAsync(
            CreateDocument(workspace, BasicGrammar + input));

        var diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual("NP1003", diagnostic.Id);
        Assert.Contains(
            "unreachable rewrite 'Unused'", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.AreEqual("Unused", diagnostic.Location.SourceTree!.GetText(
            testContext.CancellationToken).ToString(diagnostic.Location.SourceSpan));
    }

    /// <summary>
    /// Verifies validation of invalid endpoint and clause signatures.
    /// </summary>
    /// <param name="declaration">The invalid pass declaration.</param>
    /// <param name="fragment">An identifying term in the diagnostic text.</param>
    /// <returns>A task that completes after analyzer execution.</returns>
    [TestMethod]
    [DataRow("""
        [Pass(typeof(string), typeof(RawTerm))]
        public partial class Broken;
        """, "string")]
    [DataRow("""
        [Pass(typeof(RawTerm), typeof(string))]
        public partial class Broken;
        """, "string")]
    [DataRow("""
        [Pass(typeof(RawTerm), typeof(Basic), OutputEntry = "Foo")]
        public partial class Broken;
        """, "Foo")]
    [DataRow("""
        [Pass(typeof(RawTerm), typeof(Basic))]
        public partial class Broken
        {
            [Processor("*", "Foo")]
            private Basic.Expr Parse(SExpr ir) => Basic.Parse(ir);
        }
        """, "Foo")]
    [DataRow("""
        [Pass(typeof(Basic), typeof(Basic))]
        public partial class Broken
        {
            [Processor("Expr", "Expr")]
            private partial Basic.Expr InvalidProcessor(string ir);
        }
        """, "InvalidProcessor")]
    [DataRow("""
        [Pass(typeof(Basic), typeof(RawTerm))]
        public partial class Broken
        {
            [Processor("Expr", "*")]
            private partial SExpr Emit(Basic.Expr ir);

            [Rewrite(typeof(Basic.Lit), Processor = nameof(Emit))]
            private long InvalidRewrite(Basic.Lit node) => node.N;
        }
        """, "InvalidRewrite")]
    [DataRow("""
        [Pass(typeof(Basic), typeof(Basic))]
        public partial class Broken
        {
            [Rewrite(typeof(Basic.Lit))]
            private Basic.Expr InvalidRecur([Recur(Field = "noField")] Basic.Expr ir) => ir;
        }
        """, "noField")]
    [DataRow("""
        [Pass(typeof(Basic), typeof(Basic))]
        public partial class Broken
        {
            [Rewrite(Guard = nameof(InvalidGuard))]
            private Basic.Expr Keep(Basic.Lit node) => node;

            private long InvalidGuard(Basic.Lit node) => node.N;
        }
        """, "InvalidGuard")]
    [DataRow("""
        [Pass(typeof(Basic), typeof(Basic))]
        public partial class Broken
        {
            [Processor("Expr", "Expr")]
            private Basic.Expr One(Basic.Expr ir) => ir;

            [Processor("Expr", "Expr")]
            private Basic.Expr Two(Basic.Expr ir) => ir;
        }
        """, "ambiguous")]
    public async Task InvalidPass_ReportsNP1003WithoutBrokenGeneratedCode(
        string declaration, string fragment)
    {
        using var workspace = new AdhocWorkspace();
        var document = CreateDocument(workspace, BasicGrammar + declaration);
        var diagnostics = await this.GetDiagnosticsAsync(document);
        var diagnostic = diagnostics.FirstOrDefault(diagnostic =>
            diagnostic.Id == "NP1003"
            && diagnostic.GetMessage().Contains(fragment, StringComparison.OrdinalIgnoreCase));

        Assert.IsNotNull(diagnostic, Describe(diagnostics));
        Assert.IsTrue(diagnostic.Location.IsInSource);
    }

    /// <summary>
    /// Verifies equal-priority included processors report ambiguity rather than synthesizing.
    /// </summary>
    /// <param name="secondTarget">The destination of the second registered processor.</param>
    /// <returns>A task that completes after analyzer execution.</returns>
    [TestMethod]
    [DataRow("Leaf")]
    [DataRow("OtherLeaf")]
    public async Task Synthesis_AmbiguousIncludedProcessorsReportNP1003(string secondTarget)
    {
        string source = $$"""
            [Language, Terminal(typeof(Symbol), "x")]
            [NonTerminalDef("Expr", ["e"], "a")]
            [NonTerminalDef("Atom", ["a"], "(var x)")]
            public static partial class Source;

            [Language, Terminal(typeof(Symbol), "x")]
            [NonTerminalDef("Expr", ["e"], "l", "o")]
            [NonTerminalDef("Leaf", ["l"], "(var x)")]
            [NonTerminalDef("OtherLeaf", ["o"], "(other x)")]
            public static partial class Target;

            [Pass(typeof(Source), typeof(Target))]
            public partial class Broken
            {
                [Processor("Atom", "Leaf")]
                private static Target.Leaf One(Source.Atom ir) => default;

                [Processor("Atom", "{{secondTarget}}")]
                private static Target.{{secondTarget}} Two(Source.Atom ir) => default;
            }
            """;
        using var workspace = new AdhocWorkspace();
        var diagnostics = await this.GetDiagnosticsAsync(CreateDocument(workspace, source));

        var diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual("NP1003", diagnostic.Id);
        string message = diagnostic.GetMessage();
        Assert.Contains(
            "ambiguous processors for 'Atom' to 'Leaf'", message, StringComparison.Ordinal);
        Assert.Contains($"'{secondTarget}'", message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Verifies that compiled grammar metadata preserves productions and inclusions.
    /// </summary>
    [TestMethod]
    public void CompiledLanguages_ResolveEntryAndIncludedProductions()
    {
        const string grammar = """
            [Language]
            [Terminal(typeof(Symbol), "pr", Name = "primitive")]
            [Terminal(typeof(Symbol), "x", Name = "variable", Predicate = nameof(IsVariable))]
            [NonTerminalDef("Expr", ["e"], "a", "(wrap e)")]
            [NonTerminalDef("Atom", ["a"], "pr", "x", "(id x)")]
            internal static partial class Base
            {
                private static bool IsVariable(Symbol x) => x.Name != "reserved";
            }

            [Language, Extends(typeof(Base))]
            public static partial class Source;

            [Language, Extends(typeof(Base))]
            public static partial class Target;
            """;
        const string pass = """
            [Language, Extends(typeof(Target))]
            public static partial class PublicDescendant;

            [Pass(typeof(Source), typeof(Target))]
            public partial class CrossAssembly
            {
                public static string Transform(string text) =>
                    Target.UnparseToString(new CrossAssembly().Run(Source.Parse(text)));

                [Rewrite]
                private static Target.Atom KeepOnlyVariables(Symbol x) => new Symbol("renamed");
            }
            """;
        var grammarCompilation = this.Compile("PassExternalGrammars", grammar);
        byte[] grammarBytes = this.Emit(this.Generate(grammarCompilation));
        var reference = MetadataReference.CreateFromImage(grammarBytes);
        var passCompilation = this.Compile("PassExternalConsumer", pass, reference);
        byte[] passBytes = this.Emit(this.Generate(passCompilation));
        var loader = new AssemblyLoadContext("PassExternalConsumer", isCollectible: true);

        try
        {
            using var grammarStream = new MemoryStream(grammarBytes);
            using var passStream = new MemoryStream(passBytes);
            _ = loader.LoadFromStream(grammarStream);
            var assembly = loader.LoadFromStream(passStream);
            var type = assembly.GetType("CrossAssembly", throwOnError: true)!;
            var transform = type.GetMethod("Transform")!;
            const string nested = "(wrap (wrap (id x)))";
            Assert.AreEqual(nested, transform.Invoke(null, [nested]));
            Assert.AreEqual("(wrap renamed)", transform.Invoke(null, ["(wrap original)"]));
            Assert.AreEqual("(wrap reserved)", transform.Invoke(null, ["(wrap reserved)"]));
        }
        finally
        {
            loader.Unload();
        }
    }

    private static string Describe(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics);

    private sealed class GenericTypeGenerator : IIncrementalGenerator
    {
        public void Initialize(IncrementalGeneratorInitializationContext context) =>
            context.RegisterSourceOutput(context.CompilationProvider, static (output, _) =>
                output.AddSource("OtherGen.g.cs", """
                public sealed class OtherGen<T>;

                namespace Demo
                {
                    public sealed class OtherGen<T>
                    {
                        public sealed class Nested;
                    }
                }
                """));
    }

    private Task AssertInvalidResultAsync(string members, string invalidMember) =>
        this.AssertInvalidPassAsync(this.Generate(this.Compile("InvalidExtra", BasicGrammar + $$"""
            [Pass(typeof(Basic), typeof(Basic))]
            public partial class Broken { {{members}} }
            """)), invalidMember);

    private async Task AssertInvalidPassAsync(Compilation output, string invalidMember)
    {
        var diagnostics = await WithPassAnalyzer(output)
            .GetAnalyzerDiagnosticsAsync(testContext.CancellationToken);
        var diagnostic = Assert.ContainsSingle(diagnostics);
        Assert.AreEqual("NP1003", diagnostic.Id);
        Assert.Contains(invalidMember, diagnostic.GetMessage(), StringComparison.Ordinal);
    }

    private object? InvokeCheck(Compilation compilation, string typeName)
    {
        var loader = new AssemblyLoadContext(compilation.AssemblyName, isCollectible: true);
        try
        {
            using var stream = new MemoryStream(this.Emit(compilation));
            var pass = loader.LoadFromStream(stream).GetType(typeName, throwOnError: true)!;
            return pass.GetMethod("Check")!.Invoke(null, null);
        }
        finally
        {
            loader.Unload();
        }
    }

    private static CompilationWithAnalyzers WithPassAnalyzer(Compilation compilation) =>
        compilation.WithAnalyzers(
            [new NanopassPassAnalyzer()],
            new CompilationWithAnalyzersOptions(
                options: new AnalyzerOptions([]),
                onAnalyzerException: null,
                concurrentAnalysis: true,
                logAnalyzerExecutionTime: false,
                reportSuppressedDiagnostics: false));

    private static string MissingRewriteSource(params string[] productions) =>
        """
        [Language, Terminal(typeof(long), "n")]
        [NonTerminalDef("Expr", ["e"], "(lit n)",
        """ + string.Join(", ", productions.Select(static production => $"\"{production}\"")) + """
        )]
        public static partial class Arithmetic;
        """ + BasicGrammar + """
        [Pass(typeof(Arithmetic), typeof(Basic))]
        public partial class LowerNegation
        {
            [Processor("Expr", "Expr")]
            private partial Basic.Expr Lower(Arithmetic.Expr ir);
        }
        """;

    private static Document CreateDocument(AdhocWorkspace workspace, string source)
    {
        var project = workspace.AddProject(ProjectInfo.Create(
            ProjectId.CreateNewId(), VersionStamp.Create(), "PassCase", "PassCase",
            LanguageNames.CSharp,
            parseOptions: ParseOptions,
            compilationOptions: CompilationOptions,
            metadataReferences: PlatformReferences));
        return workspace.AddDocument(
            project.Id, "PassCase.cs", SourceText.From("using Hcoona.Nanopass;\n" + source));
    }

    private CSharpCompilation Compile(
        string name, string source, params MetadataReference[] references) =>
        CSharpCompilation.Create(
            name,
            [CSharpSyntaxTree.ParseText(
                "using Hcoona.Nanopass;\n" + source, ParseOptions, name + ".cs",
                cancellationToken: testContext.CancellationToken)],
            PlatformReferences.AddRange(references), CompilationOptions);

    private Compilation Generate(Compilation compilation)
    {
        var output = this.RunGenerator(compilation);
        var compilerDiagnostics = output.GetDiagnostics(testContext.CancellationToken)
            .Where(static diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning)
            .ToArray();
        Assert.IsEmpty(compilerDiagnostics, Describe(compilerDiagnostics));
        return output;
    }

    private Compilation RunGenerator(Compilation compilation)
    {
        var driver = CSharpGeneratorDriver.Create(
            [new NanopassGenerator().AsSourceGenerator()], parseOptions: ParseOptions);
        _ = driver.RunGeneratorsAndUpdateCompilation(
            compilation, out var output, out var diagnostics, testContext.CancellationToken);
        Assert.IsEmpty(diagnostics, Describe(diagnostics));
        return output;
    }

    private async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(Document document)
    {
        var compilation = await document.Project.GetCompilationAsync(testContext.CancellationToken);
        Assert.IsNotNull(compilation);
        return await WithPassAnalyzer(this.Generate(compilation))
            .GetAnalyzerDiagnosticsAsync(testContext.CancellationToken);
    }

    private async Task<CodeAction> GetCodeActionAsync(
        CodeFixProvider provider, Document document, Diagnostic diagnostic)
    {
        var actions = new List<CodeAction>();
        await provider.RegisterCodeFixesAsync(new CodeFixContext(
            document, diagnostic, (action, _) => actions.Add(action),
            testContext.CancellationToken));
        return Assert.ContainsSingle(actions);
    }

    private async Task<Document> ApplyAsync(Document document, CodeAction action)
    {
        var operations = await action.GetOperationsAsync(testContext.CancellationToken);
        return Assert.ContainsSingle(operations.OfType<ApplyChangesOperation>())
            .ChangedSolution.GetDocument(document.Id)!;
    }

    private async Task<MethodDeclarationSyntax[]> FindRewriteStubsAsync(Document document)
    {
        var syntax = await document.GetSyntaxRootAsync(testContext.CancellationToken);
        Assert.IsNotNull(syntax);
        var pass = Assert.ContainsSingle(syntax.DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Where(static node => string.Equals(
                node.Identifier.ValueText, "LowerNegation", StringComparison.Ordinal)));
        return pass.Members.OfType<MethodDeclarationSyntax>()
            .Where(static method => method.AttributeLists.SelectMany(static list => list.Attributes)
                .Any(static attribute =>
                    attribute.Name.ToString().Contains("Rewrite", StringComparison.Ordinal)))
            .ToArray();
    }

    private byte[] Emit(Compilation compilation)
    {
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream, cancellationToken: testContext.CancellationToken);
        Assert.IsTrue(result.Success, Describe(result.Diagnostics));
        return stream.ToArray();
    }

    private sealed class FixAllDiagnostics(ImmutableArray<Diagnostic> diagnostics)
        : FixAllContext.DiagnosticProvider
    {
        /// <inheritdoc/>
        public override Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(
            Document document, CancellationToken cancellationToken) =>
            Task.FromResult<IEnumerable<Diagnostic>>(diagnostics);

        /// <inheritdoc/>
        public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(
            Project project, CancellationToken cancellationToken) =>
            Task.FromResult<IEnumerable<Diagnostic>>([]);

        /// <inheritdoc/>
        public override Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(
            Project project, CancellationToken cancellationToken) =>
            Task.FromResult<IEnumerable<Diagnostic>>(diagnostics);
    }
}
