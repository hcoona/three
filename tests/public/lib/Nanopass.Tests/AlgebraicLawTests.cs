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

using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text;
using CsCheck;
using Hcoona.Nanopass.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Hcoona.Nanopass.Tests;

/// <summary>
/// Property-based algebraic law tests (<c>CsCheck</c>) and Z3 formal specification
/// differential regression tests for <c>Hcoona.Nanopass</c>.
/// </summary>
/// <param name="testContext">The active test execution context.</param>
[TestClass]
public sealed class AlgebraicLawTests(TestContext testContext)
{
    private const string DeterministicSeed = "nanopass1779";

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

    private static readonly Gen<Symbol> GenIdentifierSymbol =
        Gen.OneOf("v0", "v1", "v2", "k_alpha", "k_beta", "sym-x", "item?")
            .Select(static name => new Symbol(name));
    private readonly ConcurrentDictionary<string, EvaluationResult> evaluationCache =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Law 1a: Auto-synthesized identity passes and composed identity pipelines preserve
    /// AST structural equality, node counts, and raw S-expression unparsing across
    /// mutually recursive nonterminals, 3-hop subtype chains, maybe-sequences, nested
    /// 2D sequences, and dotted improper-list tails (<c>VEC-D2-06</c>, <c>VEC-D3-01</c>,
    /// <c>VEC-D3-04</c>).
    /// </summary>
    [TestMethod]
    public void IdentityPassLaw_PreservesRecursiveAstAndPipelineComposition()
    {
        var idPass = new AlgebraicIrIdPass();
        var contextualIdPass = new AlgebraicIrContextualIdPass();
        var pipeline = Pipeline.Start<AlgebraicIrLanguage.Expr>()
            .Then(idPass)
            .Then(contextualIdPass);

        BuildAlgebraicIrExprGenerator().Sample(
            ast =>
            {
                var single = idPass.Run(ast);
                var composed = pipeline.Run(ast);

                Assert.AreEqual(ast, single);
                Assert.AreEqual(ast, composed);
                Assert.AreEqual(
                    AlgebraicIrLanguage.CountNodes(ast),
                    AlgebraicIrLanguage.CountNodes(composed));
                Assert.AreEqual(
                    AlgebraicIrLanguage.Unparse(ast, raw: true),
                    AlgebraicIrLanguage.Unparse(composed, raw: true));
            },
            seed: DeterministicSeed,
            iter: 200,
            threads: 1,
            print: static ast => AlgebraicIrLanguage.UnparseToString(ast, raw: true));
    }

    /// <summary>
    /// Law 1b: Auto-synthesized identity passes over equal-length (<c>VEC-D2-05</c>) and
    /// unequal-length (<c>VEC-D2-01</c>) subtype diamonds preserve exact intermediate union
    /// wrappers and prettified unparsing rather than collapsing to shortest target paths.
    /// </summary>
    [TestMethod]
    public void IdentityPassLaw_PreservesEqualAndUnequalSubtypeDiamondWrappers()
    {
        var equalPass = new EqualDiamondIdPass();
        var unequalPass = new UnequalDiamondIdPass();
        var twoInclusionsPass = new TwoInclusionsIdPass();

        var genEqualDiamond = Gen.Select(GenIdentifierSymbol, Gen.Int[0, 3])
            .Select(static (sym, variant) =>
            {
                EqualDiamondLanguage.A atom = sym;
                return variant switch
                {
                    0 => (EqualDiamondLanguage.D)(EqualDiamondLanguage.B)atom,
                    1 => (EqualDiamondLanguage.D)(EqualDiamondLanguage.C)atom,
                    2 => (EqualDiamondLanguage.D)(EqualDiamondLanguage.B)
                        new EqualDiamondLanguage.Btag(sym),
                    _ => (EqualDiamondLanguage.D)(EqualDiamondLanguage.C)
                        new EqualDiamondLanguage.Ctag(sym),
                };
            });

        genEqualDiamond.Sample(
            ast =>
            {
                var actual = equalPass.Run(ast);
                Assert.AreEqual(ast, actual);
                Assert.AreEqual(ast.Value!.GetType(), actual.Value!.GetType());
            },
            seed: DeterministicSeed,
            iter: 150,
            threads: 1);

        var genUnequalDiamond = Gen.Select(Gen.Long[-10_000, 10_000], Gen.Int[0, 2])
            .Select(static (n, variant) =>
            {
                UnequalDiamondLanguage.A atom = n;
                return variant switch
                {
                    0 => (UnequalDiamondLanguage.D)(UnequalDiamondLanguage.B)atom,
                    1 => (UnequalDiamondLanguage.D)atom,
                    _ => (UnequalDiamondLanguage.D)(UnequalDiamondLanguage.B)
                        new UnequalDiamondLanguage.Wrap(n),
                };
            });

        genUnequalDiamond.Sample(
            ast =>
            {
                var actual = unequalPass.Run(ast);
                Assert.AreEqual(ast, actual);
                Assert.AreEqual(ast.Value!.GetType(), actual.Value!.GetType());
            },
            seed: DeterministicSeed,
            iter: 150,
            threads: 1);

        var parsedViaB = UnequalDiamondLanguage.Parse("42");
        Assert.IsInstanceOfType<UnequalDiamondLanguage.B>(parsedViaB.Value);
        Assert.AreEqual(parsedViaB, unequalPass.Run(parsedViaB));

        TwoInclusionsLanguage.W directW = new Symbol("datum");
        TwoInclusionsLanguage.Expr viaDirectW = directW;
        TwoInclusionsLanguage.U directU = new Symbol("datum");
        TwoInclusionsLanguage.Expr viaU = directU;
        TwoInclusionsLanguage.V intermediateV = directW;
        TwoInclusionsLanguage.Expr dup = intermediateV;

        foreach (var candidate in new[] { viaDirectW, viaU, dup })
        {
            var lowered = twoInclusionsPass.Run(candidate);
            Assert.AreEqual(candidate, lowered);
            Assert.AreEqual(
                TwoInclusionsLanguage.UnparseToString(candidate),
                TwoInclusionsLanguage.UnparseToString(lowered));
        }
    }

    /// <summary>
    /// Law 1c: Coinductive identity pass synthesis preserves ASTs on multi-node mutual
    /// inclusion cycles (<c>GraphLanguage</c>, <c>VEC-D2-03</c>) and falls back cleanly when
    /// a derived language migrates a production upward out of a subtype into its supertype.
    /// </summary>
    [TestMethod]
    public void IdentityPassLaw_SupportsMutualInclusionCyclesAndUpwardProductionMigration()
    {
        var cyclePass = new MutualCycleIdPass();
        var genCycleExpr = Gen.Recursive<MutualCycleLanguage.Expr>((depth, self) =>
        {
            var leaf = Gen.Long[-1_000, 1_000].Select(
                static n => (MutualCycleLanguage.Expr)new MutualCycleLanguage.Leaf(n));
            var boolForm = Gen.Bool.Select(
                static b => (MutualCycleLanguage.Expr)(MutualCycleLanguage.Form)b);
            if (depth >= 3)
            {
                return Gen.OneOf(leaf, boolForm);
            }

            var wrappedCycle = self.Select(
                static inner => (MutualCycleLanguage.Expr)(MutualCycleLanguage.Form)inner);
            var fork = Gen.Select(
                self,
                self,
                static (left, right) =>
                    (MutualCycleLanguage.Expr)new MutualCycleLanguage.Fork(left, right));
            return Gen.OneOf(leaf, boolForm, wrappedCycle, fork);
        });

        genCycleExpr.Sample(
            ast =>
            {
                var lowered = cyclePass.Run(ast);
                Assert.AreEqual(ast, lowered);
                Assert.AreEqual(
                    MutualCycleLanguage.UnparseToString(ast, raw: true),
                    MutualCycleLanguage.UnparseToString(lowered, raw: true));
            },
            seed: DeterministicSeed,
            iter: 150,
            threads: 1);

        var migrationPass = new UpwardMigratedAtomPass();
        UpwardSourceLanguage.Expr tagged =
            (UpwardSourceLanguage.Atom)new UpwardSourceLanguage.Tagged(new Symbol("k"));
        UpwardTargetLanguage.Expr migrated = migrationPass.Run(tagged);
        Assert.AreEqual("(tagged k)", UpwardTargetLanguage.UnparseToString(migrated, raw: true));

        var noXvalPass = new NoXvalFieldSynthesisPass();
        Assert.AreEqual(((NoXvalFieldSynthesisLanguage.Sub)107L, 1), noXvalPass.InspectSub(7L));
        NoXvalFieldSynthesisLanguage.Expr wrapped = new NoXvalFieldSynthesisLanguage.Wrap(7L);
        Assert.AreEqual(
            "(wrap 7)",
            NoXvalFieldSynthesisLanguage.UnparseToString(noXvalPass.Run(wrapped), raw: true));
    }

    /// <summary>
    /// Law 2a: <see cref="SExpr.Parse(string)"/> and <see cref="SExpr.Format()"/> form a
    /// lossless round-trip over arbitrary generated S-expression trees including escaped
    /// symbols, finite floats, strings, characters, vectors, proper lists, and dotted pairs.
    /// </summary>
    [TestMethod]
    public void RoundTripLaw_SExprFormatAndParseAreExactInverses()
    {
        BuildSExprGenerator().Sample(
            sexpr =>
            {
                var reparsed = SExpr.Parse(sexpr.Format());
                Assert.AreEqual(sexpr, reparsed);
                Assert.AreEqual(sexpr.GetHashCode(), reparsed.GetHashCode());
            },
            seed: DeterministicSeed,
            iter: 250,
            threads: 1,
            print: static sexpr => sexpr.Format());
    }

    /// <summary>
    /// Law 2b: Cross-assembly <c>[LanguageMetadata]</c> encoding and decoding preserves
    /// arbitrary generated <c>[Language]</c> schemas (namespaces, pretty templates, v1/v2
    /// pretty handlers, terminal predicates, maybe-fields, and nested sequences) when
    /// consumed via PE <see cref="MetadataReference"/> by downstream <c>[Extends]</c>,
    /// <c>[PrunedLanguage]</c>, and <c>[Pass]</c> declarations, and executes cleanly inside
    /// a collectible <see cref="AssemblyLoadContext"/>.
    /// </summary>
    [TestMethod]
    public void RoundTripLaw_CrossAssemblyLanguageMetadataPreservesSchemaAndPasses()
    {
        BuildCrossAssemblyMetadataCaseGenerator().Sample(
            item =>
            {
                var (name, entryName, prettyMode, includeMaybe, includeNestedSeq,
                    upstreamSource, downstreamSource) = item;
                var upstream = this.EvaluateSource(name + "_Upstream", upstreamSource);
                Assert.IsEmpty(upstream.NanopassErrors, Describe(upstream.NanopassErrors));
                Assert.IsEmpty(upstream.CompilerErrors, Describe(upstream.CompilerErrors));

                using var upstreamPeStream = new MemoryStream();
                var upstreamEmit = upstream.Compilation.Emit(
                    upstreamPeStream, cancellationToken: testContext.CancellationToken);
                Assert.IsTrue(
                    upstreamEmit.Success,
                    string.Join(Environment.NewLine, upstreamEmit.Diagnostics));
                byte[] upstreamBytes = upstreamPeStream.ToArray();
                var upstreamRef = MetadataReference.CreateFromImage(upstreamBytes);

                var downstream = this.EvaluateSource(
                    name + "_Downstream", downstreamSource, upstreamRef);
                Assert.IsEmpty(downstream.NanopassErrors, Describe(downstream.NanopassErrors));
                Assert.IsEmpty(downstream.CompilerErrors, Describe(downstream.CompilerErrors));

                using var downstreamPeStream = new MemoryStream();
                var downstreamEmit = downstream.Compilation.Emit(
                    downstreamPeStream, cancellationToken: testContext.CancellationToken);
                Assert.IsTrue(
                    downstreamEmit.Success,
                    string.Join(Environment.NewLine, downstreamEmit.Diagnostics));

                ExecuteCrossAssemblyMetadataChecks(
                    name,
                    upstreamBytes,
                    downstreamPeStream.ToArray(),
                    entryName,
                    prettyMode,
                    includeMaybe,
                    includeNestedSeq);
            },
            seed: DeterministicSeed,
            iter: 16,
            threads: 1);
    }

    /// <summary>
    /// Law 2c: Under the Z3 <c>NoShadowing</c> precondition, <c>Parse(Unparse(ast, raw: true))</c>
    /// is an exact identity on generated <see cref="AlgebraicIrLanguage"/> ASTs, while Z3
    /// boundary witnesses <c>VEC-D4-01..04</c> behave as specified.
    /// </summary>
    [TestMethod]
    public void RoundTripLaw_ParseAfterRawUnparseHoldsUnderNoShadowingAndPinsBoundaryWitnesses()
    {
        BuildAlgebraicIrExprGenerator().Sample(
            ast =>
            {
                Assert.IsTrue(AlgebraicIrLanguage.IsLanguageTerm(ast));
                var raw = AlgebraicIrLanguage.Unparse(ast, raw: true);
                var reparsed = AlgebraicIrLanguage.Parse(raw);
                Assert.AreEqual(ast, reparsed);
            },
            seed: DeterministicSeed,
            iter: 200,
            threads: 1,
            print: static ast => AlgebraicIrLanguage.UnparseToString(ast, raw: true));

        // VEC-D4-01: Sub with an implicit list form is demoted to phase 4 after parent (e1 e2)
        // (phase 3), so "(tag a)" matches Expr's 2-element implicit production rather than Sub.Tag.
        var vecD401Parsed = ImplicitSubDispatchLanguage.Parse("(tag a)");
        Assert.IsInstanceOfType<ImplicitSubDispatchLanguage.Expr.Implicit>(vecD401Parsed.Value);

        // VEC-D4-02: Sub without implicit list forms runs at phase 0 before parent (e1 e2),
        // parsing "(tag a)" via Sub.Tag.
        var vecD402Parsed = NonImplicitSubDispatchLanguage.Parse("(tag a)");
        Assert.IsInstanceOfType<NonImplicitSubDispatchLanguage.Sub>(vecD402Parsed.Value);

        // VEC-D4-03: Tag-erasing pretty formats collide under raw: false while raw: true is
        // injective.
        PrettyTagEraseLanguage.Expr bindNode =
            new PrettyTagEraseLanguage.Bind(new Symbol("f"), 1L, 2L);
        PrettyTagEraseLanguage.Expr callNode =
            new PrettyTagEraseLanguage.Implicit(new Symbol("f"), 1L, 2L);
        Assert.AreEqual(
            PrettyTagEraseLanguage.UnparseToString(bindNode, raw: false),
            PrettyTagEraseLanguage.UnparseToString(callNode, raw: false));
        Assert.AreNotEqual(
            PrettyTagEraseLanguage.UnparseToString(bindNode, raw: true),
            PrettyTagEraseLanguage.UnparseToString(callNode, raw: true));
        Assert.AreEqual(
            bindNode,
            PrettyTagEraseLanguage.Parse(PrettyTagEraseLanguage.Unparse(bindNode, raw: true)));

        // VEC-D4-04: Keyword-excluding terminal predicate satisfies NoShadowing and rejects
        // reserved heads.
        Assert.ThrowsExactly<NanopassParseException>(() => AlgebraicIrLanguage.Parse("add"));
        AlgebraicIrLanguage.Expr safeAtom1 = (AlgebraicIrLanguage.Prim)(AlgebraicIrLanguage.Atom)1L;
        AlgebraicIrLanguage.Expr safeAtom2 = (AlgebraicIrLanguage.Prim)(AlgebraicIrLanguage.Atom)2L;
        AlgebraicIrLanguage.Expr safeNode = new AlgebraicIrLanguage.Add(safeAtom1, safeAtom2);
        Assert.AreEqual(
            safeNode,
            AlgebraicIrLanguage.Parse(AlgebraicIrLanguage.Unparse(safeNode, raw: true)));

        // Additional boundary regression: (maybe bool) maps #f to null on parse.
        MaybeBoolShadowLanguage.Expr falseBox = new MaybeBoolShadowLanguage.Box(false);
        Assert.AreEqual("(box #f)", MaybeBoolShadowLanguage.UnparseToString(falseBox, raw: true));
        var reparsedBox = MaybeBoolShadowLanguage.Parse("(box #f)");
        Assert.IsNull(Assert.IsInstanceOfType<MaybeBoolShadowLanguage.Box>(reparsedBox.Value).B);

        // Additional boundary regression: Prune omits (terminals) on 0 surviving terminals while
        // keeping entry-reachable unproductive nonterminals and dropping unreachable ones.
        string prunedOnce = UnreachableCycleLanguage.Prune().Format();
        Assert.DoesNotContain("(terminals", prunedOnce);
        Assert.Contains("DeadA", prunedOnce);
        Assert.DoesNotContain("Orphan", prunedOnce);
    }

    /// <summary>
    /// Law 3a: Analyzer-Compiler Soundness Alignment Law. Across randomized <c>[Language]</c>,
    /// <c>[Extends]</c>, <c>[PrunedLanguage]</c>, and <c>[Pass]</c> compilations evaluated
    /// synchronously inside <c>CsCheck</c> shrinking callbacks, zero <c>NPxxxx</c> errors
    /// strictly implies zero C# compiler (<c>CSxxxx</c>) errors, and rejected Nanopass schemas
    /// emit valid fallback stubs without cascading <c>CSxxxx</c> errors.
    /// </summary>
    [TestMethod]
    public void AnalyzerCompilerAlignmentLaw_ZeroNanopassErrorsImpliesZeroCSharpErrors()
    {
        int cleanCount = 0;
        int rejectedCount = 0;

        BuildSchemaCaseGenerator().Sample(
            schema =>
            {
                var result = this.evaluationCache.GetOrAdd(
                    schema.Name,
                    _ => this.EvaluateSource(schema.Name, schema.Source));

                if (schema.ExpectClean)
                {
                    Assert.IsEmpty(
                        result.NanopassErrors,
                        $"Expected 0 NPxxxx errors for {schema.Name}:\n{schema.Source}\n" +
                        Describe(result.NanopassErrors));
                    cleanCount++;
                }
                else if (!result.NanopassErrors.IsEmpty)
                {
                    rejectedCount++;
                }

                string npIds = string.Join(", ", result.NanopassErrors.Select(static d => d.Id));
                Assert.IsEmpty(
                    result.CompilerErrors,
                    $"Leaked CSxxxx errors for {schema.Name} (NP errors: [{npIds}]):\n" +
                    $"{schema.Source}\n{Describe(result.CompilerErrors)}");
            },
            seed: DeterministicSeed,
            iter: 48,
            threads: 1);

        Assert.IsGreaterThan(0, cleanCount);
        Assert.IsGreaterThan(0, rejectedCount);
    }

    /// <summary>
    /// Law 3b: Pins all Z3 solver-decoded differential vectors across Domains 1-3
    /// (<c>VEC-D1-01..05</c>, <c>VEC-D2-01..06</c>, <c>VEC-D3-01..06</c>) plus empty-nonterminal
    /// and pure-cycle boundary regressions, verifying exact <c>NPxxxx</c> diagnostics, runtime
    /// semantics on non-diagnostic vectors, and zero leaked <c>CSxxxx</c> compiler errors.
    /// </summary>
    /// <param name="caseId">The Z3 vector or regression identifier.</param>
    /// <param name="expectedDiagnosticId">
    /// Expected <c>NPxxxx</c> error ID, or empty if clean.
    /// </param>
    /// <param name="source">The C# source under test.</param>
    [TestMethod]
    [DataRow(
        "VEC-D1-01",
        "",
        """
        [Language, Terminal(typeof(long), "n")]
        [Entry("Expr")]
        [NonTerminalDef("Expr", ["e"], "n", "(add e1 e2)")]
        public static partial class L0;

        [Language, Extends(typeof(L0))]
        [ExtendNonTerminal("Expr", Add = ["(neg e)"])]
        public static partial class L1;

        [Language, Extends(typeof(L1))]
        [ExtendNonTerminal("Expr", Remove = ["(neg e)"])]
        public static partial class L2;
        """)]
    [DataRow(
        "VEC-D1-02",
        "NP0104",
        """
        [Language]
        [Terminal(typeof(long), "x")]
        [Terminal(typeof(Symbol), "x")]
        [Entry("Expr")]
        [NonTerminalDef("Expr", ["e"], "x")]
        public static partial class DupMetaVarLang;
        """)]
    [DataRow(
        "VEC-D1-03",
        "",
        """
        [Language, Terminal(typeof(long), "n")]
        [Entry("Expr")]
        [NonTerminalDef("Expr", ["e"], "(unit)", "d")]
        [NonTerminalDef("Dead", ["d"], "(loop d)")]
        [NonTerminalDef("UnreachedA", ["ua"], "(step ub)", "n")]
        [NonTerminalDef("UnreachedB", ["ub"], "(back ua)")]
        public static partial class UnproductiveSource;

        [Language, PrunedLanguage(typeof(UnproductiveSource))]
        public static partial class PrunedUnproductive;
        """)]
    [DataRow(
        "VEC-D1-04",
        "",
        """
        [Language, Terminal(typeof(Symbol), "x", "y")]
        [Entry("Expr")]
        [NonTerminalDef("Expr", ["e"], "x", "(use y)")]
        public static partial class MultiMetaBase;

        [Language, Extends(typeof(MultiMetaBase))]
        [RemoveTerminal(typeof(Symbol)), Terminal(typeof(Symbol), "x")]
        [ExtendNonTerminal("Expr", Remove = ["(use y)"])]
        public static partial class PartialMetaRemoved;
        """)]
    [DataRow(
        "VEC-D1-05",
        "NP0104",
        """
        [Language, Terminal(typeof(Symbol), "x")]
        [Entry("Expr")]
        [NonTerminalDef("Expr", ["e"], "b1", "b2")]
        [NonTerminalDef("Body", ["b"], "x")]
        public static partial class DuplicateNonTerminalInclusion;
        """)]
    [DataRow(
        "VEC-D2-01",
        "",
        """
        [Language, Terminal(typeof(long), "n")]
        [Entry("D")]
        [NonTerminalDef("D", ["d"], "a", "b")]
        [NonTerminalDef("B", ["b"], "a", "(wrap n)")]
        [NonTerminalDef("A", ["a"], "n")]
        public static partial class UnequalDiamond;

        [Language, Terminal(typeof(long), "n")]
        [Entry("D")]
        [NonTerminalDef("D", ["d"], "a", "(wrap n)")]
        [NonTerminalDef("A", ["a"], "n")]
        public static partial class FlatUnequalSource;

        [Pass(typeof(FlatUnequalSource), typeof(UnequalDiamond))]
        public partial class UnequalDiamondCoercePass
        {
            [Processor("A", "A")]
            public partial UnequalDiamond.A LowerA(FlatUnequalSource.A term);
        }
        """)]
    [DataRow(
        "VEC-D2-02",
        "NP0105",
        """
        [Language, Terminal(typeof(Symbol), "x")]
        [NonTerminalDef("Expr", ["e"], "e", "x")]
        public static partial class DirectSelfInclusion;
        """)]
    [DataRow(
        "VEC-D2-03",
        "",
        """
        [Language, Terminal(typeof(long), "n")]
        [Entry("Expr")]
        [NonTerminalDef("Expr", ["e"], "f", "(neg e)")]
        [NonTerminalDef("Form", ["f"], "e", "n")]
        public static partial class TwoNodeCycle;

        [Pass(typeof(TwoNodeCycle), typeof(TwoNodeCycle))]
        public partial class TwoNodeCyclePass;
        """)]
    [DataRow(
        "VEC-D2-04",
        "NP1003",
        """
        [Language, Terminal(typeof(Symbol), "x")]
        [Entry("D")]
        [NonTerminalDef("D", ["d"], "b", "c")]
        [NonTerminalDef("B", ["b"], "a", "(btag x)")]
        [NonTerminalDef("C", ["c"], "a", "(ctag x)")]
        [NonTerminalDef("A", ["a"], "x")]
        public static partial class DiaTarget;

        [Language, Terminal(typeof(Symbol), "x")]
        [Entry("D")]
        [NonTerminalDef("D", ["d"], "a", "(btag x)", "(ctag x)")]
        [NonTerminalDef("A", ["a"], "x")]
        public static partial class FlatSource;

        [Pass(typeof(FlatSource), typeof(DiaTarget))]
        public partial class AmbiguousDiamondCoercionPass
        {
            [Processor("A", "A")]
            public partial DiaTarget.A LowerA(FlatSource.A term);
        }
        """)]
    [DataRow(
        "VEC-D2-05",
        "",
        """
        [Language, Terminal(typeof(Symbol), "x")]
        [Entry("D")]
        [NonTerminalDef("D", ["d"], "b", "c")]
        [NonTerminalDef("B", ["b"], "a", "(btag x)")]
        [NonTerminalDef("C", ["c"], "a", "(ctag x)")]
        [NonTerminalDef("A", ["a"], "x")]
        public static partial class EqualDia;

        [Pass(typeof(EqualDia), typeof(EqualDia))]
        public partial class EqualDiaIdPass;
        """)]
    [DataRow(
        "VEC-D2-06",
        "",
        """
        [Language, Terminal(typeof(long), "n")]
        [Entry("Top")]
        [NonTerminalDef("Top", ["t"], "m")]
        [NonTerminalDef("Mid", ["m"], "a", "(keep n)")]
        [NonTerminalDef("Atom", ["a"], "n")]
        public static partial class TransitiveSubtype;

        [Language, Terminal(typeof(long), "n")]
        [Entry("Top")]
        [NonTerminalDef("Top", ["t"], "a", "(keep n)")]
        [NonTerminalDef("Atom", ["a"], "n")]
        public static partial class FlatTopSource;

        [Pass(typeof(FlatTopSource), typeof(TransitiveSubtype))]
        public partial class TransitiveSubtypePass
        {
            [Processor("Atom", "Atom")]
            public partial TransitiveSubtype.Atom LowerAtom(FlatTopSource.Atom term);
        }
        """)]
    [DataRow(
        "VEC-D3-01",
        "",
        """
        [Language, Terminal(typeof(long), "n")]
        [Entry("Expr")]
        [NonTerminalDef("Expr", ["e"], "n", "(neg e)", "(seq e* ...)")]
        public static partial class HomomorphicLang;

        [Pass(typeof(HomomorphicLang), typeof(HomomorphicLang))]
        public partial class HomomorphicAutoPass;
        """)]
    [DataRow(
        "VEC-D3-02",
        "NP1001",
        """
        [Language, Terminal(typeof(long), "n")]
        [Entry("Expr")]
        [NonTerminalDef("Expr", ["e"], "n", "(neg e)")]
        public static partial class GuardedSrc;

        [Language, Extends(typeof(GuardedSrc))]
        [ExtendNonTerminal("Expr", Remove = ["(neg e)"])]
        public static partial class GuardedDst;

        [Pass(typeof(GuardedSrc), typeof(GuardedDst))]
        public partial class GuardWithoutFallbackPass
        {
            private static bool IsZero(GuardedSrc.Expr e) => e.Value is 0L;

            [Rewrite(typeof(GuardedSrc.Neg), Guard = nameof(IsZero))]
            private static GuardedDst.Expr FoldZero(GuardedSrc.Expr e) => 0L;
        }
        """)]
    [DataRow(
        "VEC-D3-03",
        "NP1001",
        """
        [Language, Terminal(typeof(long), "n")]
        [Entry("Expr")]
        [NonTerminalDef("Expr", ["e"], "n", "(neg e)")]
        public static partial class RemovedSrc;

        [Language, Extends(typeof(RemovedSrc))]
        [ExtendNonTerminal("Expr", Remove = ["(neg e)"])]
        public static partial class RemovedDst;

        [Pass(typeof(RemovedSrc), typeof(RemovedDst))]
        public partial class MissingRemovedClausePass;
        """)]
    [DataRow(
        "VEC-D3-04",
        "",
        """
        [Language, Terminal(typeof(long), "n")]
        [Entry("Expr")]
        [NonTerminalDef("Expr", ["e"], "n", "(neg e)")]
        public static partial class RetainedGuardLang;

        [Pass(typeof(RetainedGuardLang), typeof(RetainedGuardLang))]
        public partial class RetainedGuardPass
        {
            private static bool IsZero(RetainedGuardLang.Expr e) => e.Value is 0L;

            [Rewrite(typeof(RetainedGuardLang.Neg), Guard = nameof(IsZero))]
            private static RetainedGuardLang.Expr FoldZero(RetainedGuardLang.Expr e) => 0L;
        }
        """)]
    [DataRow(
        "VEC-D3-05",
        "NP1001",
        """
        [Language, Terminal(typeof(long), "n")]
        [Entry("Expr")]
        [NonTerminalDef("Expr", ["e"], "(wrap s)")]
        [NonTerminalDef("Sub", ["s"], "n", "(gone n)")]
        public static partial class ExtraSrc;

        [Language, Extends(typeof(ExtraSrc))]
        [ExtendNonTerminal("Sub", Remove = ["(gone n)"])]
        public static partial class ExtraDst;

        [Pass(typeof(ExtraSrc), typeof(ExtraDst))]
        public partial class UnboundExtraParentPass
        {
            [Processor("Sub", "Sub")]
            public partial (ExtraDst.Sub, int) LowerSub(ExtraSrc.Sub s);

            [Rewrite(typeof(ExtraSrc.Gone), Processor = nameof(LowerSub))]
            private static (ExtraDst.Sub, int) HandleGone(long n) => (n, 1);

            [Rewrite(typeof(long), Processor = nameof(LowerSub))]
            private static (ExtraDst.Sub, int) HandleNum(long n) => (n, 0);
        }
        """)]
    [DataRow(
        "VEC-D3-06",
        "",
        """
        [Language]
        [Entry("Root")]
        [NonTerminalDef("Root", ["r"], "a")]
        [NonTerminalDef("A", ["a"], "(wrap b)")]
        [NonTerminalDef("B", ["b"], "(keep)", "(gone)")]
        public static partial class NestedMigrationSrc;

        [Language, Extends(typeof(NestedMigrationSrc))]
        [ExtendNonTerminal("B", Remove = ["(gone)"])]
        [ExtendNonTerminal("Root", Add = ["b", "m", "(gone)"])]
        [NonTerminalDef("Alt", ["m"], "(wrap r)")]
        public static partial class NestedMigrationDst;

        [Pass(typeof(NestedMigrationSrc), typeof(NestedMigrationDst))]
        public partial class NestedMigrationPass;
        """)]
    [DataRow(
        "REG-EMPTY-REF",
        "NP0105",
        """
        [Language, Terminal(typeof(long), "n")]
        [Entry("Expr")]
        [NonTerminalDef("Expr", ["e"], "(wrap a)")]
        [NonTerminalDef("Atom", ["a"], "n")]
        public static partial class BaseLang;

        [Language, Extends(typeof(BaseLang))]
        [ExtendNonTerminal("Atom", Remove = ["n"])]
        public static partial class DerivedEmptyReferenced;
        """)]
    [DataRow(
        "REG-EMPTY-ENTRY",
        "NP0102",
        """
        [Language, Terminal(typeof(long), "n")]
        [Entry("Expr")]
        [NonTerminalDef("Expr", ["e"], "n")]
        [NonTerminalDef("Aux", ["a"], "(aux n)")]
        public static partial class BaseEntryLang;

        [Language, Extends(typeof(BaseEntryLang))]
        [ExtendNonTerminal("Expr", Remove = ["n"])]
        public static partial class DerivedEmptyEntry;
        """)]
    [DataRow(
        "REG-PURE-SELF",
        "NP0105",
        """
        [Language]
        [NonTerminalDef("Value", ["v"], "v")]
        public static partial class PureSelfCycle;
        """)]
    [DataRow(
        "REG-UNBINDABLE-OUTER",
        "",
        """
        [Language, Terminal(typeof(long), "n")]
        [Entry("Root")]
        [NonTerminalDef("Root", ["r"], "a", "m")]
        [NonTerminalDef("A", ["a"], "(wrap n)")]
        [NonTerminalDef("Alt", ["m"], "(wrap n)", "(alt_only n)")]
        public static partial class OuterUnbindableLang;

        [Pass(typeof(OuterUnbindableLang), typeof(OuterUnbindableLang))]
        public partial class OuterUnbindablePass
        {
            [Processor("A", "Alt")]
            public partial OuterUnbindableLang.Alt LowerAWithCtx(
                OuterUnbindableLang.A a, int requiredCtx);
        }
        """)]
    [DataRow(
        "REG-TUPLE-REWRITE-ISOLATION",
        "",
        """
        [Language]
        [Entry("Root")]
        [NonTerminalDef("Root", ["r"], "a")]
        [NonTerminalDef("A", ["a"], "(wrap b)")]
        [NonTerminalDef("B", ["b"], "(keep)", "(gone)")]
        public static partial class TupleIsoSrc;

        [Language, Extends(typeof(TupleIsoSrc))]
        [ExtendNonTerminal("B", Remove = ["(gone)"])]
        [ExtendNonTerminal("Root", Add = ["b", "m", "(gone)"])]
        [NonTerminalDef("Alt", ["m"], "(wrap r)")]
        public static partial class TupleIsoDst;

        [Pass(typeof(TupleIsoSrc), typeof(TupleIsoDst))]
        public partial class TupleIsoPass
        {
            [Processor("B", "B")]
            public partial (TupleIsoDst.B, int) LowerBTuple(TupleIsoSrc.B b);

            [Rewrite(typeof(TupleIsoSrc.Gone), Processor = nameof(LowerBTuple))]
            private static (TupleIsoDst.B, int) HandleGoneTuple() =>
                (new TupleIsoDst.Keep(), 1);

            [Rewrite(typeof(TupleIsoSrc.Keep), Processor = nameof(LowerBTuple))]
            private static (TupleIsoDst.B, int) HandleKeepTuple() =>
                (new TupleIsoDst.Keep(), 0);
        }
        """)]
    [DataRow(
        "VEC-D3-07",
        "",
        """
        [Language, Entry("Root")]
        [Terminal(typeof(int), "n", Name = "int")]
        [NonTerminalDef("Root", ["r"], "a")]
        [NonTerminalDef("A", ["a"], "b")]
        [NonTerminalDef("B", ["b"], "(keep n)")]
        public static partial class AmbigInnerSrc;

        [Language, Entry("Root")]
        [Terminal(typeof(int), "n", Name = "int")]
        [Terminal(typeof(long), "m", Name = "long")]
        [NonTerminalDef("Root", ["r"], "a", "(keep m)")]
        [NonTerminalDef("A", ["a"], "b", "(keep n)")]
        [NonTerminalDef("B", ["b"], "bx", "by")]
        [NonTerminalDef("B1", ["bx"], "(keep n)")]
        [NonTerminalDef("B2", ["by"], "(keep n)")]
        public static partial class AmbigInnerDst;

        [Pass(typeof(AmbigInnerSrc), typeof(AmbigInnerDst))]
        public partial class AmbigInnerFallbackPass
        {
            [Processor("int", "long")]
            private static long WidenRoot(int x) => x + 1000L;
        }
        """)]
    [DataRow(
        "VEC-D3-08",
        "",
        """
        [Language, Entry("Root")]
        [Terminal(typeof(int), "n", Name = "int")]
        [NonTerminalDef("Root", ["r"], "a")]
        [NonTerminalDef("A", ["a"], "b", "(wrap b)")]
        [NonTerminalDef("B", ["b"], "(keep n)")]
        public static partial class BothWrapSrc;

        [Language, Entry("Root")]
        [Terminal(typeof(int), "n", Name = "int")]
        [Terminal(typeof(long), "k", Name = "long")]
        [NonTerminalDef("Root", ["r"], "a", "m", "(keep k)")]
        [NonTerminalDef("A", ["a"], "(wrap b)")]
        [NonTerminalDef("Alt", ["m"], "(wrap r)")]
        [NonTerminalDef("B", ["b"], "(keep n)")]
        public static partial class BothWrapDst;

        [Pass(typeof(BothWrapSrc), typeof(BothWrapDst))]
        public partial class BothWrapPass
        {
            [Processor("int", "long")]
            private static long WidenRoot(int x) => x + 1000L;
        }
        """)]
    [DataRow(
        "REG-OUTER-TERMINAL-PROCESSOR",
        "",
        """
        [Language, Entry("Root")]
        [Terminal(typeof(Symbol), "x")]
        [NonTerminalDef("Root", ["r"], "a")]
        [NonTerminalDef("A", ["a"], "b")]
        [NonTerminalDef("B", ["b"], "x")]
        public static partial class OuterTermSrc;

        [Language, Extends(typeof(OuterTermSrc))]
        [Terminal(typeof(long), "n", Name = "long")]
        [ExtendNonTerminal("Root", Add = ["n"])]
        public static partial class OuterTermDst;

        [Pass(typeof(OuterTermSrc), typeof(OuterTermDst))]
        public partial class OuterTermPass
        {
            [Processor("B", "long")]
            private static long LowerB(OuterTermSrc.B b) => 42L;
        }
        """)]
    [DataRow(
        "REG-REBIND-SELF-INCLUSION",
        "NP0105",
        """
        [Language, Entry("Expr")]
        [NonTerminalDef("Expr", ["e"], "b", "(unit)")]
        [NonTerminalDef("Body", ["b"], "(body)")]
        public static partial class RebindSelfBase;

        [Language, Extends(typeof(RebindSelfBase))]
        [Remove(typeof(RebindSelfBase.Body))]
        [ExtendNonTerminal("Expr", MetaVars = ["b"])]
        public static partial class RebindSelfDerived;
        """)]
    [DataRow(
        "REG-REBIND-DUPLICATE-INCLUSION",
        "NP0104",
        """
        [Language, Entry("Expr")]
        [NonTerminalDef("Expr", ["e"], "a", "b")]
        [NonTerminalDef("Alt", ["a"], "(alt)")]
        [NonTerminalDef("Body", ["b"], "(body)")]
        public static partial class RebindDupBase;

        [Language, Extends(typeof(RebindDupBase))]
        [Remove(typeof(RebindDupBase.Body))]
        [ExtendNonTerminal("Alt", MetaVars = ["b"])]
        public static partial class RebindDupDerived;
        """)]
    public void Z3DifferentialVectors_MatchFormalSpecDiagnosticsWithoutCSharpLeaks(
        string caseId, string expectedDiagnosticId, string source)
    {
        var result = this.EvaluateSource(caseId, source);
        Assert.IsEmpty(
            result.CompilerErrors,
            $"Unexpected CSxxxx errors in {caseId}:\n{Describe(result.CompilerErrors)}");

        if (string.IsNullOrEmpty(expectedDiagnosticId))
        {
            Assert.IsEmpty(
                result.NanopassErrors,
                $"Expected clean compilation in {caseId}:\n{Describe(result.NanopassErrors)}");
            VerifyCompiledVectorBehavior(
                caseId, result.Compilation, testContext.CancellationToken);
        }
        else
        {
            string[] expectedIds = expectedDiagnosticId
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static id => id, StringComparer.Ordinal)
                .ToArray();
            string[] actualIds = result.NanopassErrors
                .Select(static d => d.Id)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static id => id, StringComparer.Ordinal)
                .ToArray();
            Assert.AreSequenceEqual(
                expectedIds,
                actualIds,
                $"Diagnostic ID set mismatch in {caseId}: expected " +
                $"[{string.Join(", ", expectedIds)}], actual " +
                $"[{string.Join(", ", actualIds)}]\n{Describe(result.NanopassErrors)}");
        }
    }

    /// <summary>
    /// Verifies that shortest-path subtype and structural record resolution in
    /// <c>PassResolver.Clauses.cs</c> scales linearly in <c>O(V + E)</c> on a 16-layer diamond
    /// (<c>65,536</c> equal-length paths).
    /// </summary>
    [TestMethod]
    public void SubtypeCoercion_SixteenLayerDiamond_ScalesLinearlyInPassSynthesisAndCoercion()
    {
        const int layers = 17;
        static string AlphaMeta(char branch, int layer) =>
            $"{branch}{(char)('a' + layer - 1)}";

        var builder = new StringBuilder();
        var flatForms = new List<string> { "\"lf\"" };
        builder.AppendLine("[Language, Terminal(typeof(long), \"n\")]");
        builder.AppendLine("[Entry(\"L0\")]");
        string rootA = AlphaMeta('a', 1);
        string rootB = AlphaMeta('b', 1);
        builder.AppendLine($"[NonTerminalDef(\"L0\", [\"rt\"], \"{rootA}\", \"{rootB}\")]");
        for (int i = 1; i < layers; i++)
        {
            int next = i + 1;
            string targets = next == layers
                ? "\"lf\""
                : $"\"{AlphaMeta('a', next)}\", \"{AlphaMeta('b', next)}\"";
            string tagA = $"(ta{i} n)";
            string tagB = $"(tb{i} n)";
            flatForms.Add($"\"{tagA}\"");
            flatForms.Add($"\"{tagB}\"");
            string metaA = AlphaMeta('a', i);
            string metaB = AlphaMeta('b', i);
            builder.AppendLine($"[NonTerminalDef(\"A{i}\", [\"{metaA}\"], {targets}, \"{tagA}\")]");
            builder.AppendLine($"[NonTerminalDef(\"B{i}\", [\"{metaB}\"], {targets}, \"{tagB}\")]");
        }

        builder.AppendLine("[NonTerminalDef(\"Leaf\", [\"lf\"], \"n\", \"(leaf_rec n)\")]");
        builder.AppendLine("public static partial class DeepDiamondLang;");
        string grammar = builder.ToString();

        var identityResult = this.EvaluateSource(
            "DeepDiamondIdentity",
            grammar + "\n[Pass(typeof(DeepDiamondLang), typeof(DeepDiamondLang))]\n" +
            "public partial class DeepIdPass;\n");
        Assert.IsEmpty(identityResult.NanopassErrors, Describe(identityResult.NanopassErrors));
        Assert.IsEmpty(identityResult.CompilerErrors, Describe(identityResult.CompilerErrors));

        string flatSourceGrammar = $$"""
            [Language, Terminal(typeof(long), "n")]
            [Entry("L0")]
            [NonTerminalDef("L0", ["rt"], {{string.Join(", ", flatForms)}})]
            [NonTerminalDef("Leaf", ["lf"], "n", "(leaf_rec n)")]
            public static partial class FlatDiamondSource;
            """;

        var ambiguousResult = this.EvaluateSource(
            "DeepDiamondAmbiguous",
            grammar + "\n" + flatSourceGrammar + """

            [Pass(typeof(FlatDiamondSource), typeof(DeepDiamondLang))]
            public partial class DeepAmbiguousPass
            {
                [Processor("Leaf", "Leaf")]
                public partial DeepDiamondLang.Leaf LowerLeaf(FlatDiamondSource.Leaf term);
            }
            """);
        Assert.IsTrue(
            ambiguousResult.NanopassErrors.Any(static d => d.Id == "NP1003"),
            Describe(ambiguousResult.NanopassErrors));
        Assert.IsEmpty(ambiguousResult.CompilerErrors, Describe(ambiguousResult.CompilerErrors));

        var recordMatchAmbiguousResult = this.EvaluateSource(
            "DeepDiamondRecordMatchAmbiguous",
            grammar + """

            [Language, Terminal(typeof(long), "n")]
            [Entry("L0")]
            [NonTerminalDef("L0", ["rt"], "(leaf_rec n)")]
            public static partial class FlatRecordOnlySource;

            [Pass(typeof(FlatRecordOnlySource), typeof(DeepDiamondLang))]
            public partial class DeepRecordAmbiguousPass;
            """);
        Assert.IsTrue(
            recordMatchAmbiguousResult.NanopassErrors.Any(static d => d.Id == "NP1003"),
            Describe(recordMatchAmbiguousResult.NanopassErrors));
        Assert.IsEmpty(
            recordMatchAmbiguousResult.CompilerErrors,
            Describe(recordMatchAmbiguousResult.CompilerErrors));

        const int cycleDepth = 16;
        var cyclicBuilder = new StringBuilder();
        cyclicBuilder.AppendLine("[Language, Terminal(typeof(long), \"n\")]");
        cyclicBuilder.AppendLine("[Entry(\"Root\")]");
        cyclicBuilder.AppendLine("[NonTerminalDef(\"Root\", [\"rt\"], \"ca\")]");
        for (int i = 0; i < cycleDepth; i++)
        {
            string meta = $"c{(char)('a' + i)}";
            string nextMeta = $"c{(char)('a' + i + 1)}";
            cyclicBuilder.AppendLine(
                $"[NonTerminalDef(\"N{i}\", [\"{meta}\"], \"(br{i} {nextMeta}0 {nextMeta}1)\")]");
        }

        string leafMeta = $"c{(char)('a' + cycleDepth)}";
        cyclicBuilder.AppendLine(
            $"[NonTerminalDef(\"N{cycleDepth}\", [\"{leafMeta}\"], \"n\", \"(back ca)\", "
            + "\"(gone)\")]");
        cyclicBuilder.AppendLine("public static partial class CyclicBinarySrc;");

        cyclicBuilder.AppendLine("[Language, Extends(typeof(CyclicBinarySrc))]");
        cyclicBuilder.AppendLine($"[ExtendNonTerminal(\"N{cycleDepth}\", Remove = [\"(gone)\"])]");
        var rootAdds = new List<string> { $"\"{leafMeta}\"", "\"(gone)\"", "\"(back rt)\"" };
        for (int i = 0; i < cycleDepth; i++)
        {
            rootAdds.Add($"\"m{(char)('a' + i)}\"");
        }

        cyclicBuilder.AppendLine(
            $"[ExtendNonTerminal(\"Root\", Add = [{string.Join(", ", rootAdds)}])]");
        for (int i = 0; i < cycleDepth; i++)
        {
            cyclicBuilder.AppendLine(
                $"[NonTerminalDef(\"Alt{i}\", [\"m{(char)('a' + i)}\"], \"(br{i} rt0 rt1)\")]");
        }

        cyclicBuilder.AppendLine("public static partial class CyclicBinaryDst;");
        cyclicBuilder.AppendLine("[Pass(typeof(CyclicBinarySrc), typeof(CyclicBinaryDst))]");
        cyclicBuilder.AppendLine("public partial class CyclicBinaryMigrationPass;");

        var cyclicResult = this.EvaluateSource("CyclicBinaryFixedPoint", cyclicBuilder.ToString());
        Assert.IsEmpty(cyclicResult.NanopassErrors, Describe(cyclicResult.NanopassErrors));
        Assert.IsEmpty(cyclicResult.CompilerErrors, Describe(cyclicResult.CompilerErrors));
    }

    private static Gen<AlgebraicIrLanguage.Expr> BuildAlgebraicIrExprGenerator() =>
        Gen.Recursive<AlgebraicIrLanguage.Expr>((depth, selfExpr) =>
        {
            var genAtom = Gen.OneOf<AlgebraicIrLanguage.Atom>(
                GenIdentifierSymbol.Select(static x => (AlgebraicIrLanguage.Atom)x),
                Gen.Long[-5_000, 5_000].Select(static n => (AlgebraicIrLanguage.Atom)n),
                Gen.Select(
                    GenIdentifierSymbol,
                    Gen.Long[-100, 100].Nullable(),
                    Gen.Long[-100, 100].Nullable().Array[0, 3],
                    static (x, head, tail) =>
                        (AlgebraicIrLanguage.Atom)new AlgebraicIrLanguage.Tagged(
                            x, head, [.. tail])));

            var genBasePrim = genAtom.Select(static a => (AlgebraicIrLanguage.Prim)a);
            if (depth >= 3)
            {
                return genBasePrim.Select(static p => (AlgebraicIrLanguage.Expr)p);
            }

            var genPrim = Gen.OneOf(
                genBasePrim,
                genBasePrim.Select(
                    static p => (AlgebraicIrLanguage.Prim)new AlgebraicIrLanguage.Neg(p)));

            var genStmt = Gen.OneOf<AlgebraicIrLanguage.Stmt>(
                Gen.Select(
                    GenIdentifierSymbol,
                    selfExpr,
                    static (x, e) =>
                        (AlgebraicIrLanguage.Stmt)new AlgebraicIrLanguage.Assign(x, e)),
                selfExpr.Select(
                    static e => (AlgebraicIrLanguage.Stmt)new AlgebraicIrLanguage.Eval(e)));

            return Gen.OneOf<AlgebraicIrLanguage.Expr>(
                genPrim.Select(static p => (AlgebraicIrLanguage.Expr)p),
                Gen.Select(
                    selfExpr,
                    selfExpr,
                    static (l, r) =>
                        (AlgebraicIrLanguage.Expr)new AlgebraicIrLanguage.Add(l, r)),
                selfExpr.Nullable().Select(
                    static maybe =>
                        (AlgebraicIrLanguage.Expr)new AlgebraicIrLanguage.Opt(maybe)),
                selfExpr.Array[0, 2].Array[0, 2].Select(
                    static rows => (AlgebraicIrLanguage.Expr)new AlgebraicIrLanguage.Matrix(
                        [.. rows.Select(static r => (Seq<AlgebraicIrLanguage.Expr>)[.. r])])),
                Gen.Select(
                    genStmt.Array[0, 2],
                    selfExpr,
                    static (stmts, tail) =>
                        (AlgebraicIrLanguage.Expr)new AlgebraicIrLanguage.SeqForm(
                            [.. stmts], tail)),
                Gen.Select(
                    genStmt.Array[1, 2],
                    selfExpr,
                    static (stmts, tail) =>
                        (AlgebraicIrLanguage.Expr)new AlgebraicIrLanguage.Improper(
                            [.. stmts], tail)),
                Gen.Select(
                    selfExpr,
                    selfExpr.Array[0, 2],
                    static (fn, args) =>
                        (AlgebraicIrLanguage.Expr)new AlgebraicIrLanguage.Implicit(
                            fn, [.. args])));
        });

    private static Gen<SExpr> BuildSExprGenerator() =>
        Gen.Recursive<SExpr>((depth, self) =>
        {
            var genSymbol = Gen.OneOf(
                    "alpha",
                    "kebab-name?",
                    "+",
                    "...",
                    "123not-int",
                    "with space",
                    "semi;colon",
                    "pipe|char",
                    "slash\\char",
                    "#hash-sym",
                    "")
                .Select(static s => (SExpr)new SExpr.Sym(new Symbol(s)));

            var genString = Gen.OneOf(
                    "",
                    "plain",
                    "quote\"inside",
                    "line\nfeed\ttab\\slash")
                .Select(static s => (SExpr)new SExpr.Str(s));

            var genChar = Gen.OneOf('a', 'Z', '0', ' ', '\n', '\t', '(', '\\')
                .Select(static c => (SExpr)new SExpr.Char(c));

            var genAtom = Gen.OneOf<SExpr>(
                Gen.Const<SExpr>(SExpr.Nil.Instance),
                Gen.Bool.Select(static b => (SExpr)new SExpr.Bool(b)),
                Gen.Long[-1_000_000, 1_000_000].Select(static n => (SExpr)new SExpr.Int(n)),
                Gen.OneOf(-3.5, -0.25, 0.0, 1.5, 42.125).Select(
                    static d => (SExpr)new SExpr.Float(d)),
                genChar,
                genString,
                genSymbol);

            if (depth >= 3)
            {
                return genAtom;
            }

            var genProperList = self.Array[0, 3].Select(static items => SExpr.List(items));
            var genVector = self.Array[0, 3].Select(
                static items => (SExpr)new SExpr.Vector([.. items]));
            var genDottedPair = Gen.Select(
                self,
                genAtom.Where(static tail => tail is not SExpr.Nil),
                static (car, cdr) =>
                {
                    if (cdr is SExpr.Pair)
                    {
                        return SExpr.List(car);
                    }

                    return (SExpr)new SExpr.Pair(car, cdr);
                });

            return Gen.OneOf(genAtom, genProperList, genVector, genDottedPair);
        });

    private static Gen<(
        string Name,
        string EntryName,
        int PrettyMode,
        bool IncludeMaybe,
        bool IncludeNestedSeq,
        string UpstreamSource,
        string DownstreamSource)>
        BuildCrossAssemblyMetadataCaseGenerator() =>
        Gen.Select(
            Gen.OneOf("", "Demo.Compiler", "Hcoona.Generated.Stage1"),
            Gen.OneOf("Expr", "Term"),
            Gen.Int[0, 2],
            Gen.Bool,
            Gen.Bool,
            static (ns, entryName, prettyMode, includeMaybe, includeNestedSeq) =>
            {
                string nsDecl = string.IsNullOrEmpty(ns) ? string.Empty : $"namespace {ns};\n";
                string qualifier = string.IsNullOrEmpty(ns) ? string.Empty : $"{ns}.";
                string upstream;
                if (prettyMode == 2)
                {
                    string maybeCase = includeMaybe ? ", Opt" : string.Empty;
                    string matrixCase = includeNestedSeq ? ", Grid" : string.Empty;
                    string maybeRecord = includeMaybe
                        ? """

                          [Syntax("(opt (maybe n))")]
                          public sealed record Opt(long? N);
                          """
                        : string.Empty;
                    string matrixRecord = includeNestedSeq
                        ? $$"""

                            [Syntax("(grid (e* ...) ...)")]
                            public sealed record Grid(Seq<Seq<{{entryName}}>> Rows);
                            """
                        : string.Empty;
                    upstream = $$"""
                        {{nsDecl}}
                        public static class TermHooks
                        {
                            public static bool IsNonEmpty(Symbol s) => s.Name.Length > 0;
                            public static SExpr FormatSym(Symbol s) => new SExpr.Sym(s);
                        }

                        [Language]
                        [Terminal(
                            typeof(Symbol),
                            "x",
                            Predicate = "TermHooks.IsNonEmpty",
                            Prettifier = "TermHooks.FormatSym")]
                        [Terminal(typeof(long), "n")]
                        public static partial class UpstreamLang
                        {
                            [Entry, NonTerminal("e")]
                            public partial union {{entryName}}(
                                long, Leaf, Neg{{maybeCase}}{{matrixCase}});

                            [Syntax("(leaf x)"), Pretty(PrettyHandler = nameof(PrettyLeaf))]
                            public sealed record Leaf(Symbol X);

                            [Syntax("(neg e)")]
                            public sealed record Neg({{entryName}} E);
                            {{maybeRecord}}{{matrixRecord}}

                            private static SExpr PrettyLeaf(SExpr datum) =>
                                ((SExpr.Pair)((SExpr.Pair)datum).Cdr).Car;
                        }
                        """;
                }
                else
                {
                    string leafAttr = prettyMode == 0 ? "\"(leaf x)\"" : "\"(leaf x) => x\"";
                    string maybeForm = includeMaybe ? ", \"(opt (maybe n))\"" : string.Empty;
                    string matrixForm = includeNestedSeq
                        ? ", \"(grid (e* ...) ...)\""
                        : string.Empty;

                    upstream = $$"""
                        {{nsDecl}}
                        public static class TermHooks
                        {
                            public static bool IsNonEmpty(Symbol s) => s.Name.Length > 0;
                            public static SExpr FormatSym(Symbol s) => new SExpr.Sym(s);
                        }

                        [Language]
                        [Terminal(
                            typeof(Symbol),
                            "x",
                            Predicate = "TermHooks.IsNonEmpty",
                            Prettifier = "TermHooks.FormatSym")]
                        [Terminal(typeof(long), "n")]
                        [Entry("{{entryName}}")]
                        [NonTerminalDef(
                            "{{entryName}}",
                            ["e"],
                            "n",
                            {{leafAttr}}{{maybeForm}}{{matrixForm}},
                            "(neg e)")]
                        public static partial class UpstreamLang;
                        """;
                }

                string downstream = $$"""
                    [Language, Extends(typeof({{qualifier}}UpstreamLang))]
                    [ExtendNonTerminal("{{entryName}}", Remove = ["(neg e)"], Add = ["(dup e)"])]
                    public static partial class DownstreamLang;

                    [Language, PrunedLanguage(typeof(DownstreamLang))]
                    public static partial class DownstreamPruned;

                    [Pass(typeof({{qualifier}}UpstreamLang), typeof(DownstreamLang))]
                    public partial class CrossAssemblyPass
                    {
                        [Rewrite(typeof({{qualifier}}UpstreamLang.Neg))]
                        private static DownstreamLang.{{entryName}} LowerNeg(
                            [Recur] DownstreamLang.{{entryName}} e) =>
                            new DownstreamLang.Dup(e);
                    }
                    """;

                string nsKey = ns.Replace('.', '_');
                string caseName =
                    $"Meta_{nsKey}_{entryName}_{prettyMode}_{includeMaybe}_{includeNestedSeq}";
                return (
                    caseName,
                    entryName,
                    prettyMode,
                    includeMaybe,
                    includeNestedSeq,
                    upstream,
                    downstream);
            });

    private static Gen<GeneratedCompilationCase> BuildSchemaCaseGenerator() =>
        Gen.Select(
            Gen.Int[0, 5],
            Gen.OneOf("Expr", "Value", "Class"),
            Gen.Bool,
            Gen.Bool,
            static (topology, entryName, includePruned, includeExtraForm) =>
            {
                string extraForm = includeExtraForm ? ", \"(pair e1 e2)\"" : string.Empty;
                string prunedDecl = includePruned
                    ? "[Language, PrunedLanguage(typeof(L1))] public static partial class LPruned;"
                    : string.Empty;
                return topology switch
                {
                    // Valid base + extension + pruned + identity & lowering passes
                    0 => new GeneratedCompilationCase(
                        $"ValidChain_{entryName}_{includePruned}_{includeExtraForm}",
                        ExpectClean: true,
                        $$"""
                        [Language, Terminal(typeof(Symbol), "x"), Terminal(typeof(long), "n")]
                        [Entry("{{entryName}}")]
                        [NonTerminalDef("{{entryName}}", ["e"], "a"{{extraForm}}, "(neg e)")]
                        [NonTerminalDef("Atom", ["a"], "x", "n")]
                        public static partial class L0;

                        [Language, Extends(typeof(L0))]
                        [ExtendNonTerminal(
                            "{{entryName}}", Remove = ["(neg e)"], Add = ["(sub e1 e2)"])]
                        public static partial class L1;

                        {{prunedDecl}}

                        [Pass(typeof(L0), typeof(L0))]
                        public partial class IdL0;

                        [Pass(typeof(L0), typeof(L1))]
                        public partial class LowerNegPass
                        {
                            [Rewrite(typeof(L0.Neg))]
                            private static L1.{{entryName}} LowerNeg([Recur] L1.{{entryName}} e) =>
                                new L1.Sub(e, e);
                        }
                        """),

                    // Valid equal-length subtype diamond + identity pass
                    1 => new GeneratedCompilationCase(
                        $"ValidEqualDiamond_{entryName}_{includeExtraForm}",
                        ExpectClean: true,
                        $$"""
                        [Language, Terminal(typeof(Symbol), "x")]
                        [Entry("{{entryName}}")]
                        [NonTerminalDef("{{entryName}}", ["e"], "b", "c"{{extraForm}})]
                        [NonTerminalDef("LeftBranch", ["b"], "a", "(ltag x)")]
                        [NonTerminalDef("RightBranch", ["c"], "a", "(rtag x)")]
                        [NonTerminalDef("Atom", ["a"], "x")]
                        public static partial class DiaLang;

                        [Pass(typeof(DiaLang), typeof(DiaLang))]
                        public partial class DiaId;
                        """),

                    // Valid unequal-length subtype diamond + identity pass
                    2 => new GeneratedCompilationCase(
                        $"ValidUnequalDiamond_{entryName}_{includeExtraForm}",
                        ExpectClean: true,
                        $$"""
                        [Language, Terminal(typeof(long), "n")]
                        [Entry("{{entryName}}")]
                        [NonTerminalDef("{{entryName}}", ["e"], "b", "a"{{extraForm}})]
                        [NonTerminalDef("Mid", ["b"], "a", "(wrap n)")]
                        [NonTerminalDef("Atom", ["a"], "n")]
                        public static partial class UneqLang;

                        [Pass(typeof(UneqLang), typeof(UneqLang))]
                        public partial class UneqId;
                        """),

                    // Invalid pass missing required rewrite for removed production (NP1001, 0 CS)
                    3 => new GeneratedCompilationCase(
                        $"MissingRewrite_{entryName}",
                        ExpectClean: false,
                        $$"""
                        [Language, Terminal(typeof(long), "n")]
                        [Entry("{{entryName}}")]
                        [NonTerminalDef("{{entryName}}", ["e"], "n", "(neg e)")]
                        public static partial class SrcLang;

                        [Language, Extends(typeof(SrcLang))]
                        [ExtendNonTerminal("{{entryName}}", Remove = ["(neg e)"])]
                        public static partial class DstLang;

                        [Pass(typeof(SrcLang), typeof(DstLang))]
                        public partial class IncompletePass;
                        """),

                    // Invalid direct self-inclusion referenced by a downstream [Pass] (NP0105)
                    4 => new GeneratedCompilationCase(
                        $"BrokenSelfInclusionWithPass_{entryName}",
                        ExpectClean: false,
                        $$"""
                        [Language, Terminal(typeof(Symbol), "x")]
                        [Entry("{{entryName}}")]
                        [NonTerminalDef("{{entryName}}", ["e"], "e", "x")]
                        public static partial class BrokenSelfLang;

                        [Pass(typeof(BrokenSelfLang), typeof(BrokenSelfLang))]
                        public partial class DownstreamPass;
                        """),

                    // Invalid equal-length diamond processor coercion (NP1003, 0 CS)
                    _ => new GeneratedCompilationCase(
                        $"AmbiguousDiamondPass_{entryName}",
                        ExpectClean: false,
                        $$"""
                        [Language, Terminal(typeof(Symbol), "x")]
                        [Entry("{{entryName}}")]
                        [NonTerminalDef("{{entryName}}", ["e"], "b", "c")]
                        [NonTerminalDef("B", ["b"], "a", "(btag x)")]
                        [NonTerminalDef("C", ["c"], "a", "(ctag x)")]
                        [NonTerminalDef("A", ["a"], "x")]
                        public static partial class TargetDia;

                        [Language, Terminal(typeof(Symbol), "x")]
                        [Entry("{{entryName}}")]
                        [NonTerminalDef("{{entryName}}", ["e"], "a", "(btag x)", "(ctag x)")]
                        [NonTerminalDef("A", ["a"], "x")]
                        public static partial class SourceFlat;

                        [Pass(typeof(SourceFlat), typeof(TargetDia))]
                        public partial class AmbiguousPass
                        {
                            [Processor("A", "A")]
                            public partial TargetDia.A LowerA(SourceFlat.A term);
                        }
                        """),
                };
            });

    private static string Describe(ImmutableArray<Diagnostic> diagnostics) =>
        string.Join(
            Environment.NewLine,
            diagnostics.Select(static d => $"{d.Id} ({d.Severity}): {d.GetMessage()}"));

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void VerifyCompiledVectorBehavior(
        string caseId, Compilation compilation, CancellationToken cancellationToken)
    {
        using var peStream = new MemoryStream();
        var emit = compilation.Emit(peStream, cancellationToken: cancellationToken);
        Assert.IsTrue(emit.Success, $"Emit failed for {caseId}:\n{Describe(emit.Diagnostics)}");
        byte[] peBytes = peStream.ToArray();

        var alc = new AssemblyLoadContext($"NanopassVec_{caseId}", isCollectible: true);
        try
        {
            string runtimeName = typeof(SExpr).Assembly.GetName().Name!;
            alc.Resolving += (_, asmName) =>
                string.Equals(asmName.Name, runtimeName, StringComparison.Ordinal)
                    ? typeof(SExpr).Assembly
                    : null;

            var asm = alc.LoadFromStream(new MemoryStream(peBytes, writable: false));
            static string ReadLanguageSExpr(Assembly assembly, string typeName) =>
                ((SExpr)assembly
                    .GetTypes()
                    .Single(t => string.Equals(t.Name, typeName, StringComparison.Ordinal))
                    .GetMethod("ToSExpression", BindingFlags.Public | BindingFlags.Static)!
                    .Invoke(null, [true])!).Format();

            static void AssertPassBehavior(
                Assembly assembly,
                string srcName,
                string dstName,
                string passName,
                params (string Input, string Expected)[] samples)
            {
                var srcType = assembly.GetTypes().Single(
                    t => string.Equals(t.Name, srcName, StringComparison.Ordinal));
                var dstType = assembly.GetTypes().Single(
                    t => string.Equals(t.Name, dstName, StringComparison.Ordinal));
                var passType = assembly.GetTypes().Single(
                    t => string.Equals(t.Name, passName, StringComparison.Ordinal));
                var parse = srcType.GetMethod(
                    "Parse", BindingFlags.Public | BindingFlags.Static, [typeof(string)])!;
                var unparse = dstType.GetMethod(
                    "UnparseObject", BindingFlags.Public | BindingFlags.Static)!;
                object pass = Activator.CreateInstance(passType)!;
                var run = passType.GetMethod("Run", BindingFlags.Public | BindingFlags.Instance)!;

                foreach (var (input, expected) in samples)
                {
                    object srcIr = parse.Invoke(null, [input])!;
                    object dstIr = run.Invoke(pass, [srcIr])!;
                    Assert.AreEqual(
                        expected,
                        ((SExpr)unparse.Invoke(null, [dstIr, true])!).Format(),
                        $"Pass {passName} output mismatch for input '{input}'.");
                }
            }

            switch (caseId)
            {
                case "VEC-D1-01":
                    string l0 = ReadLanguageSExpr(asm, "L0");
                    string l2 = ReadLanguageSExpr(asm, "L2");
                    Assert.DoesNotContain("(neg e)", l2);
                    Assert.AreEqual(
                        l0,
                        l2.Replace(
                            "(define-language L2",
                            "(define-language L0",
                            StringComparison.Ordinal));
                    break;

                case "VEC-D1-03":
                    string pruned = ReadLanguageSExpr(asm, "PrunedUnproductive");
                    Assert.Contains("(Dead (d)", pruned);
                    Assert.DoesNotContain("UnreachedA", pruned);
                    Assert.DoesNotContain("UnreachedB", pruned);
                    Assert.Contains("(terminals)", pruned);
                    Assert.DoesNotContain("(n)", pruned);
                    break;

                case "VEC-D1-04":
                    string partialMeta = ReadLanguageSExpr(asm, "PartialMetaRemoved");
                    Assert.Contains("(symbol (x))", partialMeta);
                    Assert.DoesNotContain("(x y)", partialMeta);
                    Assert.DoesNotContain("(use y)", partialMeta);
                    break;

                case "VEC-D2-01":
                    AssertPassBehavior(
                        asm,
                        "FlatUnequalSource",
                        "UnequalDiamond",
                        "UnequalDiamondCoercePass",
                        ("7", "7"),
                        ("(wrap 7)", "(wrap 7)"));
                    break;

                case "VEC-D2-03":
                    AssertPassBehavior(
                        asm,
                        "TwoNodeCycle",
                        "TwoNodeCycle",
                        "TwoNodeCyclePass",
                        ("7", "7"),
                        ("(neg 7)", "(neg 7)"));
                    break;

                case "VEC-D2-05":
                    AssertPassBehavior(
                        asm,
                        "EqualDia",
                        "EqualDia",
                        "EqualDiaIdPass",
                        ("foo", "foo"),
                        ("(btag foo)", "(btag foo)"),
                        ("(ctag foo)", "(ctag foo)"));
                    break;

                case "VEC-D2-06":
                    AssertPassBehavior(
                        asm,
                        "FlatTopSource",
                        "TransitiveSubtype",
                        "TransitiveSubtypePass",
                        ("7", "7"),
                        ("(keep 7)", "(keep 7)"));
                    break;

                case "VEC-D3-01":
                    AssertPassBehavior(
                        asm,
                        "HomomorphicLang",
                        "HomomorphicLang",
                        "HomomorphicAutoPass",
                        ("7", "7"),
                        ("(neg 7)", "(neg 7)"),
                        ("(seq 1 2 3)", "(seq 1 2 3)"));
                    break;

                case "VEC-D3-04":
                    AssertPassBehavior(
                        asm,
                        "RetainedGuardLang",
                        "RetainedGuardLang",
                        "RetainedGuardPass",
                        ("(neg 0)", "0"),
                        ("(neg 5)", "(neg 5)"),
                        ("(neg (neg 0))", "(neg 0)"));
                    break;

                case "VEC-D3-06":
                    AssertPassBehavior(
                        asm,
                        "NestedMigrationSrc",
                        "NestedMigrationDst",
                        "NestedMigrationPass",
                        ("(wrap (gone))", "(wrap (gone))"),
                        ("(wrap (keep))", "(wrap (keep))"));
                    break;

                case "VEC-D3-07":
                    AssertPassBehavior(
                        asm,
                        "AmbigInnerSrc",
                        "AmbigInnerDst",
                        "AmbigInnerFallbackPass",
                        ("(keep 7)", "(keep 7)"));
                    break;

                case "VEC-D3-08":
                    AssertPassBehavior(
                        asm,
                        "BothWrapSrc",
                        "BothWrapDst",
                        "BothWrapPass",
                        ("(keep 7)", "(keep 1007)"),
                        ("(wrap (keep 7))", "(wrap (keep 7))"));
                    break;

                case "REG-OUTER-TERMINAL-PROCESSOR":
                    AssertPassBehavior(
                        asm,
                        "OuterTermSrc",
                        "OuterTermDst",
                        "OuterTermPass",
                        ("foo", "42"));
                    break;

                case "REG-UNBINDABLE-OUTER":
                    AssertPassBehavior(
                        asm,
                        "OuterUnbindableLang",
                        "OuterUnbindableLang",
                        "OuterUnbindablePass",
                        ("(wrap 7)", "(wrap 7)"));
                    break;

                case "REG-TUPLE-REWRITE-ISOLATION":
                    AssertPassBehavior(
                        asm,
                        "TupleIsoSrc",
                        "TupleIsoDst",
                        "TupleIsoPass",
                        ("(wrap (gone))", "(wrap (gone))"),
                        ("(wrap (keep))", "(wrap (keep))"));
                    break;

                default:
                    Assert.Fail($"Missing runtime verification for clean vector {caseId}.");
                    break;
            }
        }
        finally
        {
            alc.Unload();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ExecuteCrossAssemblyMetadataChecks(
        string name,
        byte[] upstreamBytes,
        byte[] downstreamBytes,
        string entryName,
        int prettyMode,
        bool includeMaybe,
        bool includeNestedSeq)
    {
        var alc = new AssemblyLoadContext($"NanopassRoundTrip_{name}", isCollectible: true);
        try
        {
            string runtimeAssemblyName = typeof(SExpr).Assembly.GetName().Name!;
            alc.Resolving += (_, asmName) =>
                string.Equals(asmName.Name, runtimeAssemblyName, StringComparison.Ordinal)
                    ? typeof(SExpr).Assembly
                    : null;

            var upstreamAssembly = alc.LoadFromStream(
                new MemoryStream(upstreamBytes, writable: false));
            var downstreamAssembly = alc.LoadFromStream(
                new MemoryStream(downstreamBytes, writable: false));

            var upstreamType = upstreamAssembly
                .GetTypes()
                .Single(static t =>
                    string.Equals(t.Name, "UpstreamLang", StringComparison.Ordinal));
            var downstreamType = downstreamAssembly
                .GetTypes()
                .Single(static t =>
                    string.Equals(t.Name, "DownstreamLang", StringComparison.Ordinal));
            var prunedType = downstreamAssembly
                .GetTypes()
                .Single(static t =>
                    string.Equals(t.Name, "DownstreamPruned", StringComparison.Ordinal));
            var passType = downstreamAssembly
                .GetTypes()
                .Single(static t =>
                    string.Equals(t.Name, "CrossAssemblyPass", StringComparison.Ordinal));

            var downstreamDesc = (LanguageDescriptor)downstreamType
                .GetProperty("Descriptor", BindingFlags.Public | BindingFlags.Static)!
                .GetValue(null)!;
            Assert.AreEqual(entryName, downstreamDesc.Entry.Name);

            string prunedSexp = ((SExpr)prunedType
                .GetMethod("ToSExpression", BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, [true])!).Format();
            Assert.Contains($"(entry {entryName})", prunedSexp);
            Assert.DoesNotContain("(neg e)", prunedSexp);
            Assert.Contains("(dup e)", prunedSexp);
            Assert.AreEqual(
                includeMaybe,
                prunedSexp.Contains("(opt (maybe n))", StringComparison.Ordinal));
            Assert.AreEqual(
                includeNestedSeq,
                prunedSexp.Contains("(grid (e* ...) ...)", StringComparison.Ordinal));

            var parseUpstream = upstreamType.GetMethod(
                "Parse",
                BindingFlags.Public | BindingFlags.Static,
                [typeof(string)])!;
            var unparseUpstream = upstreamType.GetMethod(
                "UnparseObject",
                BindingFlags.Public | BindingFlags.Static)!;
            var unparseDownstream = downstreamType.GetMethod(
                "UnparseObject",
                BindingFlags.Public | BindingFlags.Static)!;

            object parsedIr = parseUpstream.Invoke(null, ["(neg (leaf k))"])!;
            Assert.AreEqual(
                "(neg (leaf k))",
                ((SExpr)unparseUpstream.Invoke(null, [parsedIr, true])!).Format());
            Assert.AreEqual(
                prettyMode == 0 ? "(neg (leaf k))" : "(neg k)",
                ((SExpr)unparseUpstream.Invoke(null, [parsedIr, false])!).Format());

            var ex = Assert.ThrowsExactly<TargetInvocationException>(
                () => parseUpstream.Invoke(null, ["(leaf ||)"]));
            Assert.IsInstanceOfType<NanopassParseException>(ex.InnerException);

            object passInstance = Activator.CreateInstance(passType)!;
            object loweredIr = passType
                .GetMethod("Run", BindingFlags.Public | BindingFlags.Instance)!
                .Invoke(passInstance, [parsedIr])!;
            Assert.AreEqual(
                "(dup (leaf k))",
                ((SExpr)unparseDownstream.Invoke(null, [loweredIr, true])!).Format());
            Assert.AreEqual(
                prettyMode == 0 ? "(dup (leaf k))" : "(dup k)",
                ((SExpr)unparseDownstream.Invoke(null, [loweredIr, false])!).Format());
        }
        finally
        {
            alc.Unload();
        }
    }

    private EvaluationResult EvaluateSource(
        string name, string source, params MetadataReference[] extraReferences)
    {
        var compilation = CSharpCompilation.Create(
            name,
            [CSharpSyntaxTree.ParseText(
                "using Hcoona.Nanopass;\n" + source,
                ParseOptions,
                name + ".cs",
                cancellationToken: testContext.CancellationToken)],
            PlatformReferences.AddRange(extraReferences),
            CompilationOptions);

        var driver = CSharpGeneratorDriver.Create(
            [new NanopassGenerator().AsSourceGenerator()], parseOptions: ParseOptions);
        _ = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var updatedCompilation,
            out var generatorDiagnostics,
            testContext.CancellationToken);

        var withAnalyzers = updatedCompilation.WithAnalyzers(
            [new NanopassPassAnalyzer()],
            new CompilationWithAnalyzersOptions(
                options: new AnalyzerOptions([]),
                onAnalyzerException: null,
                concurrentAnalysis: true,
                logAnalyzerExecutionTime: false,
                reportSuppressedDiagnostics: false));

        var analyzerDiagnostics = withAnalyzers
            .GetAnalyzerDiagnosticsAsync(testContext.CancellationToken)
            .GetAwaiter()
            .GetResult();

        var allDriverDiagnostics = generatorDiagnostics.AddRange(analyzerDiagnostics);
        var executionFailures = allDriverDiagnostics
            .Where(static d =>
                d.Id is "CS8784" or "CS8785" ||
                d.Id.StartsWith("AD", StringComparison.Ordinal))
            .ToImmutableArray();
        Assert.IsEmpty(
            executionFailures,
            $"Generator or analyzer execution failed in {name}:\n{Describe(executionFailures)}");

        var nanopassErrors = allDriverDiagnostics
            .Where(static d =>
                d.Severity == DiagnosticSeverity.Error &&
                d.Id.StartsWith("NP", StringComparison.Ordinal))
            .ToImmutableArray();

        var compilerErrors = updatedCompilation
            .GetDiagnostics(testContext.CancellationToken)
            .Where(static d => d.Severity == DiagnosticSeverity.Error)
            .ToImmutableArray();

        return new EvaluationResult(updatedCompilation, nanopassErrors, compilerErrors);
    }

    private sealed record GeneratedCompilationCase(
        string Name, bool ExpectClean, string Source);

    private sealed record EvaluationResult(
        Compilation Compilation,
        ImmutableArray<Diagnostic> NanopassErrors,
        ImmutableArray<Diagnostic> CompilerErrors);
}

internal static class AlgebraicIrPredicates
{
    private static readonly ImmutableHashSet<string> ReservedKeywords =
        ImmutableHashSet.Create(
            StringComparer.Ordinal,
            "add",
            "opt",
            "matrix",
            "seq-form",
            "improper",
            "neg",
            "tagged",
            "assign",
            "eval");

    public static bool IsIdentifier(Symbol symbol) =>
        !ReservedKeywords.Contains(symbol.Name);
}

[Language]
[Terminal(typeof(Symbol), "x", Predicate = nameof(IsIdentifier))]
[Terminal(typeof(long), "n")]
[Entry("Expr")]
[NonTerminalDef(
    "Expr",
    ["e"],
    "p",
    "(add e1 e2)",
    "(opt (maybe e))",
    "(matrix (e* ...) ...)",
    "(seq-form s* ... e)",
    "(improper s* ... . e)",
    "(e1 e2* ...)")]
[NonTerminalDef("Prim", ["p"], "a", "(neg p)")]
[NonTerminalDef("Atom", ["a"], "x", "n", "(tagged x (maybe n) (maybe n*) ...)")]
[NonTerminalDef("Stmt", ["s"], "(assign x e)", "(eval e)")]
public static partial class AlgebraicIrLanguage
{
    private static bool IsIdentifier(Symbol symbol) =>
        AlgebraicIrPredicates.IsIdentifier(symbol);
}

[Pass(typeof(AlgebraicIrLanguage), typeof(AlgebraicIrLanguage))]
public partial class AlgebraicIrIdPass;

[Pass(typeof(AlgebraicIrLanguage), typeof(AlgebraicIrLanguage))]
public partial class AlgebraicIrContextualIdPass
{
    [Processor("Stmt", "Stmt")]
    public partial AlgebraicIrLanguage.Stmt VisitStmtWithDepth(
        AlgebraicIrLanguage.Stmt stmt, int depth);
}

[Language]
[Terminal(typeof(Symbol), "x")]
[Entry("D")]
[NonTerminalDef("D", ["d"], "b", "c")]
[NonTerminalDef("B", ["b"], "a", "(btag x)")]
[NonTerminalDef("C", ["c"], "a", "(ctag x)")]
[NonTerminalDef("A", ["a"], "x")]
public static partial class EqualDiamondLanguage;

[Pass(typeof(EqualDiamondLanguage), typeof(EqualDiamondLanguage))]
public partial class EqualDiamondIdPass;

[Language]
[Terminal(typeof(long), "n")]
[Entry("D")]
[NonTerminalDef("D", ["d"], "b", "a")]
[NonTerminalDef("B", ["b"], "a", "(wrap n)")]
[NonTerminalDef("A", ["a"], "n")]
public static partial class UnequalDiamondLanguage;

[Pass(typeof(UnequalDiamondLanguage), typeof(UnequalDiamondLanguage))]
public partial class UnequalDiamondIdPass;

[Pass(typeof(TwoInclusionsLanguage), typeof(TwoInclusionsLanguage))]
internal sealed partial class TwoInclusionsIdPass;

[Language]
[Terminal(typeof(long), "n")]
[Terminal(typeof(bool), "b")]
[Entry("Expr")]
[NonTerminalDef("Expr", ["e"], "f", "(fork e1 e2)", "(leaf n)")]
[NonTerminalDef("Form", ["f"], "e", "b")]
public static partial class MutualCycleLanguage;

[Pass(typeof(MutualCycleLanguage), typeof(MutualCycleLanguage))]
internal sealed partial class MutualCycleIdPass;

[Language]
[Terminal(typeof(Symbol), "x")]
[Entry("Expr")]
[NonTerminalDef("Expr", ["e"], "a", "(pair e1 e2)")]
[NonTerminalDef("Atom", ["a"], "x", "(tagged x)")]
public static partial class UpwardSourceLanguage;

[Language]
[Extends(typeof(UpwardSourceLanguage))]
[ExtendNonTerminal("Atom", Remove = ["(tagged x)"])]
[ExtendNonTerminal("Expr", Add = ["(tagged x)"])]
public static partial class UpwardTargetLanguage;

[Pass(typeof(UpwardSourceLanguage), typeof(UpwardTargetLanguage))]
public partial class UpwardMigratedAtomPass;

[Language]
[Terminal(typeof(Symbol), "x")]
[Entry("Expr")]
[NonTerminalDef("Expr", ["e"], "s", "x", "(e1 e2)")]
[NonTerminalDef("Sub", ["s"], "(tag x)")]
public static partial class NonImplicitSubDispatchLanguage;

[Language]
[Terminal(typeof(Symbol), "x")]
[Entry("Expr")]
[NonTerminalDef("Expr", ["e"], "s", "x", "(e1 e2)")]
[NonTerminalDef("Sub", ["s"], "(tag x)", "(x1 x2 x3)")]
public static partial class ImplicitSubDispatchLanguage;

[Language]
[Terminal(typeof(bool), "b")]
[Entry("Expr")]
[NonTerminalDef("Expr", ["e"], "(box (maybe b))")]
public static partial class MaybeBoolShadowLanguage;

[Language]
[Terminal(typeof(long), "n")]
[Entry("Expr")]
[NonTerminalDef("Expr", ["e"], "(unit)", "da")]
[NonTerminalDef("DeadA", ["da"], "(step db)")]
[NonTerminalDef("DeadB", ["db"], "(back da)")]
[NonTerminalDef("Orphan", ["o"], "n")]
public static partial class UnreachableCycleLanguage;

[Language]
[Terminal(typeof(Symbol), "x")]
[Terminal(typeof(long), "n")]
[Entry("Expr")]
[NonTerminalDef("Expr", ["e"], "x", "n", "(bind x e1 e2) => (x e1 e2)", "(e0 e1 e2)")]
public static partial class PrettyTagEraseLanguage;

[Language]
[Terminal(typeof(long), "n")]
[Entry("Expr")]
[NonTerminalDef("Expr", ["e"], "(wrap s)")]
[NonTerminalDef("Sub", ["s"], "n")]
public static partial class NoXvalFieldSynthesisLanguage;

[Pass(typeof(NoXvalFieldSynthesisLanguage), typeof(NoXvalFieldSynthesisLanguage))]
public partial class NoXvalFieldSynthesisPass
{
    [Processor("Sub", "Sub")]
    public partial (NoXvalFieldSynthesisLanguage.Sub, int) InspectSub(
        NoXvalFieldSynthesisLanguage.Sub s);

    [Rewrite(typeof(long), Processor = nameof(InspectSub))]
    private static (NoXvalFieldSynthesisLanguage.Sub, int) MutateWithExtra(long n) =>
        (n + 100L, 1);
}

