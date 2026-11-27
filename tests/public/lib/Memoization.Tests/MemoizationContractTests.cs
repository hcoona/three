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

using System;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Memoization.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Internal;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Memoization.Tests
{
    /// <summary>
    /// Structural contract tests covering all 64 generated <c>Memoization.Create</c> overloads
    /// (arities 1..16 x 4 signatures), linked <c>AbsoluteExpiration</c> narrowing, and Roslyn
    /// nullable-reference-type metadata diagnostics.
    /// </summary>
    [TestClass]
    [DoNotParallelize]
    public sealed class MemoizationContractTests(TestContext testContext)
    {
        private readonly TestContext _testContext = testContext;

        private sealed class FakeClock : ISystemClock
        {
            public DateTimeOffset UtcNow { get; set; } =
                new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        }

        [TestMethod]
        public void All64GeneratedOverloads_Preconditions_ValidateEagerlyAtCreateTime()
        {
            var methods = typeof(Memoization)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(static m => m.Name == nameof(Memoization.Create))
                .ToArray();
            Assert.HasCount(64, methods);

            using var cache = new MemoryCache(new MemoryCacheOptions());
            var options = new MemoryCacheEntryOptions();
            var previousDefault = Memoization.DefaultCache;

            try
            {
                for (int arity = 1; arity <= 16; arity++)
                {
                    var typeArgs = Enumerable.Repeat(typeof(string), arity + 1).ToArray();
                    var dummyFunc = BuildPositionalStringDelegate(arity, "P", () => { });

                    var arityMethods = methods
                        .Where(m => m.GetGenericArguments().Length == arity + 1)
                        .Select(m => m.MakeGenericMethod(typeArgs))
                        .ToArray();
                    Assert.HasCount(4, arityMethods);

                    foreach (var closed in arityMethods)
                    {
                        var parameters = closed.GetParameters();
                        var signature = string.Join(
                            ",",
                            parameters.Select(static p => p.ParameterType.Name));

                        Memoization.DefaultCache = cache;
                        var nullFuncArgs = parameters.Select<ParameterInfo, object?>(
                            p => p.Name switch
                            {
                                "func" => null,
                                "cache" => cache,
                                "options" => options,
                                _ => throw new InvalidOperationException(p.Name),
                            }).ToArray();

                        var funcEx = Assert.ThrowsExactly<TargetInvocationException>(
                            () => closed.Invoke(null, nullFuncArgs));
                        var argNullFunc = Assert.IsInstanceOfType<ArgumentNullException>(
                            funcEx.InnerException);
                        Assert.AreEqual(
                            "func",
                            argNullFunc.ParamName,
                            $"Arity {arity} ({signature})");

                        if (parameters.Any(static p => p.Name == "cache"))
                        {
                            var nullCacheArgs = parameters.Select<ParameterInfo, object?>(
                                p => p.Name switch
                                {
                                    "func" => dummyFunc,
                                    "cache" => null,
                                    "options" => options,
                                    _ => throw new InvalidOperationException(p.Name),
                                }).ToArray();

                            var cacheEx = Assert.ThrowsExactly<TargetInvocationException>(
                                () => closed.Invoke(null, nullCacheArgs));
                            var argNullCache = Assert.IsInstanceOfType<ArgumentNullException>(
                                cacheEx.InnerException);
                            Assert.AreEqual(
                                "cache",
                                argNullCache.ParamName,
                                $"Arity {arity} ({signature})");
                        }

                        if (parameters.Any(static p => p.Name == "options"))
                        {
                            // Null options throws even when DefaultCache is null.
                            Memoization.DefaultCache = null!;
                            var nullOptionsArgs = parameters.Select<ParameterInfo, object?>(
                                p => p.Name switch
                                {
                                    "func" => dummyFunc,
                                    "cache" => cache,
                                    "options" => null,
                                    _ => throw new InvalidOperationException(p.Name),
                                }).ToArray();

                            var optionsEx = Assert.ThrowsExactly<TargetInvocationException>(
                                () => closed.Invoke(null, nullOptionsArgs));
                            var argNullOpts = Assert.IsInstanceOfType<ArgumentNullException>(
                                optionsEx.InnerException);
                            Assert.AreEqual(
                                "options",
                                argNullOpts.ParamName,
                                $"Arity {arity} ({signature})");
                        }

                        if (parameters.All(static p => p.Name != "cache"))
                        {
                            Memoization.DefaultCache = null!;
                            var validNonCacheArgs = parameters.Select<ParameterInfo, object?>(
                                p => p.Name switch
                                {
                                    "func" => dummyFunc,
                                    "options" => options,
                                    _ => throw new InvalidOperationException(p.Name),
                                }).ToArray();

                            var defaultNullEx = Assert.ThrowsExactly<TargetInvocationException>(
                                () => closed.Invoke(null, validNonCacheArgs));
                            Assert.IsInstanceOfType<InvalidOperationException>(
                                defaultNullEx.InnerException,
                                $"Arity {arity} ({signature})");
                        }
                    }
                }
            }
            finally
            {
                Memoization.DefaultCache = previousDefault;
            }
        }

        [TestMethod]
        public void All16Arities_PositionalKeysNullsAndNestedRest_ParticipateInScopedKey()
        {
            var createMethods = typeof(Memoization)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(static m => m.Name == nameof(Memoization.Create))
                .ToArray();

            for (int arity = 1; arity <= 16; arity++)
            {
                var typeArgs = Enumerable.Repeat(typeof(string), arity + 1).ToArray();
                var arityMethods = createMethods
                    .Where(m => m.GetGenericArguments().Length == arity + 1)
                    .Select(m => m.MakeGenericMethod(typeArgs))
                    .ToArray();
                var cacheOverloads = arityMethods
                    .Where(static m => m.GetParameters().Any(static p => p.Name == "cache"));

                foreach (var closed in cacheOverloads)
                {
                    using var cache = new MemoryCache(new MemoryCacheOptions());
                    int callsA = 0;
                    int callsB = 0;
                    var funcA = BuildPositionalStringDelegate(arity, "A", () => callsA++);
                    var funcB = BuildPositionalStringDelegate(arity, "B", () => callsB++);

                    object[] createArgsA = closed.GetParameters().Length == 2
                        ? [funcA, cache]
                        : [funcA, cache, new MemoryCacheEntryOptions()];
                    object[] createArgsB = closed.GetParameters().Length == 2
                        ? [funcB, cache]
                        : [funcB, cache, new MemoryCacheEntryOptions()];

                    var memoA = (Delegate)closed.Invoke(null, createArgsA)!;
                    var memoB = (Delegate)closed.Invoke(null, createArgsB)!;

                    var baseArgs = Enumerable.Range(0, arity)
                        .Select(static idx =>
                            (object?)$"v{idx.ToString(CultureInfo.InvariantCulture)}")
                        .ToArray();
                    var expectedBaseA = FormatExpected("A", baseArgs);
                    var expectedBaseB = FormatExpected("B", baseArgs);

                    Assert.AreEqual(expectedBaseA, memoA.DynamicInvoke(baseArgs));
                    Assert.AreEqual(1, callsA);

                    // Repeat call with freshly allocated equal strings hits cache (+0 calls).
                    var clonedArgs = baseArgs
                        .Select(static s => (object?)new string(((string)s!).ToCharArray()))
                        .ToArray();
                    Assert.AreEqual(expectedBaseA, memoA.DynamicInvoke(clonedArgs));
                    Assert.AreEqual(1, callsA);

                    // Second delegate at same arity and same cache is isolated.
                    Assert.AreEqual(expectedBaseB, memoB.DynamicInvoke(baseArgs));
                    Assert.AreEqual(1, callsB);
                    Assert.AreEqual(expectedBaseA, memoA.DynamicInvoke(baseArgs));
                    Assert.AreEqual(1, callsA);

                    // Perturbing every parameter position 0..arity-1 (including 7..15 in nested
                    // ValueTuple Rest) with null and non-null values misses once then hits.
                    for (int pos = 0; pos < arity; pos++)
                    {
                        var nullAtPos = (object?[])baseArgs.Clone();
                        nullAtPos[pos] = null;
                        int beforeNull = callsA;
                        var expectedNull = FormatExpected("A", nullAtPos);
                        Assert.AreEqual(expectedNull, memoA.DynamicInvoke(nullAtPos));
                        Assert.AreEqual(beforeNull + 1, callsA, $"Arity {arity} null@{pos}");
                        Assert.AreEqual(expectedNull, memoA.DynamicInvoke(nullAtPos));
                        Assert.AreEqual(beforeNull + 1, callsA, $"Arity {arity} hit null@{pos}");

                        var altAtPos = (object?[])baseArgs.Clone();
                        altAtPos[pos] = $"alt{pos.ToString(CultureInfo.InvariantCulture)}";
                        int beforeAlt = callsA;
                        var expectedAlt = FormatExpected("A", altAtPos);
                        Assert.AreEqual(expectedAlt, memoA.DynamicInvoke(altAtPos));
                        Assert.AreEqual(beforeAlt + 1, callsA, $"Arity {arity} alt@{pos}");
                    }
                }
            }
        }

        [TestMethod]
        public void Create_OptionsWithoutAbsoluteExpiration_PreservesLinkedChildExpiration()
        {
            var clock = new FakeClock();
            using var cache = new MemoryCache(new MemoryCacheOptions
            {
                Clock = clock,
                TrackLinkedCacheEntries = true,
            });

            int childComputations = 0;
            int parentComputations = 0;

            // Options leave AbsoluteExpiration null while setting Priority; SetOptions must run
            // before func so child's propagated AbsoluteExpiration is not overwritten with null.
            var parentOptions = new MemoryCacheEntryOptions
            {
                Priority = CacheItemPriority.High,
            };
            var memoized = Memoization.Create<int, int>(
                key =>
                {
                    parentComputations++;
                    var childVal = cache.GetOrCreate(
                        "short-lived-child",
                        entry =>
                        {
                            childComputations++;
                            entry.AbsoluteExpiration = clock.UtcNow.AddSeconds(5);
                            return 100 * childComputations;
                        });
                    return childVal + key;
                },
                cache,
                parentOptions);

            Assert.AreEqual(107, memoized(7));
            Assert.AreEqual(1, parentComputations);

            clock.UtcNow = clock.UtcNow.AddSeconds(3);
            Assert.AreEqual(107, memoized(7));
            Assert.AreEqual(1, parentComputations);

            clock.UtcNow = clock.UtcNow.AddSeconds(3);
            Assert.AreEqual(207, memoized(7));
            Assert.AreEqual(2, parentComputations);
        }

        [TestMethod]
        public void SourceGenerator_ModernNrtAndLegacyCSharp73_CompileWithoutUnexpectedWarnings()
        {
            var memoizationAssemblyPath = typeof(Memoization).Assembly.Location;
            var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator)
                .Append(typeof(IMemoryCache).Assembly.Location)
                .Where(p => !string.Equals(p, memoizationAssemblyPath, StringComparison.Ordinal))
                .Distinct(StringComparer.Ordinal)
                .Select(static p => MetadataReference.CreateFromFile(p))
                .ToImmutableArray();

            var modernParse = new CSharpParseOptions(LanguageVersion.Latest)
                .WithPreprocessorSymbols("NET9_0");
            var stubTree = CSharpSyntaxTree.ParseText(
                """
                #nullable enable
                using Microsoft.Extensions.Caching.Memory;

                namespace Memoization
                {
                    public static partial class Memoization
                    {
                        public static IMemoryCache? DefaultCache { get; set; }
                    }
                }
                """,
                modernParse,
                path: "Stub.cs",
                cancellationToken: _testContext.CancellationToken);

            var validConsumerTree = CSharpSyntaxTree.ParseText(
                """
                #nullable enable
                using System;
                using Microsoft.Extensions.Caching.Memory;

                public static class ValidConsumer
                {
                    public static Func<string?, int?, string?> UseNullable(IMemoryCache cache)
                        => Memoization.Memoization.Create<string?, int?, string?>(
                            (s, i) => s, cache);
                }
                """,
                modernParse,
                path: "ValidConsumer.cs",
                cancellationToken: _testContext.CancellationToken);

            var invalidConsumerTree = CSharpSyntaxTree.ParseText(
                """
                #nullable enable
                using System;
                using Microsoft.Extensions.Caching.Memory;

                public static class InvalidConsumer
                {
                    public static void PassNulls(
                        Func<int, int> fn,
                        IMemoryCache cache,
                        MemoryCacheEntryOptions opts)
                    {
                        _ = Memoization.Memoization.Create<int, int>(null);
                        _ = Memoization.Memoization.Create<int, int>(fn, cache: null);
                        _ = Memoization.Memoization.Create<int, int>(fn, cache, options: null);
                        _ = Memoization.Memoization.Create<int, int>(fn, options: null);
                    }
                }
                """,
                modernParse,
                path: "InvalidConsumer.cs",
                cancellationToken: _testContext.CancellationToken);

            var modernCompilation = CSharpCompilation.Create(
                "MemoizationNrtCheck",
                [stubTree, validConsumerTree, invalidConsumerTree],
                references,
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    nullableContextOptions: NullableContextOptions.Enable));

            GeneratorDriver modernDriver = CSharpGeneratorDriver.Create(
                generators: [new MemoizationGenerator().AsSourceGenerator()],
                parseOptions: modernParse);
            modernDriver.RunGeneratorsAndUpdateCompilation(
                modernCompilation,
                out var updatedModern,
                out var generatorDiagnostics,
                _testContext.CancellationToken);

            Assert.IsEmpty(generatorDiagnostics);

            var modernDiagnostics = updatedModern
                .GetDiagnostics(_testContext.CancellationToken)
                .Where(static d => d.Severity >= DiagnosticSeverity.Warning)
                .ToArray();
            var nonInvalidDiagnostics = modernDiagnostics
                .Where(static d => d.Location.GetLineSpan().Path != "InvalidConsumer.cs")
                .ToArray();
            Assert.IsEmpty(
                nonInvalidDiagnostics,
                string.Join(
                    Environment.NewLine,
                    nonInvalidDiagnostics.Select(static d => d.ToString())));

            var invalidDiagnostics = modernDiagnostics
                .Where(static d => d.Location.GetLineSpan().Path == "InvalidConsumer.cs")
                .ToArray();
            Assert.HasCount(
                4,
                invalidDiagnostics,
                string.Join(
                    Environment.NewLine,
                    invalidDiagnostics.Select(static d => d.ToString())));
            Assert.IsTrue(invalidDiagnostics.All(static d => d.Id == "CS8625"));

            // Also enforce clean C# 7.3 compilation on the NETSTANDARD2_0 / NET462 branch.
            var legacyParse = new CSharpParseOptions(LanguageVersion.CSharp7_3)
                .WithPreprocessorSymbols("NETSTANDARD2_0");
            var legacyStubTree = CSharpSyntaxTree.ParseText(
                """
                using Microsoft.Extensions.Caching.Memory;

                namespace Memoization
                {
                    public static partial class Memoization
                    {
                        public static IMemoryCache DefaultCache { get; set; }
                    }
                }
                """,
                legacyParse,
                path: "LegacyStub.cs",
                cancellationToken: _testContext.CancellationToken);

            var legacyCompilation = CSharpCompilation.Create(
                "MemoizationLegacyCheck",
                [legacyStubTree],
                references,
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    generalDiagnosticOption: ReportDiagnostic.Error));

            GeneratorDriver legacyDriver = CSharpGeneratorDriver.Create(
                generators: [new MemoizationGenerator().AsSourceGenerator()],
                parseOptions: legacyParse);
            legacyDriver.RunGeneratorsAndUpdateCompilation(
                legacyCompilation,
                out var updatedLegacy,
                out var legacyGenDiagnostics,
                _testContext.CancellationToken);

            Assert.IsEmpty(legacyGenDiagnostics);
            var legacyDiagnostics = updatedLegacy
                .GetDiagnostics(_testContext.CancellationToken)
                .Where(static d => d.Severity >= DiagnosticSeverity.Warning)
                .ToArray();
            Assert.IsEmpty(
                legacyDiagnostics,
                string.Join(
                    Environment.NewLine,
                    legacyDiagnostics.Select(static d => d.ToString())));
        }

        private static Delegate BuildPositionalStringDelegate(
            int arity,
            string tag,
            Action onInvoke)
        {
            var parameters = Enumerable.Range(0, arity)
                .Select(static idx => Expression.Parameter(
                    typeof(string),
                    $"p{idx.ToString(CultureInfo.InvariantCulture)}"))
                .ToArray();
            var arrayInit = Expression.NewArrayInit(
                typeof(object),
                parameters.Select(static p => Expression.Convert(p, typeof(object))));
            var formatMethod = typeof(MemoizationContractTests).GetMethod(
                nameof(InvokeAndFormat),
                BindingFlags.NonPublic | BindingFlags.Static)!;
            var body = Expression.Call(
                formatMethod,
                Expression.Constant(tag),
                Expression.Constant(onInvoke),
                arrayInit);
            var funcType = Expression.GetFuncType(
                Enumerable.Repeat(typeof(string), arity + 1).ToArray());
            return Expression.Lambda(funcType, body, parameters).Compile();
        }

        private static string InvokeAndFormat(string tag, Action onInvoke, object?[] values)
        {
            onInvoke();
            return FormatExpected(tag, values);
        }

        private static string FormatExpected(string tag, object?[] values)
        {
            var items = values.Select(static (v, i) =>
                $"{i.ToString(CultureInfo.InvariantCulture)}={(v is null ? "<null>" : v)}");
            return $"{tag}:[{string.Join("|", items)}]";
        }
    }
}
