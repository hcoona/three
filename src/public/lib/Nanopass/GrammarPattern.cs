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
using System.Collections.Frozen;
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
    private readonly FrozenDictionary<Symbol, int> indices;
    private readonly LanguageRuntime.Field[] fields;
    private readonly SExpr syntax;
    private readonly SExpr? pretty;
    private readonly SExpr.Sym? head;
    private readonly int minimumLength;
    private readonly bool hasFixedLength;

    internal GrammarPattern(string syntax, string? pretty, LanguageRuntime.Field[] fields)
    {
        this.fields = fields;
        this.syntax = SExpr.Parse(syntax);
        this.pretty = pretty is null ? null : SExpr.Parse(pretty);
        var index = new Dictionary<Symbol, int>(fields.Length);
        for (int i = 0; i < fields.Length; i++)
        {
            index.Add(fields[i].Name, i);
        }

        this.indices = index.ToFrozenDictionary();
        SExpr pattern = this.StripMaybe(this.syntax);
        (this.minimumLength, this.hasFixedLength) = this.Measure(pattern);
        if (pattern is SExpr.Pair { Car: SExpr.Sym symbol } pair
            && !this.indices.ContainsKey(symbol.Value) && Repetition(pair) is null)
        {
            this.head = symbol;
        }
    }

    internal bool TryMatch(
        SExpr input, Func<LanguageRuntime.Field, SExpr, object?> read, out object?[] captures)
    {
        if ((this.head is not null && (input is not SExpr.Pair pair
                || !this.head.Equals(pair.Car)))
            || !HasLength(input, this.minimumLength, this.hasFixedLength))
        {
            captures = [];
            return false;
        }

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

    private static SExpr.Pair? Repetition(SExpr.Pair pair) =>
        pair.Cdr is SExpr.Pair next && IsSymbol(next.Car, "...") ? next : null;

    private static bool HasLength(SExpr input, int minimum, bool exact)
    {
        for (int i = 0; i < minimum; i++)
        {
            if (input is not SExpr.Pair pair)
            {
                return false;
            }

            input = pair.Cdr;
        }

        return !exact || input is SExpr.Nil;
    }

    private static object? Normalize(object? value, int depth) => depth == 0
        ? value
        : new SequenceColumn(((IEnumerable)value!).Cast<object?>()
            .Select(item => Normalize(item, depth - 1)).ToArray());

    private (int Minimum, bool Fixed) Measure(SExpr pattern)
    {
        int minimum = 0;
        bool repeats = false;
        while ((pattern = this.StripMaybe(pattern)) is SExpr.Pair pair)
        {
            if (Repetition(pair) is { } repetition)
            {
                repeats = true;
                pattern = repetition.Cdr;
            }
            else
            {
                minimum++;
                pattern = pair.Cdr;
            }
        }

        return (minimum, pattern is SExpr.Nil && !repeats);
    }

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
        while ((pattern = this.StripMaybe(pattern)) is SExpr.Pair pair)
        {
            if (Repetition(pair) is { } repetition)
            {
                return this.MatchRepetition(pair.Car, repetition.Cdr, input, captures, read);
            }

            if (input is not SExpr.Pair inputPair
                || !this.Match(pair.Car, inputPair.Car, captures, read))
            {
                return false;
            }

            pattern = pair.Cdr;
            input = inputPair.Cdr;
        }

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

        return pattern.Equals(input);
    }

    private bool MatchRepetition(
        SExpr item,
        SExpr suffix,
        SExpr input,
        object?[] captures,
        Func<LanguageRuntime.Field, SExpr, object?> read)
    {
        var (minimum, fixedLength) = this.Measure(suffix);
        int count = 0;
        SExpr tail = input;
        while (tail is SExpr.Pair pair)
        {
            count++;
            tail = pair.Cdr;
        }

        int limit = count - minimum;
        if (limit < 0 || (fixedLength && tail is not SExpr.Nil))
        {
            return false;
        }

        int[] columns = [.. this.CaptureIndices(item).Distinct()];
        foreach (int column in columns)
        {
            captures[column] = limit == 0 ? Array.Empty<object?>() : new object?[limit];
        }

        var splits = fixedLength ? null : new SExpr[limit + 1];
        var row = limit == 0 ? [] : new object?[this.fields.Length];
        int matched = 0;
        while (matched < limit)
        {
            if (splits is not null)
            {
                splits[matched] = input;
            }

            var pair = (SExpr.Pair)input;
            if (!this.Match(item, pair.Car, row, read))
            {
                break;
            }

            foreach (int column in columns)
            {
                ((object?[])captures[column]!)[matched] = row[column];
            }

            matched++;
            input = pair.Cdr;
        }

        if (splits is null)
        {
            if (matched != limit || !this.Match(suffix, input, captures, read))
            {
                return false;
            }
        }
        else
        {
            splits[matched] = input;
            while (matched >= 0 && !this.Match(suffix, splits[matched], captures, read))
            {
                matched--;
            }

            if (matched < 0)
            {
                return false;
            }
        }

        if (matched < limit)
        {
            foreach (int column in columns)
            {
                captures[column] = ((object?[])captures[column]!)[..matched];
            }
        }

        return true;
    }

    private SExpr Render(
        SExpr pattern, object?[] captures, Func<LanguageRuntime.Field, object, SExpr> print)
    {
        pattern = this.StripMaybe(pattern);
        if (pattern is SExpr.Sym symbol && this.indices.TryGetValue(symbol.Value, out int index))
        {
            return captures[index] is { } value
                ? print(this.fields[index], value)
                : SExpr.Bool.False;
        }

        if (pattern is not SExpr.Pair pair)
        {
            return pattern;
        }

        if (Repetition(pair) is not { } repetition)
        {
            return new SExpr.Pair(
                this.Render(pair.Car, captures, print), this.Render(pair.Cdr, captures, print));
        }

        int[] columns =
        [
            .. this.CaptureIndices(pair.Car).Distinct()
                .Where(index => captures[index] is SequenceColumn),
        ];
        if (columns.Length == 0)
        {
            throw new NanopassException("No sequence field supplies values for an ellipsis.");
        }

        int count = ((SequenceColumn)captures[columns[0]]!).Items.Length;
        if (columns.Any(i => ((SequenceColumn)captures[i]!).Items.Length != count))
        {
            throw new NanopassException("Parallel sequence fields must have the same length.");
        }

        SExpr tail = this.Render(repetition.Cdr, captures, print);
        object?[] row = (object?[])captures.Clone();
        for (int i = count - 1; i >= 0; i--)
        {
            foreach (int column in columns)
            {
                row[column] = ((SequenceColumn)captures[column]!).Items[i];
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

    private sealed record SequenceColumn(object?[] Items);
}
