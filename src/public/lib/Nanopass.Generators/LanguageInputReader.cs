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
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hcoona.Nanopass.Generators;

internal static class LanguageInputReader
{
    private const string RuntimeNamespace = "Hcoona.Nanopass";

    internal static LanguageInput Read(
        GeneratorAttributeSyntaxContext context, CancellationToken cancellationToken)
    {
        var type = (INamedTypeSymbol)context.TargetSymbol;
        var compilation = context.SemanticModel.Compilation;
        var qualifiedName = TypeName(type);
        List<LanguageInput.Inheritance> bases = [];
        List<string> entries = [];
        List<LanguageModel.Terminal> terminals = [];
        List<LanguageInput.Removal> removals = [];
        List<LanguageInput.Clause> clauses = [];
        List<LanguageInput.Native> nonTerminals = [];
        List<LanguageInput.NativeRecord> records = [];
        Stack<string> containers = new();
        bool isValidDeclaration = type.IsStatic && CanExtend(type, cancellationToken);
        for (var owner = type.ContainingType; owner is not null; owner = owner.ContainingType)
        {
            isValidDeclaration &= CanExtend(owner, cancellationToken);
            string kind = (owner.TypeKind, owner.IsRecord) switch
            {
                (TypeKind.Struct, true) => "record struct",
                (TypeKind.Struct, false) => "struct",
                (TypeKind.Class, true) => "record",
                (TypeKind.Class, false) => "class",
                _ => "interface",
            };
            containers.Push("partial " + kind + " " + Escape(owner.Name));
        }

        foreach (var attribute in type.GetAttributes())
        {
            var args = attribute.ConstructorArguments;
            switch (AttributeName(attribute))
            {
                case "ExtendsAttribute":
                case "PrunedLanguageAttribute":
                    var source = At(args, 0).Value as INamedTypeSymbol;
                    var metadata = FindAttribute(source, "LanguageMetadataAttribute");
                    bases.Add(new(
                        source is null ? string.Empty : TypeName(source),
                        Text(metadata, 0),
                        AttributeName(attribute) == "PrunedLanguageAttribute"));
                    break;
                case "EntryAttribute":
                    entries.Add(Text(attribute, 0) ?? string.Empty);
                    break;
                case "TerminalAttribute":
                    var terminalType = At(args, 0).Value as ITypeSymbol;
                    var terminalName = NamedText(attribute, "Name")
                        ?? terminalType?.Name.ToLowerInvariant();
                    terminals.Add(new(
                        terminalName ?? string.Empty,
                        terminalType is null ? string.Empty : TypeName(terminalType),
                        Strings(At(args, 1)),
                        NamedText(attribute, "Predicate"),
                        NamedText(attribute, "Prettifier"),
                        qualifiedName));
                    break;
                case "RemoveAttribute":
                    ReadRemovals(attribute, owner: null, removals);
                    break;
                case "RemoveTerminalAttribute":
                    var isType = At(args, 0).Value is ITypeSymbol;
                    var name = At(args, 0).Value is ITypeSymbol removalType
                        ? TypeName(removalType)
                        : Text(attribute, 0);
                    removals.Add(new(null, name ?? string.Empty, IsTerminal: true, isType));
                    break;
                case "NonTerminalDefAttribute":
                    clauses.Add(new(
                        Text(attribute, 0) ?? string.Empty,
                        Strings(At(args, 1)),
                        default,
                        Strings(At(args, 2)),
                        Extend: false));
                    break;
                case "ExtendNonTerminalAttribute":
                    clauses.Add(new(
                        Text(attribute, 0) ?? string.Empty,
                        Strings(Named(attribute, "MetaVars")),
                        Strings(Named(attribute, "Remove")),
                        Strings(Named(attribute, "Add")),
                        Extend: true));
                    break;
            }
        }

        foreach (INamedTypeSymbol member in type.GetTypeMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var attribute = FindAttribute(member, "NonTerminalAttribute");
            var declarations = member.DeclaringSyntaxReferences
                .Select(reference => reference.GetSyntax(cancellationToken)).ToArray();
            var unionParts = declarations.OfType<UnionDeclarationSyntax>().ToArray();
            bool union = unionParts.Length > 0;
            if (union || (attribute is not null
                && declarations.Any(static syntax => syntax is StructDeclarationSyntax)))
            {
                var cases = union
                    ? unionParts.SelectMany(ReadCases).ToList()
                    : member.UnionCaseTypes.Select(symbol => ReadTypeRef(symbol, null, type))
                        .ToList();
                if (!union && cases.Count == 0)
                {
                    var caseAttribute = FindAttribute(member, "CasesAttribute");
                    if (caseAttribute?.ApplicationSyntaxReference?.GetSyntax(cancellationToken)
                        is AttributeSyntax { ArgumentList: { } arguments })
                    {
                        var semanticModel = compilation.GetSemanticModel(arguments.SyntaxTree);
                        cases.AddRange(arguments.DescendantNodes().OfType<TypeOfExpressionSyntax>()
                            .Select(expression => ReadCaseType(expression.Type, semanticModel)));
                    }

                    if (cases.Count == 0)
                    {
                        cases.AddRange(Types(caseAttribute)
                            .Select(symbol => ReadTypeRef(symbol, null, type)));
                    }
                }

                var metas = attribute is null
                    ? default
                    : Strings(At(attribute.ConstructorArguments, 0));
                var declaration = union
                    ? LanguageModel.Declaration.Union
                    : LanguageModel.Declaration.Struct;
                nonTerminals.Add(new(member.Name, metas, new(cases), declaration));
                foreach (var memberAttribute in member.GetAttributes())
                {
                    if (AttributeName(memberAttribute) == "EntryAttribute")
                    {
                        entries.Add(Text(memberAttribute, 0) ?? member.Name);
                    }
                    else if (AttributeName(memberAttribute) == "RemoveAttribute")
                    {
                        ReadRemovals(memberAttribute, member.Name, removals);
                    }
                }

                foreach (INamedTypeSymbol nested in member.GetTypeMembers())
                {
                    var nestedRecord = ReadRecord(
                        nested, member.Name + ".", compilation, type, cancellationToken);
                    if (nestedRecord is not null)
                    {
                        records.Add(nestedRecord);
                    }
                }
            }
            else if (ReadRecord(member, string.Empty, compilation, type, cancellationToken)
                is { } record)
            {
                records.Add(record);
            }
        }

        var namespaceName = type.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : type.ContainingNamespace.ToDisplayString();
        var memberNames = type.GetMembers().Where(static member => !member.IsImplicitlyDeclared)
            .Select(static member => member.Name).Distinct(StringComparer.Ordinal)
            .OrderBy(static name => name, StringComparer.Ordinal);
        return new(
            type.Name,
            qualifiedName,
            namespaceName,
            Access(type.DeclaredAccessibility),
            new(containers),
            isValidDeclaration,
            LocationInfo.From(context.TargetNode.GetLocation()),
            new(bases),
            new(entries),
            new(terminals),
            new(removals),
            new(clauses),
            new(nonTerminals),
            new(records),
            new(memberNames));

        IEnumerable<LanguageInput.TypeRef> ReadCases(UnionDeclarationSyntax part)
        {
            var semanticModel = compilation.GetSemanticModel(part.SyntaxTree);
            return (part.ParameterList?.Parameters ?? default)
                .Select(static parameter => parameter.Type).OfType<TypeSyntax>()
                .Select(syntax => ReadCaseType(syntax, semanticModel));
        }

        LanguageInput.TypeRef ReadCaseType(TypeSyntax syntax, SemanticModel semanticModel) =>
            ReadTypeRef(semanticModel.GetTypeInfo(syntax, cancellationToken).Type, syntax, type);
    }

    private static LanguageInput.TypeRef ReadTypeRef(
        ITypeSymbol? symbol, TypeSyntax? syntax, INamedTypeSymbol language)
    {
        var typeName = symbol is null ? syntax?.ToString() ?? string.Empty : TypeName(symbol);
        if (syntax is NullableTypeSyntax && !typeName.EndsWith("?", StringComparison.Ordinal))
        {
            typeName += "?";
        }

        if (syntax is IdentifierNameSyntax identifier)
        {
            for (var owner = symbol?.ContainingType; owner is not null;
                owner = owner.ContainingType)
            {
                if (SymbolEqualityComparer.Default.Equals(owner, language))
                {
                    return new(typeName, null);
                }
            }

            return new(typeName, identifier.Identifier.ValueText);
        }

        return new(typeName, null);
    }

    private static ITypeSymbol? NullableElement(ITypeSymbol? type) =>
        type is INamedTypeSymbol named
        && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T
            ? named.TypeArguments[0]
            : null;

    private static TypeSyntax? TypeArgument(TypeSyntax? syntax)
    {
        var name = syntax switch
        {
            QualifiedNameSyntax qualified => qualified.Right,
            AliasQualifiedNameSyntax alias => alias.Name,
            _ => syntax,
        };
        return name is GenericNameSyntax { TypeArgumentList.Arguments.Count: 1 } generic
            ? generic.TypeArgumentList.Arguments[0]
            : null;
    }

    private static AttributeData? FindAttribute(INamedTypeSymbol? owner, string name) =>
        owner?.GetAttributes().FirstOrDefault(attribute =>
            string.Equals(AttributeName(attribute), name, StringComparison.Ordinal));

    private static string? AttributeName(AttributeData attribute) => string.Equals(
        attribute.AttributeClass?.ContainingNamespace.ToDisplayString(),
        RuntimeNamespace,
        StringComparison.Ordinal)
            ? attribute.AttributeClass?.Name
            : null;

    private static bool CanExtend(INamedTypeSymbol type, CancellationToken cancellationToken) =>
        type.Arity == 0 && type.DeclaringSyntaxReferences.Any(reference =>
            reference.GetSyntax(cancellationToken) is TypeDeclarationSyntax declaration
            && declaration.Modifiers.Any(SyntaxKind.PartialKeyword));

    private static string Access(Accessibility accessibility) => accessibility switch
    {
        Accessibility.Public => "public",
        Accessibility.Private => "private",
        Accessibility.Protected => "protected",
        Accessibility.ProtectedAndInternal => "private protected",
        Accessibility.ProtectedOrInternal => "protected internal",
        _ => "internal",
    };

    private static string Escape(string identifier) =>
        SyntaxFacts.GetKeywordKind(identifier) == SyntaxKind.None ? identifier : "@" + identifier;

    private static string TypeName(ITypeSymbol symbol) =>
        symbol.WithNullableAnnotation(NullableAnnotation.NotAnnotated)
            .ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    private static TypedConstant At(ImmutableArray<TypedConstant> args, int index) =>
        index < args.Length ? args[index] : default;

    private static TypedConstant Named(AttributeData? attribute, string name) =>
        attribute?.NamedArguments.FirstOrDefault(pair =>
            string.Equals(pair.Key, name, StringComparison.Ordinal)).Value ?? default;

    private static string? Text(AttributeData? attribute, int index) =>
        attribute is null ? null : At(attribute.ConstructorArguments, index).Value as string;

    private static string? NamedText(AttributeData? attribute, string name) =>
        Named(attribute, name).Value as string;

    private static EquatableArray<string> Strings(TypedConstant value) =>
        value.Kind == TypedConstantKind.Array && !value.IsNull
            ? new(value.Values.Select(static value => value.Value as string ?? string.Empty))
            : default;

    private static IEnumerable<ITypeSymbol> Types(AttributeData? attribute)
    {
        var argument = attribute is null ? default : At(attribute.ConstructorArguments, 0);
        return argument is { Kind: TypedConstantKind.Array, IsNull: false }
            ? argument.Values.Select(static item => item.Value).OfType<ITypeSymbol>()
            : [];
    }

    private static void ReadRemovals(
        AttributeData attribute, string? owner, List<LanguageInput.Removal> removals)
    {
        foreach (var type in Types(attribute))
        {
            removals.Add(new(owner, TypeName(type), IsTerminal: false, IsType: true));
        }
    }

    private static LanguageInput.NativeRecord? ReadRecord(
        INamedTypeSymbol type,
        string prefix,
        Compilation compilation,
        INamedTypeSymbol language,
        CancellationToken cancellationToken)
    {
        if (type.TypeKind != TypeKind.Class)
        {
            return null;
        }

        var declaration = type.DeclaringSyntaxReferences.Select(r => r.GetSyntax(cancellationToken))
            .OfType<RecordDeclarationSyntax>().FirstOrDefault();
        if (declaration is null)
        {
            return null;
        }

        var syntax = FindAttribute(type, "SyntaxAttribute");
        var pretty = FindAttribute(type, "PrettyAttribute");
        SemanticModel semanticModel = compilation.GetSemanticModel(declaration.SyntaxTree);
        var fields = declaration.ParameterList?.Parameters.Select(parameter =>
        {
            TypeSyntax? leafSyntax = parameter.Type;
            var parameterType = semanticModel.GetTypeInfo(parameter.Type!, cancellationToken).Type;
            var depth = 0;
            while (parameterType
                    is INamedTypeSymbol { Name: "Seq", TypeArguments.Length: 1 } sequence
                && string.Equals(
                    sequence.ContainingNamespace.ToDisplayString(),
                    RuntimeNamespace,
                    StringComparison.Ordinal))
            {
                depth++;
                parameterType = sequence.TypeArguments[0];
                leafSyntax = TypeArgument(leafSyntax);
            }

            var element = NullableElement(parameterType);
            var maybe = element is not null || leafSyntax is NullableTypeSyntax
                || parameterType?.NullableAnnotation == NullableAnnotation.Annotated;
            leafSyntax = leafSyntax is NullableTypeSyntax nullable
                ? nullable.ElementType
                : element is null ? leafSyntax : TypeArgument(leafSyntax);

            return new LanguageInput.NativeField(
                parameter.Identifier.ValueText,
                ReadTypeRef(element ?? parameterType, leafSyntax, language),
                depth,
                maybe);
        }) ?? [];
        return new(
            type.Name,
            prefix + type.Name,
            Text(syntax, 0),
            NamedText(syntax, "Name"),
            Text(pretty, 0),
            NamedText(pretty, "PrettyHandler"),
            pretty is not null,
            new(fields));
    }
}
