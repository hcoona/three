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
using Microsoft.CodeAnalysis;

namespace Hcoona.Nanopass.Generators;

/// <summary>
/// Entry point for Nanopass incremental generation.
/// </summary>
[Generator]
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
        context.RegisterSourceOutput(models, static (output, model) =>
        {
            string hint = model.QualifiedName.Substring("global::".Length) + ".Nanopass.g.cs";
            output.AddSource(hint, new LanguageEmitter(model).Emit());
        });
        context.RegisterSourceOutput(
            results.Where(static result => result.Diagnostic != 0),
            static (output, result) => output.ReportDiagnostic(Diagnostic.Create(
                Diagnostics[result.Diagnostic - 1], result.Location.ToLocation(), result.Message)));
    }

    private static DiagnosticDescriptor CreateDiagnostic(string id, string title) =>
        new(id, title, "{0}", "Nanopass", DiagnosticSeverity.Error, isEnabledByDefault: true);
}
