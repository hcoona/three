using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using Hcoona.Nanopass.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Hcoona.Nanopass.Tests;

[TestClass]
public sealed class LanguageGeneratorTests(TestContext testContext)
{
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Preview);
    private static readonly ImmutableArray<MetadataReference> PlatformReferences =
    [
        .. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Append(typeof(SExpr).Assembly.Location)
            .Distinct(StringComparer.Ordinal)
            .Select(static path => MetadataReference.CreateFromFile(path)),
    ];

    [TestMethod]
    [DataRow("NP0101", """
        [Language, Extends(typeof(string))]
        public static partial class Broken;
        """)]
    [DataRow("NP0101", """
        [Language, PrunedLanguage(typeof(string))]
        public static partial class Broken;
        """)]
    [DataRow("NP0102", """
        [Language, Extends(typeof(Other))] public static partial class Broken;
        [Language, Extends(typeof(Broken))] public static partial class Other;
        """)]
    [DataRow("NP0102", """
        [Language, Extends(typeof(string)), Extends(typeof(Symbol))]
        public static partial class Broken;
        """)]
    [DataRow("NP0102", """
        [Language, Entry("Expr"), Entry("Expr"), NonTerminalDef("Expr", ["e"], "(unit)")]
        public static partial class Broken;
        """)]
    [DataRow("NP0102", """
        [Language, Entry("Missing"), NonTerminalDef("Expr", ["e"], "(unit)")]
        public static partial class Broken;
        """)]
    [DataRow("NP0103", """
        [Language, NonTerminalDef("Expr", ["e"], "(unit)")] public static partial class Base;
        [Language, Extends(typeof(Base)), RemoveTerminal(typeof(Symbol))]
        public static partial class Broken;
        """)]
    [DataRow("NP0103", """
        [Language, NonTerminalDef("Expr", ["e"], "(unit)")] public static partial class Base;
        [Language, Extends(typeof(Base)), ExtendNonTerminal("Missing", Remove = ["(unit)"])]
        public static partial class Broken;
        """)]
    [DataRow("NP0103", """
        [Language, NonTerminalDef("Expr", ["e"], "(unit)"), ExtendNonTerminal("Missing")]
        public static partial class Broken;
        """)]
    [DataRow("NP0103", """
        [Language, NonTerminalDef("Expr", ["e"], "(unit)")] public static partial class Base;
        [Language, Extends(typeof(Base)), ExtendNonTerminal("Expr", Remove = ["(missing)"])]
        public static partial class Broken;
        """)]
    [DataRow("NP0103", """
        [Language, NonTerminalDef("Expr", ["e"], "(unit)")] public static partial class Base;
        [Language, Extends(typeof(Base)), Remove(typeof(int))] public static partial class Broken;
        """)]
    [DataRow("NP0104", """
        [Language, Terminal(typeof(Symbol), "s"), NonTerminalDef("Expr", ["e"], "s")]
        public static partial class Base;
        [Language, Extends(typeof(Base)), ExtendNonTerminal("Expr", Add = ["s"])]
        public static partial class Broken;
        """)]
    [DataRow("NP0104", """
        [Language, Terminal(typeof(Symbol), "x"), Terminal(typeof(Symbol), "y")]
        [NonTerminalDef("Expr", ["e"], "x")]
        public static partial class Broken;
        """)]
    [DataRow("NP0104", """
        [Language, Terminal(typeof(long), "n")]
        [NonTerminalDef("Expr", ["e"], "(const n)", "(const n)")]
        public static partial class DuplicateAlternative;
        """)]
    [DataRow("NP0104", """"
        [Language, Terminal(typeof(Symbol), "x")]
        [NonTerminalDef("Expr", ["e"], "(var |x|)", """(va\r \x)""")]
        public static partial class DuplicateEscapedAlternative;
        """")]
    [DataRow("NP0104", """
        [Language, Terminal(typeof(Symbol), "|x|", "x")]
        [NonTerminalDef("Expr", ["e"], "x")]
        public static partial class DuplicateMetaVariable;
        """)]
    [DataRow("NP0105", """
        [Language, NonTerminalDef("Parse", ["p"], "(unit)")]
        public static partial class Broken;
        """)]
    [DataRow("NP0105", """
        [Language, NonTerminalDef("Broken", ["p"], "(unit)")]
        public static partial class Broken;
        """)]
    [DataRow("NP0105", """
        [Language, NonTerminalDef("Expr", ["e"], "(unit)")]
        public static partial class Broken
        {
            public static void Expr() { }
        }
        """)]
    [DataRow("NP0105", """
        [Language, Terminal(typeof(int), "|.|")]
        [NonTerminalDef("Expr", ["e"], "(unit)")]
        public static partial class Broken;
        """)]
    [DataRow("NP0105", """
        [Language, Terminal(typeof(Symbol)), NonTerminalDef("Expr", ["e"], "x")]
        public static partial class Broken;
        """)]
    [DataRow("NP0105", """
        [Language, NonTerminalDef("Expr", [], "(unit)")] public static partial class Broken;
        """)]
    [DataRow("NP0105", """
        [Language, NonTerminalDef("Empty", [], "(unit)")]
        public static partial class Broken
        {
            public partial union Expr(Empty);
        }
        """)]
    [DataRow("NP0105", """
        [Language, NonTerminalDef("Expr", ["e"], "(unit)")]
        public static partial class Source;

        [Language, Extends(typeof(Source)), Entry("Fresh")]
        public static partial class Broken
        {
            public sealed record New;
        }
        """)]
    [DataRow("NP0105", """
        [Language, NonTerminalDef("Expr", ["e"], "(1 2)")] public static partial class Broken;
        """)]
    [DataRow("NP0105", """
        [Language, NonTerminalDef("Expr", ["e"], "(bob 2)")] public static partial class Broken;
        """)]
    [DataRow("NP0105", """
        [Language, NonTerminalDef("Expr", ["e"], "(bob unknown)")]
        public static partial class Broken;
        """)]
    [DataRow("NP0105", """
        [Language, NonTerminalDef("Expr", ["e"], "(bob e ... .)")]
        public static partial class Broken;
        """)]
    [DataRow("NP0105", """
        [Language, NonTerminalDef("Expr", ["e"], "(children e ...) => (values e)")]
        public static partial class Broken;
        """)]
    [DataRow("NP0105", """
        [Language]
        public static partial class Broken
        {
            public partial union Value(bool);
        }
        """)]
    [DataRow("NP0105", """
        [Language, NonTerminalDef("Expr", ["e"], "(unit)")]
        public static partial class Source;

        [Language, Extends(typeof(Source))]
        public static partial class Broken
        {
            public partial union Expr(New);
            public sealed record New;
        }
        """)]
    [DataRow("NP0105", """
        [Language, ExtendNonTerminal("Expr", Add = ["(extra)"])]
        public static partial class Broken
        {
            public partial union Expr(Unit);
            public sealed record Unit;
        }
        """)]
    [DataRow("NP0105", """
        [Language, ExtendNonTerminal("Expr", Add = ["(extra)"])]
        public static partial class Hybrid
        {
            public partial union Expr(Unit, Hybrid.Extra);
            public sealed record Unit;
        }
        """)]
    [DataRow("NP0105", """
        [Language, ExtendNonTerminal("Expr", Remove = ["(unit)"])]
        public static partial class Broken
        {
            public partial union Expr(Unit, Keep);
            public sealed record Unit;
            public sealed record Keep;
        }
        """)]
    [DataRow("NP0105", """
        using System;

        [Language, Entry("Expr"), NonTerminalDef("Range", ["r"], "(span)")]
        public static partial class Hybrid
        {
            public partial union Expr(Range?);
        }
        """)]
    [DataRow("NP0105", """
        [Language]
        public static partial class Hybrid
        {
            public partial union Expr(Term?);
            public sealed record Term;
        }
        """)]
    [DataRow("NP0105", """
        [Language, ExtendNonTerminal("Expr", Remove = ["(unit)"])]
        public static partial class Broken
        {
            public partial union Expr(Unit);
            public sealed record Unit;
        }
        """)]
    [DataRow("NP0105", """
        [Language]
        public static partial class Broken
        {
            public partial union Expr(Unit);

            [Syntax("(unit) => inline"), Pretty("separate")]
            public sealed record Unit;
        }
        """)]
    [DataRow("NP0105", """
        [Language]
        public static partial class Broken
        {
            public partial union Expr(Unit);

            [Syntax("(unit) => inline"), Pretty]
            public sealed record Unit;
        }
        """)]
    public void Diagnostics_ReportGrammarErrorsAtSource(string expected, string declaration)
    {
        var compilerInput = this.Compile("Diagnostics", declaration);
        GeneratorDriver driver = Driver().RunGenerators(
            compilerInput, testContext.CancellationToken);
        var result = driver.GetRunResult();
        var error = result.Diagnostics.FirstOrDefault(diagnostic =>
            string.Equals(diagnostic.Id, expected, StringComparison.Ordinal));
        Assert.IsNotNull(error, Describe(result.Diagnostics));
        Assert.AreEqual("Diagnostics.cs", error.Location.GetLineSpan().Path);
        Assert.IsGreaterThan(0, error.Location.SourceSpan.Length);
        Assert.IsNull(result.Results.Single().Exception);
    }

    [TestMethod]
    public void BinaryMetadata_PreservesExternalExtensionPruningAndHandlers()
    {
        const string baseSource = """
            namespace Cross;

            [Language]
            [Terminal(
                typeof(Symbol), "x",
                Predicate = nameof(IsVariable), Prettifier = nameof(PrettyVariable))]
            [Terminal(typeof(long), "n", Name = "integer")]
            [Terminal(typeof(bool), "b", Name = "boolean")]
            [NonTerminalDef("Expr", ["e"], "(var x) => x", "(add e0 e1)", "n")]
            [NonTerminalDef("Unused", ["u"], "b")]
            public static partial class ExternalBase
            {
                private static bool IsVariable(Symbol symbol) => symbol.Name != "+";
                private static Symbol PrettyVariable(Symbol symbol) => new("pretty-" + symbol.Name);
            }
            """;
        const string consumerSource = """
            namespace Cross;

            [Language, Extends(typeof(ExternalBase))]
            [Remove(typeof(ExternalBase.Add))]
            [RemoveTerminal("integer")]
            [ExtendNonTerminal("Expr", Remove = ["n"], Add = ["(sub e0 e1)"])]
            public static partial class ExternalDerived
            {
                public static string Check() => UnparseToString(Parse("(sub (var x) (var y))"));
            }

            [Language, PrunedLanguage(typeof(ExternalBase))]
            public static partial class ExternalPruned
            {
                public static string Check() => UnparseToString(Parse("(add (var x) 2)"));
            }
            """;
        var library = this.Generate(this.Compile("NanopassReferenceFixture", baseSource));
        byte[] image = this.Emit(library);
        var consumer = this.Generate(this.Compile(
            "NanopassConsumerFixture", consumerSource, MetadataReference.CreateFromImage(image)));
        byte[] compiled = this.Emit(consumer);

        var context = new AssemblyLoadContext("CrossAssemblyLanguages", isCollectible: true);
        try
        {
            using var libraryStream = new MemoryStream(image);
            using var consumerStream = new MemoryStream(compiled);
            Assembly baseAssembly = context.LoadFromStream(libraryStream);
            Assembly consumerAssembly = context.LoadFromStream(consumerStream);
            var baseType = baseAssembly.GetType("Cross.ExternalBase", throwOnError: true)!;
            var derivedType = consumerAssembly.GetType("Cross.ExternalDerived", throwOnError: true);
            var prunedType = consumerAssembly.GetType("Cross.ExternalPruned", throwOnError: true)!;
            var metadata = baseType.GetCustomAttribute<LanguageMetadataAttribute>();
            Assert.IsNotNull(metadata);
            Assert.IsNotNull(derivedType);
            Assert.StartsWith("(nanopass-v1 ", metadata.Metadata);
            Assert.AreEqual(
                "(sub pretty-x pretty-y)", derivedType.GetMethod("Check")!.Invoke(null, null));
            Assert.AreEqual("(add pretty-x 2)", prunedType.GetMethod("Check")!.Invoke(null, null));
            Assert.IsNull(prunedType.GetNestedType("Unused"));
            Assert.IsNull(derivedType.GetNestedType("Add"));
            var parse = derivedType.GetMethod("Parse", [typeof(string)])!;
            var error = Assert.ThrowsExactly<TargetInvocationException>(
                () => parse.Invoke(null, ["(var +)"]));
            Assert.IsInstanceOfType<NanopassParseException>(error.InnerException);
        }
        finally
        {
            context.Unload();
        }
    }

    [TestMethod]
    public void NestedLanguage_RebindsInheritedFieldsAndRemovesASynthesizedCase()
    {
        const string input = """
            namespace Cross;

            public partial class Fixture
            {
                [Language, Terminal(typeof(Symbol), "x")]
                [NonTerminalDef("Expr", ["e"], "(item x)", "(old)")]
                public static partial class Source;

                [Language, Extends(typeof(Source))]
                [Remove(typeof(Source.Old)), RemoveTerminal(typeof(Symbol))]
                [Terminal(typeof(long), "x", Name = "integer")]
                public static partial class Target
                {
                    public static string Check() => UnparseToString(Parse("(item 7)"));
                }
            }
            """;
        var compiled = this.Emit(this.Generate(this.Compile("NestedFixture", input)));
        var context = new AssemblyLoadContext("NestedLanguages", isCollectible: true);
        try
        {
            using var stream = new MemoryStream(compiled);
            var type = context.LoadFromStream(stream).GetType("Cross.Fixture+Target", true)!;
            Assert.AreEqual("(item 7)", type.GetMethod("Check")!.Invoke(null, null));
            Assert.IsNull(type.GetNestedType("Old"));
        }
        finally
        {
            context.Unload();
        }
    }

    [TestMethod]
    public void IncrementalModels_ReusesUnchangedLanguageAfterAnotherGrammarChanges()
    {
        const string independent = """
            [Language, NonTerminalDef("Expr", ["e"], "(unit)")]
            public static partial class Independent;
            """;
        const string dependent = """
            [Language, NonTerminalDef("Expr", ["e"], "(old)")]
            public static partial class Changed;
            """;
        var initial = this.Compile("Incremental", independent);
        var changingTree = CSharpSyntaxTree.ParseText(
            "using Hcoona.Nanopass;\n" + dependent,
            ParseOptions,
            "Changed.cs",
            cancellationToken: testContext.CancellationToken);
        initial = initial.AddSyntaxTrees(changingTree);

        GeneratorDriver driver = Driver(track: true).RunGenerators(
            initial, testContext.CancellationToken);
        Assert.IsEmpty(driver.GetRunResult().Diagnostics);
        var edited = initial.ReplaceSyntaxTree(
            changingTree,
            CSharpSyntaxTree.ParseText(
                changingTree.ToString().Replace("(old)", "(new)", StringComparison.Ordinal),
                ParseOptions,
                "Changed.cs",
                cancellationToken: testContext.CancellationToken));
        driver = driver.RunGenerators(edited, testContext.CancellationToken);

        var result = driver.GetRunResult().Results.Single();
        var reasons = result.TrackedSteps["LanguageModels"].SelectMany(static step => step.Outputs)
            .Select(static output => output.Reason).ToArray();
        Assert.Contains(IncrementalStepRunReason.Modified, reasons);
        Assert.Contains(static reason =>
            reason is IncrementalStepRunReason.Unchanged or IncrementalStepRunReason.Cached,
            reasons);
        Assert.HasCount(2, result.GeneratedSources);
        foreach (var generated in result.GeneratedSources)
        {
            Assert.StartsWith(
                "// <auto-generated/>\n#nullable enable\n"
                + "#pragma warning disable CA1716, CA1720, CS1591\n",
                generated.SourceText.ToString());
        }
    }

    [TestMethod]
    public void ProductionNames_ReserveUnionMembersAndUserLanguageMembers()
    {
        const string input = """
            [Language, Terminal(typeof(Symbol), "x", Predicate = nameof(IsVariable))]
            [NonTerminalDef("Expr", ["e"], "(value x)", "(raw-value x)", "(expr x)",
                "(is-variable x)")]
            [NonTerminalDef("Stmt", ["s"], "(value x)", "(raw-value x)")]
            [NonTerminalDef("ValueNode", ["v"], "(value x)")]
            public static partial class Reserved
            {
                public static Expr Make(Symbol arg) => new Expr.ValueNode(arg);
                public static Expr MakeRaw(Symbol arg) => new Expr.RawValueNode(arg);
                public static Expr MakeExpr(Symbol arg) => new Expr.ExprNode(arg);
                public static Expr MakeVariable(Symbol arg) => new IsVariableNode(arg);
                public static ValueNode MakeValue(Symbol arg) => new ValueNode.ValueNode2(arg);
            }

            public static partial class Reserved
            {
                private static bool IsVariable(Symbol symbol) => symbol.Name.Length > 0;
            }
            """;

        _ = this.Emit(this.Generate(this.Compile("ReservedNames", input)));
    }

    [TestMethod]
    public void DeclaredMetaVariables_ResolveUnescapedReferencesInAllDeclarationStyles()
    {
        const string input = """"
            [Language, Terminal(typeof(Symbol), "|x name|", "|x|")]
            [NonTerminalDef("Expr", ["|e|"], "(variable x)", "(step e)", "(leaf l)")]
            public static partial class Source
            {
                [NonTerminal("|l|")]
                public partial union Leaf(Unit, Symbol);

                public sealed record Unit(Symbol Word);
            }

            [Language, Extends(typeof(Source))]
            [ExtendNonTerminal("Expr", MetaVars = ["|e|", """\r"""], Add = ["(wrap r)"])]
            public static partial class Target;
            """";

        _ = this.Emit(this.Generate(this.Compile("CanonicalMetas", input)));
    }

    [TestMethod]
    public void HybridReferences_ResolveOptionalAndSequenceFieldsToGeneratedSibling()
    {
        const string input = """
            using System;

            [Language, Entry("Expr"), NonTerminalDef("Range", ["r"], "(unit)")]
            public static partial class Hybrid
            {
                public partial union Expr(Range, Box);
                public sealed record Box(Range? Item, Seq<Range> Items, Seq<Range?> OptionalItems);
                public static Expr Make() => new Box(null, [ParseRange("(unit)")], [null]);
            }
            """;

        _ = this.Emit(this.Generate(this.Compile("HybridUnion", input)));
    }

    [TestMethod]
    public void NativeReferences_ResolveInheritedNonterminalsInCasesAndFields()
    {
        const string input = """
            using System;

            [Language, NonTerminalDef("Expr", ["e"], "(unit)")]
            [NonTerminalDef("Range", ["r"], "(span)")]
            public static partial class Source;

            [Language, Extends(typeof(Source))]
            public static partial class Derived
            {
                [NonTerminal("e"), Cases(typeof(Range))]
                public readonly partial struct Expr
                {
                    public sealed record Box(Range? Span);
                }

                public static Expr Make() => ParseRange("(span)");
            }
            """;

        _ = this.Emit(this.Generate(this.Compile("InheritedCases", input)));
    }

    [TestMethod]
    [DataRow("[Remove(typeof(Source.Span))]")]
    [DataRow("""[ExtendNonTerminal("Range", Remove = ["(span)"])]""")]
    public void NativeReferences_UseTheBoundTypeWhenAnInheritedNonterminalIsRemoved(
        string removal)
    {
        var input = $$"""
            using System;

            [Language, NonTerminalDef("Expr", ["e"], "(unit)")]
            [NonTerminalDef("Range", ["r"], "(span)")]
            public static partial class Source;

            [Language, Extends(typeof(Source))]
            {{removal}}
            public static partial class Derived
            {
                [NonTerminal("e")]
                public readonly partial struct Expr;

                public sealed record Slice(Range Bounds);
                public static Expr Make() => new Slice(System.Range.All);
            }
            """;

        _ = this.Emit(this.Generate(this.Compile("RemovedSibling", input)));
    }

    [TestMethod]
    public void NativeRecords_RecreateAnInheritedEntryBeforeResolvingFields()
    {
        const string input = """
            using System;

            [Language, NonTerminalDef("Range", ["r"], "(span)")]
            public static partial class Source;

            [Language, Extends(typeof(Source)), Remove(typeof(Source.Range))]
            public static partial class Derived
            {
                public sealed record SubRange(Range Inner);
                public sealed record End;
                public static Range Make() => new SubRange(new End());
            }
            """;

        _ = this.Emit(this.Generate(this.Compile("RecreatedEntry", input)));
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void RecreatedNonterminals_UseOnlyNewMetavariablesAcrossSplitClauses(bool metaVarsFirst)
    {
        const string metaVars = """[ExtendNonTerminal("Expr", MetaVars = ["fresh"])]""";
        const string additions = """
            [ExtendNonTerminal("Expr", Add = ["(unit)", "(wrap fresh old)"])]
            """;
        var first = metaVarsFirst ? metaVars : additions;
        var second = metaVarsFirst ? additions : metaVars;
        var input = $$"""
            [Language, NonTerminalDef("Expr", ["old"], "(unit)")]
            public static partial class Source;

            [Language, Extends(typeof(Source)), Terminal(typeof(Symbol), "old")]
            [ExtendNonTerminal("Expr")]
            {{first}}
            {{second}}
            [ExtendNonTerminal("Expr", Remove = ["(unit)"])]
            public static partial class Derived
            {
                public static Expr Make() => new Wrap(new Unit(), new Symbol("datum"));
            }
            """;

        _ = this.Emit(this.Generate(this.Compile("RecreatedAliases", input)));
    }

    [TestMethod]
    public void RecreatedNativeNonterminal_UsesInheritedMetavariablesWhenUnannotated()
    {
        const string input = """
            [Language, NonTerminalDef("Expr", ["old"], "(unit)")]
            public static partial class Source;

            [Language, Extends(typeof(Source)), Remove(typeof(Source.Unit))]
            public static partial class Derived
            {
                public partial union Expr(Next, End);

                [Syntax("(next old)")]
                public sealed record Next(Expr Tail);
                public sealed record End;
            }
            """;

        _ = this.Emit(this.Generate(this.Compile("RecreatedNativeAliases", input)));
    }

    [TestMethod]
    public void NativeNonterminals_MergeInheritedClauseAndNativeMetavariables()
    {
        const string input = """
            [Language, NonTerminalDef("Expr", ["e", "body"], "(unit)")]
            public static partial class Source;

            [Language, Extends(typeof(Source))]
            [ExtendNonTerminal("Expr", MetaVars = ["hybrid"], Add = ["(call body hybrid native)"])]
            public static partial class Derived
            {
                [NonTerminal("e", "native")]
                public readonly partial struct Expr;
            }
            """;

        _ = this.Emit(this.Generate(this.Compile("MergedNativeMetavariables", input)));
    }

    [TestMethod]
    public void NativeRecords_RouteUnclaimedProductionsToTheExplicitEntry()
    {
        const string input = """
            [Language, NonTerminalDef("Expr", ["e"], "(unit)")]
            public static partial class Source;

            [Language, Extends(typeof(Source)), Entry("Fresh")]
            [ExtendNonTerminal("Fresh", MetaVars = ["f"])]
            public static partial class Derived
            {
                public sealed record Start;
                public static Fresh Make() => new Start();
                public static Fresh Read() => Parse("(start)");
            }
            """;

        _ = this.Emit(this.Generate(this.Compile("ExplicitNativeEntry", input)));
    }

    [TestMethod]
    public void NativeRecords_ResolveSyntaxAfterSubtractionsFreeTheTerminalMetavariable()
    {
        const string input = """
            using System;

            [Language, NonTerminalDef("Expr", ["e"], "(unit)")]
            [NonTerminalDef("Range", ["r"], "(span)")]
            public static partial class Source;

            [Language, Extends(typeof(Source)), Remove(typeof(Source.Span))]
            [Terminal(typeof(Range), "r")]
            [ExtendNonTerminal("Expr", Add = ["(unit)"])]
            [ExtendNonTerminal("Expr", Remove = ["(unit)"])]
            public static partial class Derived
            {
                [Syntax("(slice r)")]
                public sealed record Slice(Range R);
                public static Expr Make() => new Slice(System.Range.All);
            }
            """;

        _ = this.Emit(this.Generate(this.Compile("ReusedMetavariable", input)));
    }

    [TestMethod]
    public void NativeReferences_PreserveAnInnerTerminalThatShadowsAGeneratedSibling()
    {
        const string input = """
            [Language, Entry("Expr"), NonTerminalDef("Opaque", ["o"], "(outer)")]
            public static partial class Hybrid
            {
                [NonTerminal("e")]
                public readonly partial struct Expr
                {
                    public readonly record struct Opaque(int Value);
                    public sealed record Box(Opaque Value, Seq<Opaque?> Items);
                }

                public static Expr Make() => new Expr.Box(new Expr.Opaque(1), [null]);
            }
            """;

        _ = this.Emit(this.Generate(this.Compile("ShadowedTerminal", input)));
    }

    private CSharpCompilation Compile(
        string name, string declaration, params MetadataReference[] additional) =>
        CSharpCompilation.Create(
            name,
            [CSharpSyntaxTree.ParseText(
                "using Hcoona.Nanopass;\n" + declaration,
                ParseOptions,
                name + ".cs",
                cancellationToken: testContext.CancellationToken)],
            PlatformReferences.AddRange(additional),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

    private static CSharpGeneratorDriver Driver(bool track = false) => CSharpGeneratorDriver.Create(
        [new NanopassGenerator().AsSourceGenerator()],
        parseOptions: ParseOptions,
        driverOptions: new GeneratorDriverOptions(default, trackIncrementalGeneratorSteps: track));

    private Compilation Generate(CSharpCompilation compilation)
    {
        _ = Driver().RunGeneratorsAndUpdateCompilation(
            compilation, out var output, out var diagnostics, testContext.CancellationToken);
        Assert.IsEmpty(diagnostics, Describe(diagnostics));
        return output;
    }

    private byte[] Emit(Compilation compilation)
    {
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream, cancellationToken: testContext.CancellationToken);
        Assert.IsTrue(result.Success, Describe(result.Diagnostics));
        return stream.ToArray();
    }

    private static string Describe(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics);
}
