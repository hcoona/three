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
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hcoona.Nanopass.Generators;

internal static class LanguagePipeline
{
    internal static IncrementalValuesProvider<
        (LanguageInput Input, LocationInfo Location)> Discover(
        IncrementalGeneratorInitializationContext context) =>
        context.SyntaxProvider.ForAttributeWithMetadataName(
            "Hcoona.Nanopass.LanguageAttribute",
            static (syntax, _) => syntax is ClassDeclarationSyntax,
            static (attributeContext, cancellationToken) => (
                LanguageInputReader.Read(
                    (INamedTypeSymbol)attributeContext.TargetSymbol,
                    attributeContext.SemanticModel.Compilation,
                    cancellationToken),
                LocationInfo.From(attributeContext.TargetNode.GetLocation())));

    internal static IncrementalValuesProvider<LanguageResolver.Result> Resolve(
        IncrementalValuesProvider<(LanguageInput Input, LocationInfo Location)> inputs) =>
        inputs.Select(static (item, _) => item.Input).Collect()
            .SelectMany(static (languages, _) => new LanguageResolver(languages).ResolveAll())
            .WithTrackingName("LanguageResolution");

    internal static IncrementalValuesProvider<LanguageModel> Models(
        IncrementalValuesProvider<LanguageResolver.Result> results) =>
        results.Where(static result => result.Model is not null)
            .Select(static (result, _) => result.Model!)
            .WithTrackingName("LanguageModels");

    internal sealed class ModelMap(ImmutableArray<LanguageModel> languages) : IEquatable<ModelMap>
    {
        private readonly EquatableArray<LanguageModel> models = new(
            languages.OrderBy(static model => model.QualifiedName, StringComparer.Ordinal));

        private readonly ImmutableDictionary<string, LanguageModel> byName =
            languages.ToImmutableDictionary(
                static model => model.QualifiedName,
                StringComparer.Ordinal);

        public bool Equals(ModelMap? other) =>
            other is not null && this.models.Equals(other.models);

        public override bool Equals(object? obj) => this.Equals(obj as ModelMap);

        public override int GetHashCode() => this.models.GetHashCode();

        internal LanguageModel? Find(string? name) =>
            name is not null && this.byName.TryGetValue(name, out var model) ? model : null;
    }
}
