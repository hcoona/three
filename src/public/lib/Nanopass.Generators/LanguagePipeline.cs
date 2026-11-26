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

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hcoona.Nanopass.Generators;

internal static class LanguagePipeline
{
    internal static IncrementalValuesProvider<LanguageResolver.Result> Discover(
        IncrementalGeneratorInitializationContext context)
    {
        var inputs = context.SyntaxProvider.ForAttributeWithMetadataName(
            "Hcoona.Nanopass.LanguageAttribute",
            static (syntax, _) => syntax is ClassDeclarationSyntax,
            static (attributeContext, cancellationToken) =>
                LanguageInputReader.Read(attributeContext, cancellationToken));
        return inputs.Collect().SelectMany(static (languages, _) =>
            new LanguageResolver(languages).ResolveAll());
    }

    internal static IncrementalValuesProvider<LanguageModel> Models(
        IncrementalValuesProvider<LanguageResolver.Result> results) =>
        results.Where(static result => result.Model is not null)
            .Select(static (result, _) => result.Model!)
            .WithTrackingName("LanguageModels");
}
