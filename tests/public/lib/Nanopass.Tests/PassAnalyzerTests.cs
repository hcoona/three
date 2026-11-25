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
        AssertCached(otherEdit);
        var bodyEdit = otherEdit.ReplaceSyntaxTree(passTree, Parse(
            "using Hcoona.Nanopass;\n" + BasicGrammar + pass.Replace(
                "node.N + 1", "node.N * 75", StringComparison.Ordinal), passTree.FilePath));
        AssertCached(bodyEdit);
        var otherGrammar = BasicGrammar.Replace("Basic", "Other", StringComparison.Ordinal);
        var grammarEdit = bodyEdit.ReplaceSyntaxTree(
            otherReplacement,
            Parse("using Hcoona.Nanopass;\n" + otherGrammar, otherTree.FilePath));
        AssertCached(grammarEdit);

        SyntaxTree Parse(string source, string path) =>
            CSharpSyntaxTree.ParseText(
                source, ParseOptions, path, cancellationToken: testContext.CancellationToken);

        void AssertCached(Compilation updated)
        {
            driver = driver.RunGeneratorsAndUpdateCompilation(
                updated, out var output, out var diagnostics, testContext.CancellationToken);
            Assert.IsEmpty(diagnostics, Describe(diagnostics));
            Assert.IsEmpty(output.GetDiagnostics(testContext.CancellationToken)
                .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
            var steps = Assert.ContainsSingle(driver.GetRunResult().Results).TrackedSteps;
            var input = Assert.ContainsSingle(Assert.ContainsSingle(steps["PassInputs"]).Outputs);
            IncrementalStepRunReason[] stableReasons =
                [IncrementalStepRunReason.Cached, IncrementalStepRunReason.Unchanged];
            Assert.Contains(input.Reason, stableReasons);
            foreach (string step in new[] { "PassResolution", "PassEmission" })
            {
                Assert.AreEqual(IncrementalStepRunReason.Cached,
                    Assert.ContainsSingle(Assert.ContainsSingle(steps[step]).Outputs).Reason, step);
            }
        }
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
        var driver = CSharpGeneratorDriver.Create(
            [new NanopassGenerator().AsSourceGenerator()], parseOptions: ParseOptions);
        _ = driver.RunGeneratorsAndUpdateCompilation(
            compilation, out var output, out var diagnostics, testContext.CancellationToken);
        Assert.IsEmpty(diagnostics, Describe(diagnostics));
        var compilerDiagnostics = output.GetDiagnostics(testContext.CancellationToken)
            .Where(static diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning)
            .ToArray();
        Assert.IsEmpty(compilerDiagnostics, Describe(compilerDiagnostics));
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
