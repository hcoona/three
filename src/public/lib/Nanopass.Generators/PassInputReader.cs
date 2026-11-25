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
using System.Globalization;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hcoona.Nanopass.Generators;

internal sealed class PassInputReader(
    Compilation compilation,
    INamedTypeSymbol symbol,
    AttributeData pass,
    bool includeLocations,
    CancellationToken token)
{
    internal const string Runtime = "global::Hcoona.Nanopass.";
    internal const string PassAttribute = "Hcoona.Nanopass.PassAttribute";
    internal const string GeneratedSuffix = ".NanopassPass.g.cs";

    private static readonly SymbolDisplayFormat TypeFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions
            | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    internal static AttributeData? Attribute(ISymbol? owner, string name) =>
        owner is null ? null : Attributes(owner, name).FirstOrDefault();

    internal static ImmutableArray<AttributeData> Attributes(ISymbol owner, string name) =>
        [
            .. owner.GetAttributes().Where(attribute => string.Equals(
                attribute.AttributeClass?.ToDisplayString(),
                "Hcoona.Nanopass." + name,
                StringComparison.Ordinal)),
        ];

    internal static TypedConstant At(AttributeData attribute, int index) =>
        index < attribute.ConstructorArguments.Length
            ? attribute.ConstructorArguments[index]
            : default;

    internal static TypedConstant Named(AttributeData attribute, string name) =>
        attribute.NamedArguments.FirstOrDefault(item =>
            string.Equals(item.Key, name, StringComparison.Ordinal)).Value;

    internal static string Type(PassType type) => type.Code;

    internal static string Id(string name) => LanguageEmitter.Id(name);

    internal static string Literal(string value) => SymbolDisplay.FormatLiteral(value, quote: true);

    internal static bool Same(PassType? left, PassType? right) =>
        string.Equals(left?.Key, right?.Key, StringComparison.Ordinal);

    internal static bool IsGenerated(SyntaxTree? tree) =>
        tree?.FilePath.EndsWith(GeneratedSuffix, StringComparison.Ordinal) == true;

    internal static string AccessibilityText(Accessibility accessibility) => accessibility switch
    {
        Accessibility.Public => "public",
        Accessibility.Private => "private",
        Accessibility.Protected => "protected",
        Accessibility.ProtectedAndInternal => "private protected",
        Accessibility.ProtectedOrInternal => "protected internal",
        _ => "internal",
    };

    internal PassInput? Read()
    {
        var declaration = pass.ApplicationSyntaxReference
            ?.GetSyntax(token).FirstAncestorOrSelf<ClassDeclarationSyntax>();
        if (declaration is null || symbol.TypeKind != TypeKind.Class || symbol.IsRecord)
        {
            return null;
        }

        var containers = new Stack<string>();
        bool canEmit = !symbol.IsStatic && this.CanExtend(symbol);
        for (var owner = symbol.ContainingType; owner is not null; owner = owner.ContainingType)
        {
            canEmit &= this.CanExtend(owner);
            string kind = (owner.TypeKind, owner.IsRecord) switch
            {
                (TypeKind.Struct, true) => "record struct",
                (TypeKind.Struct, false) => "struct",
                (TypeKind.Class, true) => "record",
                (TypeKind.Interface, _) => "interface",
                _ => "class",
            };
            containers.Push($"partial {kind} {Id(owner.Name)}");
        }

        var members = symbol.GetMembers().Where(member => !member.IsImplicitlyDeclared
            && member.Locations.Any(static location =>
                location.IsInSource && !IsGenerated(location.SourceTree))).ToArray();
        var methods = members.OfType<IMethodSymbol>()
            .Where(static method => method.MethodKind == MethodKind.Ordinary)
            .Select(method => (Method: method, Syntax: (method.PartialImplementationPart
                    ?.DeclaringSyntaxReferences ?? []).Concat(method.DeclaringSyntaxReferences)
                .Select(reference => reference.GetSyntax(token))
                .OfType<MethodDeclarationSyntax>()
                .FirstOrDefault(static syntax => !IsGenerated(syntax.SyntaxTree))))
            .Where(static item => item.Syntax is not null)
            .ToArray();
        var signatures = methods.Select(item => this.ReadMethod(item.Method, item.Syntax!))
            .ToArray();
        var fromEndpoint = this.ReadEndpoint(0, "InputEntry");
        var toEndpoint = this.ReadEndpoint(1, "OutputEntry");
        var conversions = new HashSet<PassInput.Conversion>();
        for (int i = 0; i < methods.Length; i++)
        {
            if (signatures[i].Name == "Run" && toEndpoint.IsRaw
                && compilation.GetTypeByMetadataName("Hcoona.Nanopass.SExpr") is { } rawType)
            {
                ReadConversions(
                    methods[i].Method.ReturnType,
                    signatures[i].ReturnType,
                    rawType,
                    ReadType(rawType));
            }

            bool rewrite = signatures[i].Rewrites.Count > 0;
            bool tupleProcessor = signatures[i].Processor is not null
                && signatures[i].ReturnType.IsTuple;
            if (!rewrite && !tupleProcessor)
            {
                continue;
            }

            for (int j = 0; j < methods.Length; j++)
            {
                if (signatures[j].Processor is not null
                    && (rewrite || signatures[j].ReturnType.IsTuple))
                {
                    ReadConversions(
                        methods[i].Method.ReturnType,
                        signatures[i].ReturnType,
                        methods[j].Method.ReturnType,
                        signatures[j].ReturnType);
                }
            }
        }

        string namespaceName = symbol.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : symbol.ContainingNamespace.ToDisplayString();
        return new(
            symbol.Name,
            symbol.ToDisplayString(TypeFormat),
            namespaceName,
            AccessibilityText(symbol.DeclaredAccessibility),
            new(containers),
            canEmit,
            this.Location(declaration.Identifier.GetLocation()),
            fromEndpoint,
            toEndpoint,
            Named(pass, "Trace").Value is true,
            new(signatures),
            new(members.Select(static member => member.Name).Distinct(StringComparer.Ordinal)
                .OrderBy(static name => name, StringComparer.Ordinal)),
            new(conversions.OrderBy(static conversion => conversion.From, StringComparer.Ordinal)
                .ThenBy(static conversion => conversion.To, StringComparer.Ordinal)));

        void ReadConversions(ITypeSymbol from, PassType fromRef, ITypeSymbol to, PassType toRef)
        {
            if (fromRef.IsTuple && toRef.IsTuple
                && fromRef.Arguments.Count == toRef.Arguments.Count
                && from is INamedTypeSymbol fromTuple && to is INamedTypeSymbol toTuple)
            {
                for (int i = 0; i < fromRef.Arguments.Count; i++)
                {
                    ReadConversions(
                        fromTuple.TupleElements[i].Type,
                        fromRef.Arguments[i],
                        toTuple.TupleElements[i].Type,
                        toRef.Arguments[i]);
                }
            }
            else if (!Same(fromRef, toRef) && from.TypeKind != TypeKind.Error
                && to.TypeKind != TypeKind.Error && this.Converts(from, to))
            {
                conversions.Add(new(fromRef.Key, toRef.Key));
            }
        }
    }

    private static PassType ReadType(
        ITypeSymbol? type, TypeSyntax? syntax = null, SemanticModel? semantic = null)
    {
        if (syntax is NullableTypeSyntax nullableSyntax)
        {
            var value = type is INamedTypeSymbol
            { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullableValue
                ? nullableValue.TypeArguments[0]
                : type;
            return ReadType(value, nullableSyntax.ElementType, semantic) with { Nullable = true };
        }

        if (type is IArrayTypeSymbol array)
        {
            return new(
                string.Empty,
                new([
                    ReadType(
                    array.ElementType, (syntax as ArrayTypeSyntax)?.ElementType, semantic),
                ]),
                Nullable: array.NullableAnnotation == NullableAnnotation.Annotated,
                ArrayRank: array.Rank);
        }

        if (type is INamedTypeSymbol { IsTupleType: true } tuple)
        {
            var elements = (syntax as TupleTypeSyntax)?.Elements;
            return new(
                string.Empty,
                new(tuple.TupleElements.Select((field, index) =>
                    ReadType(field.Type, elements?[index].Type, semantic))),
                new(tuple.TupleElements.Select(static field =>
                    field.IsExplicitlyNamedTupleElement ? field.Name : string.Empty)));
        }

        if (type is INamedTypeSymbol
            { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
        {
            return ReadType(nullable.TypeArguments[0]) with { Nullable = true };
        }

        if (type is INamedTypeSymbol { TypeKind: not TypeKind.Error, Arity: > 0 } named)
        {
            var generic = (syntax is QualifiedNameSyntax qualified ? qualified.Right : syntax)
                as GenericNameSyntax;
            string name = named.ConstructedFrom.ToDisplayString(TypeFormat);
            return new(
                name.Substring(0, name.LastIndexOf('<')),
                new(named.TypeArguments.Select((arg, index) =>
                    ReadType(arg, generic?.TypeArgumentList.Arguments[index], semantic))),
                Nullable: named.NullableAnnotation == NullableAnnotation.Annotated);
        }

        if (type is not null && type.TypeKind != TypeKind.Error)
        {
            string text = type.ToDisplayString(TypeFormat);
            return text.EndsWith("?", StringComparison.Ordinal)
                ? new(text.Substring(0, text.Length - 1), Nullable: true)
                : new(text);
        }

        if (syntax is QualifiedNameSyntax nameSyntax && semantic is not null)
        {
            var owner = semantic.GetSymbolInfo(nameSyntax.Left).Symbol;
            string prefix = owner is not null and not IErrorTypeSymbol
                ? owner.ToDisplayString(TypeFormat)
                : ReadType(owner as ITypeSymbol, nameSyntax.Left, semantic).Code;
            return new(prefix + "." + nameSyntax.Right.WithoutTrivia());
        }

        if (type?.ContainingType is { TypeKind: not TypeKind.Error } containing)
        {
            return new(containing.ToDisplayString(TypeFormat) + "." + Id(type.Name));
        }

        return new(syntax?.WithoutTrivia().ToString()
            ?? type?.ToDisplayString(TypeFormat) ?? string.Empty);
    }

    private static string Constant(TypedConstant value)
    {
        if (value.IsNull)
        {
            return "null";
        }

        if (value.Kind == TypedConstantKind.Array)
        {
            string values = string.Join(", ", value.Values.Select(Constant));
            return $"new {ReadType(value.Type).Code} {{ {values} }}";
        }

        if (value.Kind == TypedConstantKind.Enum)
        {
            return $"({ReadType(value.Type).Code}){SymbolDisplay.FormatPrimitive(
                value.Value!, quoteStrings: true, useHexadecimalNumbers: false)}";
        }

        if (value.Kind == TypedConstantKind.Type && value.Value is ITypeSymbol type)
        {
            return $"typeof({ReadType(type).Code})";
        }

        return value.Value switch
        {
            float number when !float.IsNaN(number) && !float.IsInfinity(number) =>
                number.ToString("R", CultureInfo.InvariantCulture) + "f",
            float number => "float." + NonFinite(number),
            double number when double.IsNaN(number) || double.IsInfinity(number) =>
                "double." + NonFinite(number),
            double number => number.ToString("R", CultureInfo.InvariantCulture) + "d",
            long number => number.ToString(CultureInfo.InvariantCulture) + "L",
            ulong number => number.ToString(CultureInfo.InvariantCulture) + "UL",
            uint number => number.ToString(CultureInfo.InvariantCulture) + "U",
            _ => value.ToCSharpString(),
        };

        static string NonFinite(double number) =>
            double.IsNaN(number) ? "NaN" : number > 0 ? "PositiveInfinity" : "NegativeInfinity";
    }

    private static string ParameterModifier(IParameterSymbol parameter) => parameter.RefKind switch
    {
        RefKind.Ref => "ref ",
        RefKind.Out => "out ",
        RefKind.In => "in ",
        RefKind.RefReadOnlyParameter => "ref readonly ",
        _ => parameter.IsParams ? "params " : string.Empty,
    };

    private static string? Text(AttributeData attribute, string name) =>
        Named(attribute, name).Value as string;

    private static PassInput.Recur? ReadRecur(IParameterSymbol parameter)
    {
        if (Attribute(parameter, "RecurAttribute") is not { } attribute)
        {
            return null;
        }

        var args = Named(attribute, "Arguments");
        var names = args.Kind == TypedConstantKind.Array && !args.IsNull
            ? new EquatableArray<string>(args.Values.Select(
                static value => value.Value as string ?? string.Empty))
            : default;
        return new(Text(attribute, "Field"), Text(attribute, "Processor"), names);
    }

    private bool Converts(ITypeSymbol from, ITypeSymbol to) =>
        ((CSharpCompilation)compilation).ClassifyConversion(from, to).IsImplicit;

    private bool CanExtend(INamedTypeSymbol type) =>
        type.Arity == 0 && type.DeclaringSyntaxReferences.Any(reference =>
            reference.GetSyntax(token) is TypeDeclarationSyntax node
            && node.Modifiers.Any(SyntaxKind.PartialKeyword));

    private LocationInfo Location(Location location) =>
        includeLocations ? LocationInfo.From(location) : default;

    private PassInput.Method ReadMethod(IMethodSymbol method, MethodDeclarationSyntax syntax)
    {
        var semantic = compilation.GetSemanticModel(syntax.SyntaxTree);
        bool isPartialDeclaration = syntax.Modifiers.Any(SyntaxKind.PartialKeyword)
            && syntax.Body is null && syntax.ExpressionBody is null;
        string returnModifier = method.ReturnsByRefReadonly
            ? "ref readonly "
            : method.ReturnsByRef ? "ref " : string.Empty;
        return new(
            method.Name,
            AccessibilityText(method.DeclaredAccessibility),
            method.IsStatic,
            isPartialDeclaration,
            returnModifier,
            ReadType(method.ReturnType, syntax.ReturnType, semantic),
            new(method.TypeParameters.Select(static parameter => parameter.Name)),
            new(method.Parameters.Select((parameter, index) => new PassInput.Parameter(
                parameter.Name,
                ReadType(parameter.Type, syntax.ParameterList.Parameters[index].Type, semantic),
                parameter.IsOptional,
                ParameterModifier(parameter),
                ReadRecur(parameter),
                this.Location(syntax.ParameterList.Parameters[index].Identifier.GetLocation())))),
            this.Location(syntax.Identifier.GetLocation()),
            this.ReadProcessor(method),
            new(Attributes(method, "RewriteAttribute").Select(this.ReadRewrite)));
    }

    private PassInput.Processor? ReadProcessor(IMethodSymbol method)
    {
        if (Attribute(method, "ProcessorAttribute") is not { } attribute)
        {
            return null;
        }

        var constants = Named(attribute, "DefaultExtraReturns");
        var defaults = constants.Kind == TypedConstantKind.Array && !constants.IsNull
            ? constants.Values
            : [];
        bool valid = defaults.IsEmpty
            || (method.ReturnType is INamedTypeSymbol { IsTupleType: true } tuple
                && defaults.Length == tuple.TupleElements.Length - 1
                && defaults.Select((value, i) =>
                        (Value: value, Expected: tuple.TupleElements[i + 1].Type))
                    .All(pair => pair.Value.IsNull
                        ? pair.Expected.IsReferenceType || pair.Expected is INamedTypeSymbol
                        { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T }
                        : pair.Value.Type is { } type && this.Converts(type, pair.Expected)));
        return new(
            At(attribute, 0).Value as string,
            At(attribute, 1).Value as string,
            valid ? new(defaults.Select(Constant)) : default,
            valid);
    }

    private PassInput.Rewrite ReadRewrite(AttributeData attribute)
    {
        var symbol = At(attribute, 0).Value as ITypeSymbol;
        var type = (attribute.ApplicationSyntaxReference?.GetSyntax(token) as AttributeSyntax)
            ?.ArgumentList?.Arguments.Select(static argument => argument.Expression)
            .OfType<TypeOfExpressionSyntax>().FirstOrDefault()?.Type;
        var semantic = type is null ? null : compilation.GetSemanticModel(type.SyntaxTree);
        return new(
            symbol is null && type is null ? null : ReadType(symbol, type, semantic),
            Text(attribute, "Processor"),
            Text(attribute, "Guard"),
            Named(attribute, "IsElse").Value is true,
            Named(attribute, "Order").Value as int? ?? 0);
    }

    private PassInput.Endpoint ReadEndpoint(int index, string entryProperty)
    {
        var type = At(pass, index).Value as INamedTypeSymbol;
        var meta = Attribute(type, "LanguageMetadataAttribute");
        string? name = type?.ToDisplayString(TypeFormat);
        bool readMetadata = includeLocations || type?.DeclaringSyntaxReferences.Length == 0;
        var metadata = readMetadata && meta is not null ? At(meta, 0).Value as string : null;
        return new(name, metadata, name == Runtime + "RawTerm", Text(pass, entryProperty));
    }
}
