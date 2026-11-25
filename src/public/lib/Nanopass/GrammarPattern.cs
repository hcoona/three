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
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace Hcoona.Nanopass;

/// <summary>
/// Matches and renders S-expression production patterns to support generated Nanopass language
/// implementations.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
internal sealed class GrammarPattern
{
    private readonly Dictionary<Symbol, int> indices;
    private readonly LanguageRuntime.Field[] fields;
    private readonly SExpr syntax;
    private readonly SExpr? pretty;

    internal GrammarPattern(string syntax, string? pretty, LanguageRuntime.Field[] fields)
    {
        this.fields = fields;
        this.syntax = SExpr.Parse(syntax);
        this.pretty = pretty is null ? null : SExpr.Parse(pretty);
        this.indices = new(fields.Length);
        for (int i = 0; i < fields.Length; i++)
        {
            this.indices.Add(fields[i].Name, i);
        }
    }

    internal bool TryMatch(
        SExpr input, Func<LanguageRuntime.Field, SExpr, object?> read, out object?[] captures)
    {
        captures = new object?[this.fields.Length];
        return this.Match(this.syntax, input, captures, read);
    }

    internal SExpr Render(
        object?[] values, bool raw, Func<LanguageRuntime.Field, object, SExpr> print)
    {
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = Normalize(values[i], this.fields[i].SequenceDepth);
        }

        SExpr template = !raw && this.pretty is not null ? this.pretty : this.syntax;
        return this.Render(template, values, print);
    }

    private static bool IsSymbol(SExpr datum, string text) =>
        datum is SExpr.Sym symbol
        && string.Equals(symbol.Value.Name, text, StringComparison.Ordinal);

    private static object? Normalize(object? value, int depth) => depth == 0
        ? value
        : ((IEnumerable)value!).Cast<object?>()
            .Select(item => Normalize(item, depth - 1)).ToArray();

    private SExpr StripMaybe(SExpr pattern) =>
        pattern is SExpr.Pair { Cdr: SExpr.Pair { Car: SExpr.Sym leaf, Cdr: SExpr.Nil } } pair
        && IsSymbol(pair.Car, "maybe")
        && this.indices.TryGetValue(leaf.Value, out int index)
        && this.fields[index].IsMaybe
            ? leaf
            : pattern;

    private bool Match(
        SExpr pattern,
        SExpr input,
        object?[] captures,
        Func<LanguageRuntime.Field, SExpr, object?> read)
    {
        pattern = this.StripMaybe(pattern);
        if (pattern is SExpr.Sym symbol && this.indices.TryGetValue(symbol.Value, out int index))
        {
            LanguageRuntime.Field field = this.fields[index];
            if (field.IsMaybe && input is SExpr.Bool { Value: false })
            {
                captures[index] = null;
                return true;
            }

            captures[index] = read(field, input);
            return captures[index] is not null;
        }

        if (pattern is not SExpr.Pair pair)
        {
            return pattern.Equals(input);
        }

        if (pair.Cdr is SExpr.Pair repetition && IsSymbol(repetition.Car, "..."))
        {
            return this.MatchRepetition(pair.Car, repetition.Cdr, input, captures, read);
        }

        return input is SExpr.Pair inputPair
            && this.Match(pair.Car, inputPair.Car, captures, read)
            && this.Match(pair.Cdr, inputPair.Cdr, captures, read);
    }

    private bool MatchRepetition(
        SExpr item,
        SExpr suffix,
        SExpr input,
        object?[] captures,
        Func<LanguageRuntime.Field, SExpr, object?> read)
    {
        int[] columns = [.. this.CaptureIndices(item).Distinct()];
        input.SplitList(out Seq<SExpr> items, out _);
        suffix.SplitList(out Seq<SExpr> required, out _);
        int count = items.Length - required.Length;
        if (count < 0)
        {
            return false;
        }

        foreach (int column in columns)
        {
            captures[column] = new object?[count];
        }

        for (int i = 0; i < count; i++)
        {
            var pair = (SExpr.Pair)input;
            object?[] row = new object?[this.fields.Length];
            if (!this.Match(item, pair.Car, row, read))
            {
                return false;
            }

            foreach (int column in columns)
            {
                ((object?[])captures[column]!)[i] = row[column];
            }

            input = pair.Cdr;
        }

        return this.Match(suffix, input, captures, read);
    }

    private SExpr Render(
        SExpr pattern, object?[] captures, Func<LanguageRuntime.Field, object, SExpr> print)
    {
        pattern = this.StripMaybe(pattern);
        if (pattern is SExpr.Sym symbol && this.indices.TryGetValue(symbol.Value, out int index))
        {
            return captures[index] is { } value
                ? print(this.fields[index], value)
                : new SExpr.Bool(false);
        }

        if (pattern is not SExpr.Pair pair)
        {
            return pattern;
        }

        if (pair.Cdr is not SExpr.Pair repetition || !IsSymbol(repetition.Car, "..."))
        {
            return new SExpr.Pair(
                this.Render(pair.Car, captures, print), this.Render(pair.Cdr, captures, print));
        }

        int[] columns =
        [
            .. this.CaptureIndices(pair.Car).Distinct()
                .Where(index => captures[index] is object?[]),
        ];
        if (columns.Length == 0)
        {
            throw new NanopassException("No sequence field supplies values for an ellipsis.");
        }

        int count = ((object?[])captures[columns[0]]!).Length;
        if (columns.Any(i => ((object?[])captures[i]!).Length != count))
        {
            throw new NanopassException("Parallel sequence fields must have the same length.");
        }

        SExpr tail = this.Render(repetition.Cdr, captures, print);
        object?[] row = (object?[])captures.Clone();
        for (int i = count - 1; i >= 0; i--)
        {
            foreach (int column in columns)
            {
                row[column] = ((object?[])captures[column]!)[i];
            }

            tail = new SExpr.Pair(this.Render(pair.Car, row, print), tail);
        }

        return tail;
    }

    private IEnumerable<int> CaptureIndices(SExpr pattern)
    {
        if (pattern is SExpr.Sym symbol && this.indices.TryGetValue(symbol.Value, out int index))
        {
            yield return index;
        }
        else if (pattern is SExpr.Pair pair)
        {
            var children = this.CaptureIndices(pair.Car).Concat(this.CaptureIndices(pair.Cdr));
            foreach (int child in children)
            {
                yield return child;
            }
        }
    }
}
