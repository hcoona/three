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

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.CodeAnalysis;
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
        context.RegisterSymbolAction(AnalyzePass, SymbolKind.NamedType);
    }

    private static void AnalyzePass(SymbolAnalysisContext context)
    {
        if (context.Symbol is not INamedTypeSymbol type
            || PassInputReader.Attribute(type, "PassAttribute") is not { } attribute)
        {
            return;
        }

        var input = new PassInputReader(
            context.Compilation,
            type,
            attribute,
            includeLocations: true,
            context.CancellationToken).Read();
        if (input is not { CanEmit: true })
        {
            var location = type.Locations.FirstOrDefault(static location => location.IsInSource);
            var message = $"Pass '{type.Name}' must be a partial instance class in partial types.";
            context.ReportDiagnostic(Diagnostic.Create(InvalidPass, location, message));
            return;
        }

        var model = new PassResolver(input, null, null).Resolve();
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
            var tree = context.Compilation.SyntaxTrees.FirstOrDefault(tree =>
                tree.FilePath == problem.Location.Path
                && problem.Location.Span.End <= tree.Length);
            var location = tree is null
                ? type.Locations.FirstOrDefault(static candidate => candidate.IsInSource)
                : Location.Create(tree, problem.Location.Span);
            context.ReportDiagnostic(Diagnostic.Create(descriptor, location, fix, problem.Message));
        }
    }
}
