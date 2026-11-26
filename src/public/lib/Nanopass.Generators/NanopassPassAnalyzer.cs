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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Hcoona.Nanopass.Generators;

/// <summary>
/// Reports invalid Nanopass transformations and missing rewrite rules.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
[SuppressMessage(
    "MicrosoftCodeAnalysisCorrectness",
    "RS1038",
    Justification = "Only the code fix loads workspace services.")]
public sealed class NanopassPassAnalyzer : DiagnosticAnalyzer
{
    internal const string MissingRewriteId = "NP1001";
    internal const string StubsProperty = "RewriteStubs";
    internal const char StubSeparator = '\u001e';

    [SuppressMessage(
        "MicrosoftCodeAnalysisReleaseTracking",
        "RS2008",
        Justification = "Diagnostics ship with the Nanopass package.")]
    private static readonly DiagnosticDescriptor MissingRewrite = new(
        MissingRewriteId,
        "Missing rewrite rule",
        "{0}",
        "Nanopass",
        DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    [SuppressMessage(
        "MicrosoftCodeAnalysisReleaseTracking",
        "RS2008",
        Justification = "Diagnostics ship with the Nanopass package.")]
    private static readonly DiagnosticDescriptor MissingTerminalProcessor = new(
        "NP1002", "Missing terminal processor", "{0}", "Nanopass", DiagnosticSeverity.Error, true);

    [SuppressMessage(
        "MicrosoftCodeAnalysisReleaseTracking",
        "RS2008",
        Justification = "Diagnostics ship with the Nanopass package.")]
    private static readonly DiagnosticDescriptor InvalidPass = new(
        "NP1003", "Invalid pass declaration", "{0}", "Nanopass", DiagnosticSeverity.Error, true);

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [MissingRewrite, MissingTerminalProcessor, InvalidPass];

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.RegisterCompilationStartAction(static start =>
        {
            if (start.Compilation.GetTypeByMetadataName(PassInputReader.PassAttribute)
                is not { } passAttribute)
            {
                return;
            }

            var generatedTrees = start.Compilation.SyntaxTrees.Where(static tree =>
                tree.FilePath.EndsWith(
                    NanopassGenerator.LanguageGeneratedSuffix,
                    StringComparison.Ordinal)
                || PassInputReader.IsGenerated(tree)).ToArray();
            var preGenCompilation = generatedTrees.Length == 0
                ? start.Compilation
                : start.Compilation.RemoveSyntaxTrees(generatedTrees);
            var languages = new ConcurrentDictionary<INamedTypeSymbol, LanguageModel?>(
                SymbolEqualityComparer.Default);
            var trees = start.Compilation.SyntaxTrees.ToLookup(
                static tree => tree.FilePath, StringComparer.Ordinal);
            start.RegisterSymbolAction(
                symbol => AnalyzePass(symbol, passAttribute, preGenCompilation, languages, trees),
                SymbolKind.NamedType);
        });
    }

    [SuppressMessage(
        "MicrosoftCodeAnalysisPerformance",
        "RS1030",
        Justification = """
            The analyzer must bind pass declarations without Nanopass-generated syntax trees.
            """)]
    private static void AnalyzePass(
        SymbolAnalysisContext context,
        INamedTypeSymbol passAttribute,
        Compilation preGenCompilation,
        ConcurrentDictionary<INamedTypeSymbol, LanguageModel?> languages,
        ILookup<string, SyntaxTree> trees)
    {
        if (context.Symbol is not INamedTypeSymbol type
            || type.GetAttributes().FirstOrDefault(candidate =>
                SymbolEqualityComparer.Default.Equals(candidate.AttributeClass, passAttribute))
            is not { } attribute)
        {
            return;
        }

        var token = context.CancellationToken;
        var attributeSyntax = attribute.ApplicationSyntaxReference;
        if (attributeSyntax is not null
            && !preGenCompilation.ContainsSyntaxTree(attributeSyntax.SyntaxTree))
        {
            return;
        }

        var declaration = attributeSyntax?.GetSyntax(token)
            .FirstAncestorOrSelf<ClassDeclarationSyntax>();
        var sourceType = declaration is null
            ? null
            : preGenCompilation.GetSemanticModel(declaration.SyntaxTree)
                .GetDeclaredSymbol(declaration, token);
        var sourceAttribute = sourceType?.GetAttributes().FirstOrDefault(candidate =>
            candidate.ApplicationSyntaxReference?.SyntaxTree == attributeSyntax!.SyntaxTree
            && candidate.ApplicationSyntaxReference?.Span == attributeSyntax.Span);
        var reader = sourceType is null || sourceAttribute is null
            ? null
            : new PassInputReader(
                preGenCompilation, sourceType, sourceAttribute, includeLocations: true, token);
        var input = reader?.Read();
        if (input is not { CanEmit: true })
        {
            var location = type.Locations.FirstOrDefault(static location => location.IsInSource);
            var message = $"Pass '{type.Name}' must be a partial instance class in partial types.";
            context.ReportDiagnostic(Diagnostic.Create(InvalidPass, location, message));
            return;
        }

        var model = new PassResolver(input, Language(0, input.From), Language(1, input.To))
            .Resolve();
        var properties = ImmutableDictionary<string, string?>.Empty;
        var stubs = model.Problems.Select(static problem => problem.Stub)
            .OfType<string>().ToArray();
        if (stubs.Length > 0)
        {
            // BatchFixer can merge identical changes but not distinct insertions at one location.
            string edits = string.Join(StubSeparator.ToString(), stubs);
            properties = properties.Add(StubsProperty, edits);
        }

        bool offeredGroupFix = false;
        foreach (var problem in model.Problems)
        {
            var descriptor = problem.Number switch
            {
                1 => MissingRewrite,
                2 => MissingTerminalProcessor,
                _ => InvalidPass,
            };
            var fix = problem.Number == 1 && !offeredGroupFix ? properties : null;
            offeredGroupFix |= fix is not null;
            var tree = trees[problem.Location.Path ?? string.Empty].FirstOrDefault(tree =>
                problem.Location.Span.End <= tree.Length);
            var location = tree is null
                ? type.Locations.FirstOrDefault(static candidate => candidate.IsInSource)
                : Location.Create(tree, problem.Location.Span);
            context.ReportDiagnostic(Diagnostic.Create(descriptor, location, fix, problem.Message));
        }

        LanguageModel? Language(int index, PassInput.Endpoint endpoint) =>
            !endpoint.IsRaw && reader!.EndpointSymbol(index) is { } language
                ? languages.GetOrAdd(language, source => endpoint.Metadata is { } metadata
                    ? LanguageMetadataCodec.Decode(metadata)
                    : ResolveSource(source, preGenCompilation, token))
                : null;
    }

    private static LanguageModel? ResolveSource(
        INamedTypeSymbol root, Compilation compilation, CancellationToken token)
    {
        var inputs = ImmutableArray.CreateBuilder<LanguageInput>();
        var visited = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        Visit(root);
        return new LanguageResolver(inputs.ToImmutable()).ResolveAll().FirstOrDefault()?.Model;

        void Visit(INamedTypeSymbol symbol)
        {
            token.ThrowIfCancellationRequested();
            if (!visited.Add(symbol)
                || !SymbolEqualityComparer.Default.Equals(
                    symbol.ContainingAssembly,
                    compilation.Assembly)
                || symbol.DeclaringSyntaxReferences.Length == 0
                || PassInputReader.Attribute(symbol, "LanguageMetadataAttribute") is not null
                || PassInputReader.Attribute(symbol, "LanguageAttribute") is null)
            {
                return;
            }

            inputs.Add(LanguageInputReader.Read(symbol, compilation, token));
            foreach (var attribute in symbol.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString() is
                    "Hcoona.Nanopass.ExtendsAttribute" or "Hcoona.Nanopass.PrunedLanguageAttribute"
                    && PassInputReader.At(attribute, 0).Value is INamedTypeSymbol parent)
                {
                    Visit(parent);
                }
            }
        }
    }
}
