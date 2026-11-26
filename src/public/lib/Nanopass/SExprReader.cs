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
using System.Runtime.CompilerServices;
using System.Text;

namespace Hcoona.Nanopass;

internal sealed class SExprReader(string text)
{
    private int position;

    private bool AtEnd => this.position == text.Length;

    internal static bool IsSymbolicAtom(ReadOnlySpan<char> token) =>
        SExprLiteral.ReadBoolean(token) is null
        && SExprLiteral.ReadSpecialFloat(token) is null
        && !SExprLiteral.StartsLikeNumber(token);

    internal static SExpr? ClassifyAtom(ReadOnlySpan<char> token)
    {
        if (SExprLiteral.ReadBoolean(token) is bool boolean)
        {
            return boolean ? SExpr.Bool.True : SExpr.Bool.False;
        }

        if (SExprLiteral.ReadSpecialFloat(token) is double special)
        {
            return new SExpr.Float(special);
        }

        if (!SExprLiteral.StartsLikeNumber(token))
        {
            return new SExpr.Sym(token.ToString());
        }

        if (SExprLiteral.IsFloat(token))
        {
            return SExprLiteral.TryReadFloat(token, out var floating)
                ? new SExpr.Float(floating)
                : null;
        }

        return SExprLiteral.TryReadInteger(token, out var integer) ? new SExpr.Int(integer) : null;
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

    private static NanopassParseException LiteralError(string message, int offset) =>
        new(message, offset);

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
                return new SExpr.Str(
                    SExprLiteral.ReadString(text, ref this.position, LiteralError));
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
            return new SExpr.Char(
                SExprLiteral.ReadCharacter(text, ref this.position, LiteralError));
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

    private SExpr ReadAtom()
    {
        int start = this.position;
        StringBuilder? name = null;
        while (!this.AtEnd)
        {
            int length = SExprLiteral.TokenLength(text.AsSpan(this.position), stopAtEscape: true);
            name?.Append(text.AsSpan(this.position, length));
            this.position += length;
            if (this.AtTokenEnd(this.position))
            {
                break;
            }

            name ??= new StringBuilder().Append(text.AsSpan(start, this.position - start));
            char ch = text[this.position++];
            if (ch == '|')
            {
                int quotedLength = text.AsSpan(this.position).IndexOf('|');
                if (quotedLength < 0)
                {
                    throw new NanopassParseException("Unterminated quoted symbol", text.Length);
                }

                name.Append(text.AsSpan(this.position, quotedLength));
                this.position += quotedLength + 1;
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
        index == text.Length || SExprLiteral.IsDelimiter(text[index]);

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
