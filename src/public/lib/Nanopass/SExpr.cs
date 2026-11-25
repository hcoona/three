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

/// <summary>
/// Represents an immutable S-expression datum.
/// </summary>
public closed record SExpr
{
    /// <summary>
    /// Converts a non-null CLR value to its canonical datum, or wraps it as an opaque terminal.
    /// </summary>
    /// <param name="value">The non-null CLR value to convert.</param>
    /// <returns>The corresponding <see cref="SExpr"/> datum.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is null.</exception>
    public static SExpr FromValue(object? value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return AsDatum(value) ?? new Boxed(value);
    }

    /// <summary>
    /// Reads exactly one datum, allowing comments and whitespace around it.
    /// </summary>
    /// <param name="text">The S-expression text containing a single datum.</param>
    /// <returns>The parsed <see cref="SExpr"/> datum.</returns>
    public static SExpr Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new SExprReader(text).Parse();
    }

    /// <summary>
    /// Reads all top-level data in order, ignoring comments and whitespace.
    /// </summary>
    /// <param name="text">The S-expression text containing zero or more data.</param>
    /// <returns>A sequence of parsed top-level <see cref="SExpr"/> data.</returns>
    public static Seq<SExpr> ParseMany(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new SExprReader(text).ParseMany();
    }

    /// <summary>
    /// Builds a proper list from the specified elements terminated by <see cref="Nil.Instance"/>.
    /// </summary>
    /// <param name="elements">The elements of the proper list in order.</param>
    /// <returns>The constructed proper list datum.</returns>
    public static SExpr List(params ReadOnlySpan<SExpr> elements) =>
        ImproperList(elements, Nil.Instance);

    /// <summary>
    /// Builds a list by prepending the given elements to a tail, which may itself be a list.
    /// </summary>
    /// <param name="elements">The leading elements to prepend.</param>
    /// <param name="tail">The terminating datum of the constructed chain.</param>
    /// <returns>The constructed list or improper list datum.</returns>
    public static SExpr ImproperList(ReadOnlySpan<SExpr> elements, SExpr tail)
    {
        ArgumentNullException.ThrowIfNull(tail);
        for (int i = elements.Length - 1; i >= 0; i--)
        {
            tail = new Pair(elements[i], tail);
        }

        return tail;
    }

    /// <summary>
    /// Attempts to decompose this datum as a proper list terminated by <see cref="Nil"/>.
    /// </summary>
    /// <param name="elements">
    /// When this method returns <see langword="true"/>, contains the proper list elements;
    /// otherwise, an empty sequence.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if this datum is <see cref="Nil"/> or a <see cref="Pair"/> chain
    /// ending in <see cref="Nil"/>; otherwise, <see langword="false"/>.
    /// </returns>
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
    /// <param name="elements">The leading pair elements before the final non-pair tail.</param>
    /// <param name="tail">The terminating non-pair datum.</param>
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
    /// <returns>The canonical S-expression text for this datum.</returns>
    /// <exception cref="InvalidOperationException">The datum contains an opaque value.</exception>
    public string Format() => SExprFormatter.Format(this, debug: false);

    /// <summary>
    /// Formats this datum for diagnostics, rendering any <see cref="Boxed"/> values with
    /// debug placeholders.
    /// </summary>
    /// <returns>A diagnostic string representation of this datum.</returns>
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

    /// <summary>
    /// Represents the empty list datum <c>()</c>.
    /// </summary>
    public sealed record Nil : SExpr
    {
        private Nil()
        {
        }

        /// <summary>
        /// Gets the singleton empty list datum.
        /// </summary>
        public static Nil Instance { get; } = new();
    }

    /// <summary>
    /// Represents a boolean S-expression datum (<c>#t</c> or <c>#f</c>).
    /// </summary>
    /// <param name="Value">The boolean value.</param>
    public sealed record Bool(bool Value) : SExpr;

    /// <summary>
    /// Represents an exact 64-bit signed integer S-expression datum.
    /// </summary>
    /// <param name="Value">The 64-bit signed integer value.</param>
    [SuppressMessage("Naming", "CA1720", Justification = "Use the abbreviated datum case name.")]
    public sealed record Int(long Value) : SExpr;

    /// <summary>
    /// Represents an IEEE 754 double-precision floating-point S-expression datum.
    /// </summary>
    /// <param name="Value">The double-precision floating-point value.</param>
    [SuppressMessage("Naming", "CA1720", Justification = "Use the abbreviated datum case name.")]
    public sealed record Float(double Value) : SExpr;

    /// <summary>
    /// Represents a UTF-16 character S-expression datum.
    /// </summary>
    /// <param name="Value">The character value.</param>
    [SuppressMessage("Naming", "CA1716", Justification = "Use the abbreviated datum case name.")]
    [SuppressMessage("Naming", "CA1720", Justification = "Use the abbreviated datum case name.")]
    public sealed record Char(char Value) : SExpr;

    /// <summary>
    /// Represents a string literal S-expression datum.
    /// </summary>
    /// <param name="Value">The unescaped string value.</param>
    public sealed record Str(string Value) : SExpr;

    /// <summary>
    /// Represents a symbolic S-expression datum.
    /// </summary>
    /// <param name="Value">The symbol value.</param>
    public sealed record Sym(Symbol Value) : SExpr;

    /// <summary>
    /// Represents a cons cell pairing a head (<paramref name="Car"/>) and tail
    /// (<paramref name="Cdr"/>) datum.
    /// </summary>
    /// <param name="Car">The head datum of the pair.</param>
    /// <param name="Cdr">The tail datum of the pair.</param>
    public sealed record Pair(SExpr Car, SExpr Cdr) : SExpr;

    /// <summary>
    /// Represents a vector S-expression datum <c>#(...)</c>.
    /// </summary>
    /// <param name="Items">The elements of the vector.</param>
    public sealed record Vector(Seq<SExpr> Items) : SExpr;

    /// <summary>
    /// Wraps an opaque CLR value from a custom terminal, without a textual representation.
    /// </summary>
    /// <param name="Value">The non-null opaque CLR value.</param>
    public sealed record Boxed(object Value) : SExpr
    {
        /// <summary>
        /// Gets the wrapped non-null opaque CLR value.
        /// </summary>
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
