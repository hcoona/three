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
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace Hcoona.Nanopass;

internal sealed class SExprReader(string text)
{
    private const NumberStyles FloatStyle = NumberStyles.AllowLeadingSign
        | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent;

    private int position;

    private bool AtEnd => this.position == text.Length;

    internal static bool IsDelimiter(char ch) =>
        char.IsWhiteSpace(ch) || ch is '(' or ')' or '[' or ']' or '{' or '}'
            or '"' or '\'' or '`' or ',' or ';';

    internal static SExpr? ClassifyAtom(ReadOnlySpan<char> token)
    {
        if (token is "#t" or "#true")
        {
            return new SExpr.Bool(true);
        }

        if (token is "#f" or "#false")
        {
            return new SExpr.Bool(false);
        }

        if (token is "+inf.0" or "-inf.0")
        {
            return new SExpr.Float(
                token[0] == '+' ? double.PositiveInfinity : double.NegativeInfinity);
        }

        if (token is "+nan.0" or "-nan.0")
        {
            return new SExpr.Float(double.NaN);
        }

        if (!StartsLikeNumber(token))
        {
            return new SExpr.Sym(token.ToString());
        }

        // The numeric parsers accept trailing NULs, but they are not part of a number token.
        if (token[^1] == '\0')
        {
            return null;
        }

        if (token.IndexOfAny('.', 'e', 'E') >= 0)
        {
            bool parsedFloat = double.TryParse(
                token, FloatStyle, CultureInfo.InvariantCulture, out double floating);
            return parsedFloat ? new SExpr.Float(floating) : null;
        }

        bool parsed = long.TryParse(
            token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long integer);
        return parsed ? new SExpr.Int(integer) : null;
    }

    internal SExpr Parse()
    {
        SExpr datum = this.ReadDatum();
        this.SkipTrivia();
        if (!this.AtEnd)
        {
            throw this.Error("Expected the end of input");
        }

        return datum;
    }

    internal Seq<SExpr> ParseMany()
    {
        var items = ImmutableArray.CreateBuilder<SExpr>();
        this.SkipTrivia();
        while (!this.AtEnd)
        {
            items.Add(this.ReadDatum());
            this.SkipTrivia();
        }

        return new(items.DrainToImmutable());
    }

    private static bool TryReadHex(ReadOnlySpan<char> token, out ushort code) =>
        ushort.TryParse(
            token, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out code);

    private static bool StartsLikeNumber(ReadOnlySpan<char> token)
    {
        if (token is ['+' or '-', ..])
        {
            token = token[1..];
        }

        return !token.IsEmpty
            && (char.IsAsciiDigit(token[0])
                || (token[0] == '.' && token.Length > 1 && char.IsAsciiDigit(token[1])));
    }

    private SExpr ReadDatum()
    {
        if (!RuntimeHelpers.TryEnsureSufficientExecutionStack())
        {
            throw this.Error("The datum is nested too deeply");
        }

        this.SkipTrivia();
        if (this.AtEnd)
        {
            throw this.Error("Expected a datum");
        }

        char next = text[this.position];
        switch (next)
        {
            case '(':
            case '[':
            case '{':
                this.position++;
                char close = next switch
                {
                    '(' => ')',
                    '[' => ']',
                    _ => '}',
                };
                return this.ReadDelimited(close, vector: false);
            case ')':
            case ']':
            case '}':
                throw this.Error("Unexpected closing delimiter");
            case '\'':
                this.position++;
                return this.ReadAbbreviation("quote");
            case '`':
                this.position++;
                return this.ReadAbbreviation("quasiquote");
            case ',':
                this.position++;
                return this.Take('@')
                    ? this.ReadAbbreviation("unquote-splicing")
                    : this.ReadAbbreviation("unquote");
            case '"':
                return new SExpr.Str(this.ReadString());
            case '#':
                return this.ReadHashDatum();
            default:
                return this.ReadAtom();
        }
    }

    private SExpr ReadDelimited(char close, bool vector)
    {
        var items = ImmutableArray.CreateBuilder<SExpr>();
        while (true)
        {
            this.SkipTrivia();
            if (this.Take(close))
            {
                return vector
                    ? new SExpr.Vector(new(items.DrainToImmutable()))
                    : SExpr.List(items.DrainToImmutable().AsSpan());
            }

            if (this.AtEnd)
            {
                throw this.Error("Unterminated list or vector");
            }

            if (text[this.position] == '.' && this.AtTokenEnd(this.position + 1))
            {
                if (vector || items.Count == 0)
                {
                    throw this.Error("A dot must follow a list element");
                }

                this.position++;
                SExpr tail = this.ReadDatum();
                this.SkipTrivia();
                if (!this.Take(close))
                {
                    throw this.Error("Expected a matching closing delimiter after the dotted tail");
                }

                return SExpr.ImproperList(items.DrainToImmutable().AsSpan(), tail);
            }

            items.Add(this.ReadDatum());
        }
    }

    private SExpr ReadAbbreviation(Symbol name) =>
        SExpr.List(new SExpr.Sym(name), this.ReadDatum());

    private SExpr ReadHashDatum()
    {
        if (this.Take("#("))
        {
            return this.ReadDelimited(')', vector: true);
        }

        if (this.Take("#\\"))
        {
            return this.ReadCharacter();
        }

        if (this.Take("#&"))
        {
            throw new NanopassParseException(
                "Scheme box literals are not supported", this.position - 2);
        }

        int start = this.position;
        SExpr atom = this.ReadAtom();
        if (atom is not SExpr.Bool)
        {
            throw new NanopassParseException("Unknown # syntax", start);
        }

        return atom;
    }

    private SExpr.Char ReadCharacter()
    {
        if (this.AtEnd)
        {
            throw this.Error("Expected a character");
        }

        int start = this.position++;
        if (IsDelimiter(text[start]))
        {
            return new(text[start]);
        }

        while (!this.AtTokenEnd(this.position))
        {
            this.position++;
        }

        ReadOnlySpan<char> token = text.AsSpan(start, this.position - start);
        char ch = token switch
        {
            "space" => ' ',
            "newline" => '\n',
            "tab" => '\t',
            "return" => '\r',
            [_] => token[0],
            ['u', _, _, _, _] when TryReadHex(token[1..], out ushort code) =>
                (char)code,
            _ => throw new NanopassParseException("Unknown character literal", start),
        };
        return new(ch);
    }

    private string ReadString()
    {
        this.position++;
        StringBuilder value = new();
        while (!this.AtEnd)
        {
            char ch = text[this.position++];
            if (ch == '"')
            {
                return value.ToString();
            }

            if (ch == '\\')
            {
                if (this.AtEnd)
                {
                    break;
                }

                ch = text[this.position++] switch
                {
                    '"' => '"',
                    '\\' => '\\',
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    'u' => this.ReadHexEscape(),
                    _ => throw this.Error("Unknown escape sequence"),
                };
            }

            value.Append(ch);
        }

        throw this.Error("Unterminated string");
    }

    private char ReadHexEscape()
    {
        if (text.Length - this.position < 4
            || !TryReadHex(text.AsSpan(this.position, 4), out ushort code))
        {
            throw this.Error("Expected four hexadecimal digits");
        }

        this.position += 4;
        return (char)code;
    }

    private SExpr ReadAtom()
    {
        int start = this.position;
        StringBuilder? name = null;
        while (!this.AtTokenEnd(this.position))
        {
            char ch = text[this.position++];
            if (ch is not ('\\' or '|'))
            {
                name?.Append(ch);
                continue;
            }

            name ??= new StringBuilder().Append(text.AsSpan(start, this.position - start - 1));
            if (ch == '|')
            {
                int length = text.AsSpan(this.position).IndexOf('|');
                if (length < 0)
                {
                    throw new NanopassParseException("Unterminated quoted symbol", text.Length);
                }

                name.Append(text.AsSpan(this.position, length));
                this.position += length + 1;
            }
            else
            {
                if (this.AtEnd)
                {
                    throw this.Error("Incomplete symbol escape");
                }

                name.Append(text[this.position++]);
            }
        }

        if (name is not null)
        {
            return new SExpr.Sym(name.ToString());
        }

        ReadOnlySpan<char> token = text.AsSpan(start, this.position - start);
        if (token is ".")
        {
            throw new NanopassParseException("Unexpected dot outside a list tail", start);
        }

        return ClassifyAtom(token)
            ?? throw new NanopassParseException("Unsupported or out-of-range number", start);
    }

    private void SkipTrivia()
    {
        while (!this.AtEnd)
        {
            if (char.IsWhiteSpace(text[this.position]))
            {
                this.position++;
            }
            else if (this.Take(';'))
            {
                while (!this.AtEnd && text[this.position] is not ('\n' or '\r'))
                {
                    this.position++;
                }
            }
            else if (this.Take("#|"))
            {
                this.SkipBlockComment();
            }
            else if (this.Take("#;"))
            {
                _ = this.ReadDatum();
            }
            else
            {
                break;
            }
        }
    }

    private void SkipBlockComment()
    {
        int depth = 1;
        while (depth != 0 && !this.AtEnd)
        {
            if (this.Take("#|"))
            {
                depth++;
            }
            else if (this.Take("|#"))
            {
                depth--;
            }
            else
            {
                this.position++;
            }
        }

        if (depth != 0)
        {
            throw this.Error("Unterminated block comment");
        }
    }

    private bool AtTokenEnd(int index) =>
        index == text.Length || IsDelimiter(text[index]);

    private bool Take(char ch)
    {
        if (this.AtEnd || text[this.position] != ch)
        {
            return false;
        }

        this.position++;
        return true;
    }

    private bool Take(string prefix)
    {
        if (!text.AsSpan(this.position).StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        this.position += prefix.Length;
        return true;
    }

    private NanopassParseException Error(string message) => new(message, this.position);
}
