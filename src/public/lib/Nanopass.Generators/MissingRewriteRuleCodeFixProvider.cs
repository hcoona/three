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
using System.Composition;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;

namespace Hcoona.Nanopass.Generators;

/// <summary>
/// Inserts placeholder implementations for required rewrite rules.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(MissingRewriteRuleCodeFixProvider))]
[Shared]
public sealed class MissingRewriteRuleCodeFixProvider : CodeFixProvider
{
    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds =>
        [NanopassPassAnalyzer.MissingRewriteId];

    /// <inheritdoc/>
    public override FixAllProvider GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    /// <inheritdoc/>
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken)
            .ConfigureAwait(false);
        if (root is null)
        {
            return;
        }

        foreach (var diagnostic in context.Diagnostics)
        {
            var key = NanopassPassAnalyzer.StubsProperty;
            if (!diagnostic.Properties.TryGetValue(key, out var stubs)
                || stubs is null || root.FindToken(diagnostic.Location.SourceSpan.Start).Parent
                    ?.FirstAncestorOrSelf<ClassDeclarationSyntax>() is not { } declaration)
            {
                continue;
            }

            var additions = stubs.Split(NanopassPassAnalyzer.StubSeparator)
                .Select(static stub => SyntaxFactory.ParseMemberDeclaration(stub))
                .OfType<MethodDeclarationSyntax>()
                .Select(static method => method.WithAdditionalAnnotations(Formatter.Annotation))
                .ToArray();
            if (additions.Length == 0)
            {
                continue;
            }

            var replacement = declaration.AddMembers(additions);
            if (!declaration.SemicolonToken.IsKind(SyntaxKind.None))
            {
                replacement = replacement.WithSemicolonToken(default)
                    .WithOpenBraceToken(SyntaxFactory.Token(SyntaxKind.OpenBraceToken))
                    .WithCloseBraceToken(SyntaxFactory.Token(SyntaxKind.CloseBraceToken)
                        .WithTrailingTrivia(declaration.SemicolonToken.TrailingTrivia))
                    .WithAdditionalAnnotations(Formatter.Annotation);
            }

            string title = additions.Length == 1
                ? "Add missing rewrite rule"
                : "Add missing rewrite rules";
            context.RegisterCodeFix(
                CodeAction.Create(
                    title,
                    _ => Task.FromResult(context.Document.WithSyntaxRoot(
                        root.ReplaceNode(declaration, replacement))),
                    nameof(MissingRewriteRuleCodeFixProvider)),
                diagnostic);
        }
    }
}
