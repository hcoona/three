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
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hcoona.Nanopass.Generators;

/// <summary>
/// Entry point for Nanopass incremental generation.
/// </summary>
[Generator]
[SuppressMessage(
    "MicrosoftCodeAnalysisCorrectness",
    "RS1038",
    Justification = "Only the code fix loads workspace services.")]
public sealed class NanopassGenerator : IIncrementalGenerator
{
    private static readonly ImmutableArray<DiagnosticDescriptor> Diagnostics =
    [
        CreateDiagnostic("NP0101", "Unrecognized language"),
        CreateDiagnostic("NP0102", "Invalid language inheritance or entry"),
        CreateDiagnostic("NP0103", "Unrecognized grammar subtraction"),
        CreateDiagnostic("NP0104", "Duplicate grammar addition"),
        CreateDiagnostic("NP0105", "Malformed grammar"),
    ];

    /// <inheritdoc/>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var results = LanguagePipeline.Discover(context);
        var models = LanguagePipeline.Models(results);
        var languages = models.Select(static (model, _) =>
            (Hint: model.QualifiedName.Substring("global::".Length) + ".Nanopass.g.cs",
                Source: new LanguageEmitter(model).Emit()));
        context.RegisterSourceOutput(languages, static (output, language) =>
            output.AddSource(language.Hint, language.Source));
        context.RegisterSourceOutput(
            results.Where(static result => result.Diagnostic != 0),
            static (output, result) => output.ReportDiagnostic(Diagnostic.Create(
                Diagnostics[result.Diagnostic - 1], result.Location.ToLocation(), result.Message)));

        var passInputs = context.SyntaxProvider.ForAttributeWithMetadataName(
                PassInputReader.PassAttribute,
                static (syntax, _) => syntax is ClassDeclarationSyntax,
                static (attributeContext, token) => new PassInputReader(
                    attributeContext.SemanticModel.Compilation,
                    (INamedTypeSymbol)attributeContext.TargetSymbol,
                    attributeContext.Attributes[0],
                    includeLocations: false,
                    token).Read())
            .WithTrackingName("PassInputs");
        var passes = passInputs.Collect().Combine(models.Collect())
            .SelectMany(static (value, _) => value.Left
                .Where(static input => input is { CanEmit: true })
                .Select(input => (
                    Input: input!,
                    From: value.Right.FirstOrDefault(model =>
                        model.QualifiedName == input!.From.Name),
                    To: value.Right.FirstOrDefault(model =>
                        model.QualifiedName == input!.To.Name))))
            .Select(static (item, _) => new PassResolver(item.Input, item.From, item.To).Resolve())
            .WithTrackingName("PassResolution")
            .Select(static (model, _) =>
                (Hint: model.Declaration.QualifiedName.Substring("global::".Length)
                    + PassInputReader.GeneratedSuffix, Source: new PassEmitter(model).Emit()))
            .WithTrackingName("PassEmission");
        context.RegisterSourceOutput(passes, static (output, pass) =>
            output.AddSource(pass.Hint, pass.Source));
    }

    [SuppressMessage(
        "MicrosoftCodeAnalysisReleaseTracking",
        "RS2008",
        Justification = "Diagnostics ship with the Nanopass package.")]
    private static DiagnosticDescriptor CreateDiagnostic(string id, string title) =>
        new(id, title, "{0}", "Nanopass", DiagnosticSeverity.Error, isEnabledByDefault: true);
}
