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
using System.Diagnostics.CodeAnalysis;

namespace Hcoona.Nanopass;

public closed record SExpr
{
    /// <summary>
    /// Converts a non-null CLR value to its canonical datum, or wraps it as an opaque terminal.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public static SExpr FromValue(object? value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return AsDatum(value) ?? new Boxed(value);
    }

    /// <summary>
    /// Reads exactly one datum, allowing comments and whitespace around it.
    /// </summary>
    public static SExpr Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new SExprReader(text).Parse();
    }

    /// <summary>
    /// Reads all top-level data in order, ignoring comments and whitespace.
    /// </summary>
    public static Seq<SExpr> ParseMany(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new SExprReader(text).ParseMany();
    }

    public static SExpr List(params ReadOnlySpan<SExpr> elements) =>
        ImproperList(elements, Nil.Instance);

    /// <summary>
    /// Builds a list by prepending the given elements to a tail, which may itself be a list.
    /// </summary>
    public static SExpr ImproperList(ReadOnlySpan<SExpr> elements, SExpr tail)
    {
        ArgumentNullException.ThrowIfNull(tail);
        for (int i = elements.Length - 1; i >= 0; i--)
        {
            tail = new Pair(elements[i], tail);
        }

        return tail;
    }

    public bool TryToProperList(out Seq<SExpr> elements)
    {
        this.SplitList(out elements, out SExpr tail);
        if (tail is Nil)
        {
            return true;
        }

        elements = default;
        return false;
    }

    /// <summary>
    /// Separates the list's leading elements from its final non-pair tail.
    /// </summary>
    public void SplitList(out Seq<SExpr> elements, out SExpr tail)
    {
        if (this is not Pair)
        {
            elements = default;
            tail = this;
            return;
        }

        var builder = ImmutableArray.CreateBuilder<SExpr>();
        tail = this;
        while (tail is Pair pair)
        {
            builder.Add(pair.Car);
            tail = pair.Cdr;
        }

        elements = new(builder.DrainToImmutable());
    }

    /// <summary>
    /// Formats a datum as S-expression text.
    /// </summary>
    /// <exception cref="InvalidOperationException">The datum contains an opaque value.</exception>
    public string Format() => SExprFormatter.Format(this, debug: false);

    public sealed override string ToString() => SExprFormatter.Format(this, debug: true);

    private static SExpr? AsDatum(object value) => value switch
    {
        SExpr datum => datum,
        bool boolean => new Bool(boolean),
        int integer => new Int(integer),
        long integer => new Int(integer),
        short integer => new Int(integer),
        byte integer => new Int(integer),
        float floating => new Float(floating),
        double floating => new Float(floating),
        char character => new Char(character),
        string text => new Str(text),
        Symbol symbol => new Sym(symbol),
        _ => null,
    };

    public sealed record Nil : SExpr
    {
        private Nil()
        {
        }

        public static Nil Instance { get; } = new();
    }

    public sealed record Bool(bool Value) : SExpr;

    [SuppressMessage("Naming", "CA1720", Justification = "Use the abbreviated datum case name.")]
    public sealed record Int(long Value) : SExpr;

    [SuppressMessage("Naming", "CA1720", Justification = "Use the abbreviated datum case name.")]
    public sealed record Float(double Value) : SExpr;

    [SuppressMessage("Naming", "CA1716", Justification = "Use the abbreviated datum case name.")]
    [SuppressMessage("Naming", "CA1720", Justification = "Use the abbreviated datum case name.")]
    public sealed record Char(char Value) : SExpr;

    public sealed record Str(string Value) : SExpr;

    public sealed record Sym(Symbol Value) : SExpr;

    public sealed record Pair(SExpr Car, SExpr Cdr) : SExpr;

    public sealed record Vector(Seq<SExpr> Items) : SExpr;

    /// <summary>
    /// Wraps an opaque CLR value from a custom terminal, without a textual representation.
    /// </summary>
    public sealed record Boxed(object Value) : SExpr
    {
        public object Value { get; } = RequireOpaque(Value);

        private static object RequireOpaque(object value)
        {
            ArgumentNullException.ThrowIfNull(value);
            if (AsDatum(value) is not null)
            {
                throw new ArgumentException(
                    "Use SExpr.FromValue or the corresponding typed SExpr case.", nameof(value));
            }

            return value;
        }
    }
}
