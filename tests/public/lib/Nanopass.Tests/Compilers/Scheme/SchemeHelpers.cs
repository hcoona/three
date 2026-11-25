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

// Adapted from the MIT-licensed Nanopass Compiler Framework (Copyright (c) 2000-2015 Dipanwita Sarkar, Andrew W. Keep, R. Kent Dybvig, Oscar Waddell). See src/public/lib/Nanopass/THIRD-PARTY-NOTICES.txt.

using System.Globalization;

namespace Hcoona.Nanopass.Tests.Compilers.Scheme;

internal static class SchemeHelpers
{
    private static readonly Dictionary<Symbol, Primitive> UserPrimitives =
        new Dictionary<Symbol, Primitive>
        {
            ["not"] = new(1, Category.Not),
            ["<"] = new(2, Category.Test),
            ["<="] = new(2, Category.Test),
            ["="] = new(2, Category.Test),
            ["boolean?"] = new(1, Category.Test),
            ["char?"] = new(1, Category.Test),
            ["eq?"] = new(2, Category.Test),
            ["integer?"] = new(1, Category.Test),
            ["null?"] = new(1, Category.Test),
            ["pair?"] = new(1, Category.Test),
            ["procedure?"] = new(1, Category.Test),
            ["vector?"] = new(1, Category.Test),
            ["zero?"] = new(1, Category.Test),
            ["*"] = new(2, Category.Value),
            ["+"] = new(2, Category.Value),
            ["-"] = new(2, Category.Value),
            ["add1"] = new(1, Category.Value),
            ["car"] = new(1, Category.Value),
            ["cdr"] = new(1, Category.Value),
            ["char->integer"] = new(1, Category.Value),
            ["cons"] = new(2, Category.Value),
            ["make-vector"] = new(1, Category.Value),
            ["quotient"] = new(2, Category.Value),
            ["remainder"] = new(2, Category.Value),
            ["sub1"] = new(1, Category.Value),
            ["vector"] = new(-1, Category.Value),
            ["vector-length"] = new(1, Category.Value),
            ["vector-ref"] = new(2, Category.Value),
            ["void"] = new(0, Category.Value),
            ["set-car!"] = new(2, Category.Effect),
            ["set-cdr!"] = new(2, Category.Effect),
            ["vector-set!"] = new(3, Category.Effect),
        };

    private static readonly Dictionary<Symbol, Primitive> SystemPrimitives =
        new Dictionary<Symbol, Primitive>
        {
            ["closure-ref"] = new(2, Category.Value),
            ["make-closure"] = new(2, Category.Value),
            ["procedure-code"] = new(1, Category.Value),
            ["closure-set!"] = new(3, Category.Effect),
            ["fref"] = new(1, Category.Value),
            ["fset!"] = new(2, Category.Effect),
            ["fincr!"] = new(1, Category.Effect),
            ["fdecr!"] = new(1, Category.Effect),
            ["href"] = new(2, Category.Value),
            ["hset!"] = new(3, Category.Effect),
            ["logand"] = new(2, Category.Value),
            ["sll"] = new(2, Category.Value),
            ["sra"] = new(2, Category.Value),
        };

    internal static bool IsConstant(SExpr value) =>
        value is SExpr.Nil or SExpr.Int or SExpr.Float or SExpr.Char or SExpr.Bool or SExpr.Str;

    internal static bool IsKeyword(Symbol value) =>
        value.Name is "quote" or "set!" or "if" or "begin" or "let" or "letrec" or "lambda";

    internal static bool IsDatum(SExpr value) => value switch
    {
        SExpr.Pair(var head, var tail) => IsDatum(head) && IsDatum(tail),
        SExpr.Vector(var items) => items.All(IsDatum),
        _ => IsConstant(value),
    };

    internal static bool IsVariable(Symbol value) => value.Name is not null;

    internal static bool IsUserPrimitive(Symbol value) => UserPrimitives.ContainsKey(value);

    internal static bool IsSystemPrimitive(Symbol value) => SystemPrimitives.ContainsKey(value);

    internal static bool IsPrimitive(Symbol value) => TryGetPrimitive(value, out _);

    internal static bool IsPredicatePrimitive(Symbol value) => HasCategory(value, Category.Test);

    internal static bool IsValuePrimitive(Symbol value) => HasCategory(value, Category.Value);

    internal static bool IsEffectPrimitive(Symbol value) => HasCategory(value, Category.Effect);

    internal static bool AcceptsUserArity(Symbol symbol, int arity) =>
        UserPrimitives.TryGetValue(symbol, out var primitive)
        && (primitive.Arity < 0 || primitive.Arity == arity);

    internal static Seq<Symbol> SetCons(Symbol value, Seq<Symbol> set) =>
        set.Contains(value) ? set : [value, .. set];

    internal static Seq<Symbol> Union() => [];

    internal static Seq<Symbol> Union(Seq<Symbol> set) => set;

    internal static Seq<Symbol> Union(Seq<Symbol> first, Seq<Symbol> second) =>
        first.IsEmpty ? second :
        second.IsEmpty || first.Equals(second) ? first :
        [.. first.Where(value => !second.Contains(value)).Reverse(), .. second];

    internal static Seq<Symbol> Union(Seq<Symbol> first, params Seq<Symbol>[] others) =>
        Union(first, others.Aggregate(Seq<Symbol>.Empty, (result, set) => Union(set, result)));

    internal static Seq<Symbol> Intersection(Seq<Symbol> first, params Seq<Symbol>[] others) =>
        others.Length == 0 ? first :
        [.. first.Where(value => others.All(set => set.Contains(value)))];

    internal static Seq<Symbol> Difference(Seq<Symbol> first, params Seq<Symbol>[] others) =>
        [.. first.Where(value => !others.Any(set => set.Contains(value)))];

    internal static bool IsSet(IEnumerable<Symbol> values)
    {
        HashSet<Symbol> seen = [];
        return values.All(seen.Add);
    }

    internal static int ListIndex<T>(T value, Seq<T> values)
    {
        for (int i = 0; i < values.Count; i++)
        {
            if (EqualityComparer<T>.Default.Equals(value, values[i]))
            {
                return i;
            }
        }

        return -1;
    }

    internal static Seq<SExpr> RequireList(SExpr datum) =>
        datum.TryToProperList(out var result) ? result :
            throw new NanopassException($"Expected a proper Scheme list: {datum}.");

    internal static Symbol RequireSymbol(SExpr datum) =>
        datum is SExpr.Sym(var result) ? result :
            throw new NanopassException($"Expected a Scheme symbol: {datum}.");

    private static bool HasCategory(Symbol value, Category category) =>
        TryGetPrimitive(value, out var primitive) && primitive.Category == category;

    private static bool TryGetPrimitive(Symbol value, out Primitive primitive) =>
        UserPrimitives.TryGetValue(value, out primitive)
        || SystemPrimitives.TryGetValue(value, out primitive);

    private enum Category
    {
        Not,
        Test,
        Value,
        Effect,
    }

    private readonly record struct Primitive(int Arity, Category Category);
}

internal sealed class SchemeSymbols
{
    private int seed;

    internal static Symbol GenLabel(Symbol symbol) => $"{symbol.Name}%";

    internal Symbol GenSymbol(Symbol symbol) =>
        string.Create(CultureInfo.InvariantCulture, $"{symbol.Name}_{++this.seed}");
}
