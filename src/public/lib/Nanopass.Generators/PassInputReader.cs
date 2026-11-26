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

    internal static bool IsSzArrayInterface(PassType type) => type.Name is
        "global::System.Collections.Generic.IList"
        or "global::System.Collections.Generic.ICollection"
        or "global::System.Collections.Generic.IReadOnlyList"
        or "global::System.Collections.Generic.IReadOnlyCollection"
        or "global::System.Collections.Generic.IEnumerable";

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

    internal INamedTypeSymbol? EndpointSymbol(int index) =>
        At(pass, index).Value as INamedTypeSymbol;

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
        var fromEndpoint = this.ReadEndpoint(0, "InputEntry");
        var toEndpoint = this.ReadEndpoint(1, "OutputEntry");
        var conversions = new HashSet<PassInput.TypeConversion>();
        var projections = new HashSet<PassInput.TypeProjection>();
        var referenceTypes = new HashSet<PassType>();
        var signatures = methods.Select(item => this.ReadMethod(item.Method, item.Syntax!))
            .ToArray();
        var compiler = (CSharpCompilation)compilation;
        var processorExtras = new List<(ITypeSymbol Symbol, PassType Type)>();
        var recurExtras = new List<(ITypeSymbol Symbol, PassType Type)>();
        for (int i = 0; i < methods.Length; i++)
        {
            var signature = signatures[i];
            var method = methods[i].Method;
            var from = method.ReturnType;
            if (signature.Processor is not null && method.Parameters.Length > 0)
            {
                ReadConversion(
                    method.Parameters[0].Type,
                    Primary(from),
                    signature.Parameters[0].Type,
                    signature.ReturnType.Primary);
            }

            if (signature.Processor is not null && signature.ReturnType.IsTuple)
            {
                processorExtras.AddRange(ExtraSlots(from, signature.ReturnType));
            }

            if (signature.Rewrites.Count == 0 || (signature.ReturnType.Nullable
                && signature.ReturnType.HasTupleElements))
            {
                continue;
            }

            for (int j = 0; j < signature.Parameters.Count; j++)
            {
                if (signature.Parameters[j].Recursion is not null)
                {
                    recurExtras.AddRange(ExtraSlots(
                        methods[i].Method.Parameters[j].Type, signature.Parameters[j].Type));
                }
            }

            from = Primary(from);
            for (int j = 0; j < methods.Length; j++)
            {
                if (signatures[j].Processor is not null)
                {
                    var to = Primary(methods[j].Method.ReturnType);
                    ReadConversion(
                        from, to, signature.ReturnType.Primary, signatures[j].ReturnType.Primary);
                }
            }
        }

        var extraSources = processorExtras.Distinct().ToArray();
        foreach (var to in extraSources.Concat(recurExtras).Distinct())
        {
            foreach (var from in extraSources)
            {
                ReadConversion(from.Symbol, to.Symbol, from.Type, to.Type);
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
                .ThenBy(static conversion => conversion.To, StringComparer.Ordinal)
                .ThenBy(static conversion => conversion.IsReference)),
            new(projections
                .OrderBy(static projection => projection.From.Key, StringComparer.Ordinal)
                .ThenBy(static projection => projection.AsTarget.Key, StringComparer.Ordinal)),
            new(referenceTypes.OrderBy(static type => type.Key, StringComparer.Ordinal)));

        void ReadConversion(
            ITypeSymbol from,
            ITypeSymbol to,
            PassType fromType,
            PassType toType)
        {
            var visited = new HashSet<(
                (ITypeSymbol Symbol, PassType Type) From,
                (ITypeSymbol Symbol, PassType Type, int Depth) To)>();
            int maxNodes = Math.Max(1024, Math.Max(fromType.NodeCount, toType.NodeCount));
            int budget = Math.Max(4096, (fromType.NodeCount + toType.NodeCount) * 4);
            WalkConversion(
                from, to, fromType, toType, visited, maxNodes, varianceDepth: 0, ref budget);
        }

        void WalkConversion(
            ITypeSymbol from,
            ITypeSymbol to,
            PassType fromType,
            PassType toType,
            HashSet<(
                (ITypeSymbol Symbol, PassType Type) From,
                (ITypeSymbol Symbol, PassType Type, int Depth) To)> visited,
            int maxNodes,
            int varianceDepth,
            ref int budget)
        {
            if (budget <= 0
                || fromType.NodeCount > maxNodes
                || toType.NodeCount > maxNodes
                || !visited.Add(((from, fromType), (to, toType, varianceDepth))))
            {
                return;
            }

            budget--;
            if (from is not IErrorTypeSymbol && from.IsReferenceType)
            {
                referenceTypes.Add(fromType with { Nullable = false });
            }

            if (Same(fromType, toType) || (fromType.Nullable && !toType.Nullable))
            {
                return;
            }

            if (!fromType.Nullable && toType.Nullable)
            {
                WalkConversion(
                    from,
                    UnwrapNullableValue(to),
                    fromType,
                    toType with { Nullable = false },
                    visited,
                    maxNodes,
                    varianceDepth,
                    ref budget);
            }
            else if (fromType.HasTupleElements && toType.HasTupleElements
                && fromType.Arguments.Count == toType.Arguments.Count
                && UnwrapNullableValue(from) is INamedTypeSymbol { IsTupleType: true } fromTuple
                && UnwrapNullableValue(to) is INamedTypeSymbol { IsTupleType: true } toTuple)
            {
                for (int i = 0; i < fromType.Arguments.Count; i++)
                {
                    WalkConversion(
                        fromTuple.TupleElements[i].Type,
                        toTuple.TupleElements[i].Type,
                        fromType.Arguments[i],
                        toType.Arguments[i],
                        visited,
                        maxNodes,
                        varianceDepth,
                        ref budget);
                }
            }

            if (from is IArrayTypeSymbol array)
            {
                bool toInterface = to is INamedTypeSymbol
                    && array.IsSZArray && IsSzArrayInterface(toType);
                var element = to switch
                {
                    IArrayTypeSymbol targetArray when array.Rank == targetArray.Rank =>
                        targetArray.ElementType,
                    INamedTypeSymbol targetInterface when toInterface && varianceDepth < 50 =>
                        targetInterface.TypeArguments[0],
                    _ => null,
                };
                if (element is not null)
                {
                    WalkConversion(
                        array.ElementType,
                        element,
                        fromType.Arguments[0],
                        toType.Arguments[0],
                        visited,
                        maxNodes,
                        toInterface ? varianceDepth + 1 : varianceDepth,
                        ref budget);
                }
            }

            if (to is INamedTypeSymbol { Arity: > 0 } target && target is not IErrorTypeSymbol
                && !IsNullableValue(target) && target.Arity == toType.Arguments.Count
                && !toType.HasTupleElements && fromType.Nullable == toType.Nullable)
            {
                var variances = new EquatableArray<VarianceKind>(
                    target.TypeParameters.Select(static parameter => parameter.Variance));
                foreach (var (projected, asTarget) in this.ReadProjections(from, fromType, target))
                {
                    if (asTarget.NodeCount > 1024)
                    {
                        continue;
                    }

                    projections.Add(new(
                        fromType with { Nullable = false },
                        asTarget with { Nullable = false },
                        variances));
                    if (varianceDepth >= 50)
                    {
                        continue;
                    }

                    for (int i = 0; i < variances.Count; i++)
                    {
                        if (variances[i] is VarianceKind.In or VarianceKind.Out)
                        {
                            bool reverse = variances[i] == VarianceKind.In;
                            WalkConversion(
                                reverse ? target.TypeArguments[i] : projected.TypeArguments[i],
                                reverse ? projected.TypeArguments[i] : target.TypeArguments[i],
                                reverse ? toType.Arguments[i] : asTarget.Arguments[i],
                                reverse ? asTarget.Arguments[i] : toType.Arguments[i],
                                visited,
                                maxNodes,
                                varianceDepth + 1,
                                ref budget);
                        }
                    }
                }
            }

            if (HasErrorType(from) || HasErrorType(to))
            {
                return;
            }

            var conversion = compiler.ClassifyConversion(from, to);
            if (conversion.IsImplicit && !conversion.IsIdentity
                && (conversion.IsUserDefined
                    ? conversion.MethodSymbol is { Name: "op_Implicit" } op
                        && CompatibleOperatorLeg(from, op.Parameters[0].Type)
                        && CompatibleOperatorLeg(op.ReturnType, to)
                    : !(conversion.IsReference || conversion.IsBoxing)
                        || (!HasTypeArguments(from) && !HasTypeArguments(to))))
            {
                conversions.Add(new(fromType.Key, toType.Key, conversion.IsReference));
            }
        }
    }

    private static bool HasTypeArguments(ITypeSymbol type) =>
        UnwrapNullableValue(type) is IArrayTypeSymbol or INamedTypeSymbol { IsGenericType: true };

    private static bool CompatibleOperatorLeg(ITypeSymbol from, ITypeSymbol to) =>
        (!HasTypeArguments(from) && !HasTypeArguments(to))
        || SymbolEqualityComparer.IncludeNullability.Equals(
            UnwrapNullableValue(from).WithNullableAnnotation(NullableAnnotation.None),
            UnwrapNullableValue(to).WithNullableAnnotation(NullableAnnotation.None));

    private static bool IsNullableValue(ITypeSymbol type) =>
        type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;

    private static ITypeSymbol UnwrapNullableValue(ITypeSymbol type) =>
        type is INamedTypeSymbol nullable && IsNullableValue(nullable)
            ? nullable.TypeArguments[0]
            : type;

    private static ITypeSymbol Primary(ITypeSymbol type) =>
        type is INamedTypeSymbol { IsTupleType: true, TupleElements.Length: >= 2 } tuple
            ? tuple.TupleElements[0].Type
            : type;

    private static IEnumerable<(ITypeSymbol Symbol, PassType Type)> ExtraSlots(
        ITypeSymbol symbol, PassType type)
    {
        while (symbol is INamedTypeSymbol sequence && type.Name == Runtime + "Seq"
            && type.Arguments.Count == 1 && sequence.TypeArguments.Length == 1)
        {
            symbol = sequence.TypeArguments[0];
            type = type.Arguments[0];
        }

        if (symbol is INamedTypeSymbol nullable && IsNullableValue(nullable))
        {
            symbol = nullable.TypeArguments[0];
        }

        if (symbol is INamedTypeSymbol { IsTupleType: true } tuple && type.HasTupleElements)
        {
            for (int i = 1; i < tuple.TupleElements.Length; i++)
            {
                yield return (tuple.TupleElements[i].Type, type.Arguments[i]);
            }
        }
    }

    private static bool HasErrorType(ITypeSymbol type) => type switch
    {
        IErrorTypeSymbol => true,
        IArrayTypeSymbol array => HasErrorType(array.ElementType),
        INamedTypeSymbol named => named.TypeArguments.Any(HasErrorType)
            || (named.ContainingType is { } owner && HasErrorType(owner)),
        _ => false,
    };

    private static string DisplayName(ITypeSymbol? type) =>
        (type?.ToDisplayString(TypeFormat) ?? string.Empty).TrimEnd('?');

    private static string? DefaultExpression(IParameterSymbol parameter)
    {
        if (!parameter.IsOptional)
        {
            return null;
        }

        bool callerInfo = parameter.GetAttributes().Any(static attribute =>
            attribute.AttributeClass?.ContainingNamespace?.ToDisplayString()
            == "System.Runtime.CompilerServices"
            && attribute.AttributeClass.Name is "CallerMemberNameAttribute"
                or "CallerFilePathAttribute"
                or "CallerLineNumberAttribute"
                or "CallerArgumentExpressionAttribute");
        if (callerInfo)
        {
            return null;
        }

        if (!parameter.HasExplicitDefaultValue
            || (parameter.ExplicitDefaultValue is null && parameter.Type.IsValueType))
        {
            return "default";
        }

        if (parameter.ExplicitDefaultValue is null)
        {
            return "null!";
        }

        if (parameter.Type.TypeKind == TypeKind.Enum)
        {
            return SymbolDisplay.FormatPrimitive(
                parameter.ExplicitDefaultValue, quoteStrings: true, useHexadecimalNumbers: false);
        }

        return parameter.ExplicitDefaultValue switch
        {
            float number when !float.IsNaN(number) && !float.IsInfinity(number) =>
                number.ToString("R", CultureInfo.InvariantCulture) + "f",
            float number =>
                "float." + (float.IsNaN(number)
                    ? "NaN"
                    : number > 0 ? "PositiveInfinity" : "NegativeInfinity"),
            double number when double.IsNaN(number) || double.IsInfinity(number) =>
                "double." + (double.IsNaN(number)
                    ? "NaN"
                    : number > 0 ? "PositiveInfinity" : "NegativeInfinity"),
            double number => number.ToString("R", CultureInfo.InvariantCulture) + "d",
            decimal number => number.ToString(CultureInfo.InvariantCulture) + "m",
            long number => number.ToString(CultureInfo.InvariantCulture) + "L",
            ulong number => number.ToString(CultureInfo.InvariantCulture) + "UL",
            uint number => number.ToString(CultureInfo.InvariantCulture) + "U",
            _ => SymbolDisplay.FormatPrimitive(
                parameter.ExplicitDefaultValue, quoteStrings: true, useHexadecimalNumbers: false),
        };
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

    private static bool HasGenericName(NameSyntax syntax) => syntax switch
    {
        GenericNameSyntax => true,
        QualifiedNameSyntax qualified => qualified.Right is GenericNameSyntax
            || HasGenericName(qualified.Left),
        AliasQualifiedNameSyntax alias => alias.Name is GenericNameSyntax,
        _ => false,
    };

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

    private IEnumerable<(INamedTypeSymbol Symbol, PassType Type)> ReadProjections(
        ITypeSymbol from, PassType fromType, INamedTypeSymbol target)
    {
        if (from is IArrayTypeSymbol { IsSZArray: true } array)
        {
            var substitutions = new Dictionary<ITypeSymbol, PassType>(
                SymbolEqualityComparer.Default)
            {
                [array.ElementType] = fromType.Arguments[0],
            };
            foreach (var candidate in array.AllInterfaces.Where(MatchesDefinition))
            {
                yield return (candidate, ReadProjection(candidate, substitutions));
            }
        }
        else if (from is INamedTypeSymbol { IsTupleType: false } named
            && named is not IErrorTypeSymbol && !IsNullableValue(named))
        {
            var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.IncludeNullability);
            foreach (var candidate in Project(named, named.OriginalDefinition, fromType))
            {
                yield return candidate;
            }

            IEnumerable<(INamedTypeSymbol, PassType)> Project(
                INamedTypeSymbol actual,
                INamedTypeSymbol open,
                PassType type)
            {
                if (!seen.Add(open))
                {
                    yield break;
                }

                if (MatchesDefinition(open))
                {
                    yield return (actual, type);
                }

                // Project each declaration with its owner's arguments, including unresolved
                // syntax candidates.
                var owner = open.OriginalDefinition;
                var substitutions = new Dictionary<ITypeSymbol, PassType>(
                    SymbolEqualityComparer.Default);
                AddTypeParameters(owner, type, substitutions);
                if (open.BaseType is { } openBase && actual.BaseType is { } actualBase)
                {
                    foreach (var candidate in Project(
                        actualBase,
                        openBase,
                        ReadProjection(owner.BaseType!, substitutions, owner)))
                    {
                        yield return candidate;
                    }
                }

                for (int i = 0; i < open.Interfaces.Length; i++)
                {
                    foreach (var candidate in Project(
                        actual.Interfaces[i],
                        open.Interfaces[i],
                        ReadProjection(owner.Interfaces[i], substitutions, owner)))
                    {
                        yield return candidate;
                    }
                }
            }
        }

        static void AddTypeParameters(
            INamedTypeSymbol definition,
            PassType type,
            Dictionary<ITypeSymbol, PassType> substitutions)
        {
            for (int i = 0; i < definition.TypeParameters.Length; i++)
            {
                substitutions.Add(definition.TypeParameters[i], type.Arguments[i]);
            }

            if (definition.ContainingType is { } owner && type.ContainingType is { } ownerType)
            {
                AddTypeParameters(owner, ownerType, substitutions);
            }
        }

        bool MatchesDefinition(INamedTypeSymbol candidate) =>
            SymbolEqualityComparer.Default.Equals(
                candidate.OriginalDefinition, target.OriginalDefinition);

        PassType ReadProjection(
            INamedTypeSymbol candidate,
            Dictionary<ITypeSymbol, PassType> substitutions,
            INamedTypeSymbol? owner = null)
        {
            if (owner is not null && HasErrorType(candidate))
            {
                foreach (var reference in owner.DeclaringSyntaxReferences)
                {
                    if (reference.GetSyntax(token) is TypeDeclarationSyntax { BaseList: { } bases })
                    {
                        var semantic = compilation.GetSemanticModel(reference.SyntaxTree);
                        var matching = bases.Types.FirstOrDefault(item =>
                            SymbolEqualityComparer.Default.Equals(
                                semantic.GetTypeInfo(item.Type, token).Type, candidate));
                        if (matching is not null)
                        {
                            return this.ReadType(
                                candidate, matching.Type, semantic, substitutions: substitutions);
                        }
                    }
                }
            }

            return this.ReadType(candidate, substitutions: substitutions);
        }
    }

    private PassType ReadType(
        ITypeSymbol? type,
        TypeSyntax? syntax = null,
        SemanticModel? semantic = null,
        int rank = 0,
        IReadOnlyDictionary<ITypeSymbol, PassType>? substitutions = null)
    {
        bool annotated = type?.NullableAnnotation == NullableAnnotation.Annotated
            || syntax is NullableTypeSyntax;
        if (type is not null && substitutions?.TryGetValue(type, out var replacement) == true)
        {
            return annotated ? replacement with { Nullable = true } : replacement;
        }

        var bareSyntax = syntax is NullableTypeSyntax annotation ? annotation.ElementType : syntax;
        if (bareSyntax is IdentifierNameSyntax identifier && semantic is not null
            && semantic.GetAliasInfo(identifier, token)
                is { Target: ITypeSymbol aliasType } aliasSymbol
            && aliasSymbol.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(token)
                is UsingDirectiveSyntax { NamespaceOrType: TypeSyntax targetSyntax })
        {
            var aliasModel = targetSyntax.SyntaxTree == semantic.SyntaxTree
                ? semantic
                : compilation.GetSemanticModel(targetSyntax.SyntaxTree);
            var target = this.ReadType(aliasType, targetSyntax, aliasModel);
            return annotated ? target with { Nullable = true } : target;
        }

        if (type is INamedTypeSymbol nullable && IsNullableValue(nullable))
        {
            return this.ReadType(
                nullable.TypeArguments[0],
                (syntax as NullableTypeSyntax)?.ElementType,
                semantic,
                substitutions: substitutions)
                with
            { Nullable = true };
        }

        syntax = bareSyntax;
        if (type is IArrayTypeSymbol array)
        {
            var arraySyntax = syntax as ArrayTypeSyntax;
            bool moreRanks = arraySyntax is not null && rank + 1 < arraySyntax.RankSpecifiers.Count;
            var element = this.ReadType(
                array.ElementType,
                moreRanks ? arraySyntax : arraySyntax?.ElementType,
                semantic,
                moreRanks ? rank + 1 : 0,
                substitutions);
            return new(
                string.Empty,
                new([element]),
                Nullable: annotated,
                ArrayRank: array.Rank);
        }

        if (type is INamedTypeSymbol { IsTupleType: true, TupleElements.Length: >= 2 } tuple)
        {
            return new(
                string.Empty,
                new(tuple.TupleElements.Select((field, index) => this.ReadType(
                    field.Type,
                    (syntax as TupleTypeSyntax)?.Elements[index].Type,
                    semantic,
                    substitutions: substitutions))),
                new(tuple.TupleElements.Select(static field =>
                    field.IsExplicitlyNamedTupleElement ? field.Name : string.Empty)),
                Nullable: annotated);
        }

        if (type is IErrorTypeSymbol && syntax is NameSyntax nameSyntax && semantic is not null)
        {
            if (HasGenericName(nameSyntax))
            {
                return this.ReadUnboundName(nameSyntax, semantic) with { Nullable = annotated };
            }

            var candidates = this.TypeCandidates(nameSyntax, semantic)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            return new(
                candidates.Length == 1 ? candidates[0] : DisplayName(type),
                Nullable: annotated,
                Candidates: new(candidates));
        }

        if (type is INamedTypeSymbol named
            && (named.Arity > 0 || named.ContainingType?.IsGenericType == true))
        {
            var simpleSyntax = syntax switch
            {
                QualifiedNameSyntax qualified => qualified.Right,
                AliasQualifiedNameSyntax alias => alias.Name,
                _ => syntax as SimpleNameSyntax,
            };
            var arguments = (simpleSyntax as GenericNameSyntax)?.TypeArgumentList.Arguments;
            var owner = named.ContainingType is { } containing
                ? this.ReadType(
                    containing,
                    (syntax as QualifiedNameSyntax)?.Left,
                    semantic,
                    substitutions: substitutions)
                : null;
            string name = owner is not null ? Id(named.Name) : DisplayName(named.ConstructedFrom);
            return new(
                owner is null ? name.Substring(0, name.LastIndexOf('<')) : name,
                new(named.TypeArguments.Select((argument, index) =>
                    this.ReadType(
                        argument, arguments?[index], semantic, substitutions: substitutions))),
                Nullable: annotated,
                ContainingType: owner);
        }

        return new(DisplayName(type), Nullable: annotated);
    }

    private PassType ReadUnboundName(NameSyntax syntax, SemanticModel semantic)
    {
        if (semantic.GetSymbolInfo(syntax, token).Symbol is INamespaceOrTypeSymbol bound
            && bound is not IErrorTypeSymbol)
        {
            return bound is ITypeSymbol type
                ? this.ReadType(type, syntax, semantic)
                : new(bound.ToDisplayString(TypeFormat));
        }

        return syntax switch
        {
            QualifiedNameSyntax qualified => ReadSimple(qualified.Right) with
            {
                ContainingType = this.ReadUnboundName(qualified.Left, semantic),
            },
            AliasQualifiedNameSyntax alias => ReadSimple(alias.Name) with
            {
                Name = AliasPrefix(alias.Alias) + Id(alias.Name.Identifier.ValueText),
            },
            SimpleNameSyntax simple => ReadSimple(simple),
            _ => new(syntax.ToString()),
        };

        string AliasPrefix(IdentifierNameSyntax alias) => alias.Identifier.Text != "global"
            && semantic.GetAliasInfo(alias, token)?.Target is { } target
                ? target.ToDisplayString(TypeFormat) + "."
                : alias.Identifier.Text + "::";

        PassType ReadSimple(SimpleNameSyntax name)
        {
            EquatableArray<PassType> arguments = name is GenericNameSyntax generic
                ? new(generic.TypeArgumentList.Arguments.Select(argument =>
                    this.ReadType(semantic.GetTypeInfo(argument, token).Type, argument, semantic)))
                : default;
            return new(Id(name.Identifier.ValueText), arguments);
        }
    }

    private IEnumerable<string> TypeCandidates(
        NameSyntax syntax, SemanticModel semantic, bool expandAlias = true)
    {
        if (HasGenericName(syntax))
        {
            yield break;
        }

        if (semantic.GetSymbolInfo(syntax, token).Symbol is INamespaceOrTypeSymbol bound
            && bound is not IErrorTypeSymbol)
        {
            yield return bound.ToDisplayString(TypeFormat);
            yield break;
        }

        switch (syntax)
        {
            case QualifiedNameSyntax qualified:
                foreach (string prefix in this.TypeCandidates(
                    qualified.Left, semantic, expandAlias))
                {
                    yield return prefix + "." + Id(qualified.Right.Identifier.ValueText);
                }

                break;
            case AliasQualifiedNameSyntax alias:
                if (alias.Alias.Identifier.Text == "global")
                {
                    yield return "global::" + Id(alias.Name.Identifier.ValueText);
                }
                else if (semantic.GetAliasInfo(alias.Alias, token)?.Target is { } target)
                {
                    yield return target.ToDisplayString(TypeFormat)
                        + "." + Id(alias.Name.Identifier.ValueText);
                }

                break;
            case IdentifierNameSyntax identifier:
                string name = Id(identifier.Identifier.ValueText);
                var scopes = semantic.GetImportScopes(syntax.SpanStart, token);
                var usingAlias = scopes.SelectMany(static scope => scope.Aliases)
                    .FirstOrDefault(candidate => candidate.Name == identifier.Identifier.ValueText);
                if (usingAlias is not null)
                {
                    if (usingAlias.Target is not IErrorTypeSymbol)
                    {
                        yield return usingAlias.Target.ToDisplayString(TypeFormat);
                    }
                    else if (expandAlias
                        && usingAlias.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax(token)
                            is UsingDirectiveSyntax { Name: NameSyntax targetName })
                    {
                        var aliasModel = targetName.SyntaxTree == semantic.SyntaxTree
                            ? semantic
                            : compilation.GetSemanticModel(targetName.SyntaxTree);
                        foreach (string candidate in this.TypeCandidates(
                            targetName, aliasModel, expandAlias: false))
                        {
                            yield return candidate;
                        }
                    }

                    yield break;
                }

                var enclosing = semantic.GetEnclosingSymbol(syntax.SpanStart, token);
                for (var owner = enclosing as INamedTypeSymbol ?? enclosing?.ContainingType;
                    owner is not null; owner = owner.ContainingType)
                {
                    yield return owner.ToDisplayString(TypeFormat) + "." + name;
                }

                for (var ns = enclosing?.ContainingNamespace; ns is { IsGlobalNamespace: false };
                    ns = ns.ContainingNamespace)
                {
                    yield return ns.ToDisplayString(TypeFormat) + "." + name;
                }

                foreach (var import in scopes.SelectMany(static scope => scope.Imports))
                {
                    yield return import.NamespaceOrType.ToDisplayString(TypeFormat) + "." + name;
                }

                yield return "global::" + name;
                break;
        }
    }

    private string Constant(TypedConstant value)
    {
        if (value.IsNull)
        {
            return "null";
        }

        if (value.Kind == TypedConstantKind.Array)
        {
            string values = string.Join(", ", value.Values.Select(this.Constant));
            return $"new {this.ReadType(value.Type).Code} {{ {values} }}";
        }

        if (value.Kind == TypedConstantKind.Enum)
        {
            return $"({this.ReadType(value.Type).Code}){SymbolDisplay.FormatPrimitive(
                value.Value!, quoteStrings: true, useHexadecimalNumbers: false)}";
        }

        if (value.Kind == TypedConstantKind.Type && value.Value is ITypeSymbol type)
        {
            return $"typeof({this.ReadType(type).Code})";
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
            byte or sbyte or short or ushort =>
                $"(({this.ReadType(value.Type).Code}){value.ToCSharpString()})",
            _ => value.ToCSharpString(),
        };

        static string NonFinite(double number) =>
            double.IsNaN(number) ? "NaN" : number > 0 ? "PositiveInfinity" : "NegativeInfinity";
    }

    private PassInput.Processor? ReadProcessor(IMethodSymbol method)
    {
        if (Attribute(method, "ProcessorAttribute") is not { } attribute)
        {
            return null;
        }

        var constants = Named(attribute, "DefaultExtraReturns");
        bool hasDefaults = constants.Kind == TypedConstantKind.Array;
        EquatableArray<string>? defaults = hasDefaults && !constants.IsNull
            ? new(constants.Values.Select(this.Constant))
            : null;
        return new(
            At(attribute, 0).Value as string,
            At(attribute, 1).Value as string,
            defaults,
            hasDefaults);
    }

    private PassInput.Rewrite ReadRewrite(AttributeData attribute)
    {
        var type = At(attribute, 0).Value as ITypeSymbol;
        var syntax = (attribute.ApplicationSyntaxReference?.GetSyntax(token) as AttributeSyntax)
            ?.ArgumentList?.Arguments.FirstOrDefault(static argument => argument.NameEquals is null)
            ?.Expression as TypeOfExpressionSyntax;
        var semantic = syntax is not null ? compilation.GetSemanticModel(syntax.SyntaxTree) : null;
        var production = type is not null || syntax is not null
            ? this.ReadType(
                type ?? semantic!.GetTypeInfo(syntax!.Type, token).Type, syntax?.Type, semantic)
            : null;
        return new(
            production,
            Text(attribute, "Processor"),
            Text(attribute, "Guard"),
            Named(attribute, "IsElse").Value is true,
            Named(attribute, "Order").Value as int? ?? 0);
    }

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
            this.ReadType(method.ReturnType, syntax.ReturnType, semantic),
            new(method.TypeParameters.Select(static parameter => parameter.Name)),
            new(method.Parameters.Select((parameter, index) => new PassInput.Parameter(
                parameter.Name,
                this.ReadType(
                    parameter.Type, syntax.ParameterList.Parameters[index].Type, semantic),
                parameter.IsOptional,
                ParameterModifier(parameter),
                ReadRecur(parameter),
                this.Location(syntax.ParameterList.Parameters[index].Identifier.GetLocation()),
                DefaultExpression(parameter)))),
            this.Location(syntax.Identifier.GetLocation()),
            this.ReadProcessor(method),
            new(Attributes(method, "RewriteAttribute").Select(this.ReadRewrite)));
    }

    private PassInput.Endpoint ReadEndpoint(int index, string entryProperty)
    {
        var type = this.EndpointSymbol(index);
        var meta = Attribute(type, "LanguageMetadataAttribute");
        string? name = type?.ToDisplayString(TypeFormat);
        var metadata = meta is null ? null : At(meta, 0).Value as string;
        return new(name, metadata, name == Runtime + "RawTerm", Text(pass, entryProperty));
    }
}
