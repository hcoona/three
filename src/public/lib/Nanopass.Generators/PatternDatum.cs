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
using System.Linq;
using System.Text;

namespace Hcoona.Nanopass.Generators;

internal sealed record PatternDatum(
    string? Atom,
    EquatableArray<PatternDatum> Items,
    PatternDatum? Tail,
    bool IsEscapedSymbol = false)
{
    internal bool IsSymbol => this.Atom is { } atom && (this.IsEscapedSymbol || IsBareSymbol(atom));

    public override string ToString()
    {
        if (this.Atom is not null)
        {
            return this.IsEscapedSymbol ? FormatSymbol(this.Atom) : this.Atom;
        }

        var items = string.Join(" ", this.Items.Select(static item => item.ToString()));
        return this.Tail is null ? "(" + items + ")" : "(" + items + " . " + this.Tail + ")";
    }

    internal static (PatternDatum Syntax, PatternDatum? Pretty) Parse(string text)
    {
        var reader = new Reader(text);
        PatternDatum syntax = reader.Read();
        if (reader.End)
        {
            return (syntax, null);
        }

        var marker = reader.Read();
        if (marker.Atom != "=>" || marker.IsEscapedSymbol)
        {
            throw new FormatException("Expected '=>' before the prettifier.");
        }

        PatternDatum pretty = reader.Read();
        if (!reader.End)
        {
            throw new FormatException("Unexpected material after the prettifier.");
        }

        return (syntax, pretty);
    }

    private static bool IsBareSymbol(string value) =>
        value.Length > 0 && value[0] != '#' && value != "."
        && SExprLiteral.ReadSpecialFloat(value.AsSpan()) is null
        && !SExprLiteral.StartsLikeNumber(value.AsSpan())
        && !value.Any(static token => token is '\\' or '|' || SExprLiteral.IsDelimiter(token));

    private static FormatException LiteralError(string reason, int offset) =>
        new($"{reason} (offset {offset}).");

    private static string FormatSymbol(string value)
    {
        if (IsBareSymbol(value))
        {
            return value;
        }

        if (value.IndexOf('|') < 0)
        {
            return "|" + value + "|";
        }

        var result = new StringBuilder();
        foreach (char token in value)
        {
            if (SExprLiteral.IsDelimiter(token) || token is '\\' or '|'
                || (result.Length == 0 && token == '#'))
            {
                result.Append('\\');
            }

            result.Append(token);
        }

        return result.ToString();
    }

    private sealed class Reader(string text)
    {
        private const int MaximumDepth = 512;

        private int index;

        internal bool End
        {
            get
            {
                this.SkipWhitespace();
                return this.index == text.Length;
            }
        }

        internal PatternDatum Read(int depth = 0)
        {
            if (depth > MaximumDepth)
            {
                throw new FormatException("The grammar pattern is nested too deeply.");
            }

            if (this.End)
            {
                throw new FormatException("Expected a grammar pattern.");
            }

            var start = this.index++;
            char token = text[start];
            switch (token)
            {
                case '(':
                case '[':
                case '{':
                    char close = token switch
                    {
                        '(' => ')',
                        '[' => ']',
                        _ => '}',
                    };
                    return this.ReadList(close, depth + 1);
                case ')':
                case ']':
                case '}':
                    throw new FormatException("Unexpected closing delimiter.");
                case '"':
                    this.index = start;
                    _ = SExprLiteral.ReadString(text, ref this.index, LiteralError);
                    return new(text.Substring(start, this.index - start), default, null);
                case '#':
                    return this.ReadHash(start);
                case '\'':
                    return this.ReadAbbreviation("quote", depth + 1);
                case '`':
                    return this.ReadAbbreviation("quasiquote", depth + 1);
                case ',':
                    if (this.index < text.Length && text[this.index] == '@')
                    {
                        this.index++;
                        return this.ReadAbbreviation("unquote-splicing", depth + 1);
                    }

                    return this.ReadAbbreviation("unquote", depth + 1);
                default:
                    this.index = start;
                    return this.ReadAtom();
            }
        }

        private PatternDatum ReadAbbreviation(string name, int depth) =>
            new(null, new([new(name, default, null), this.Read(depth)]), null);

        private PatternDatum ReadHash(int start)
        {
            if (this.index < text.Length && text[this.index] == '\\')
            {
                this.index++;
                _ = SExprLiteral.ReadCharacter(text, ref this.index, LiteralError);
            }
            else
            {
                while (this.index < text.Length && !SExprLiteral.IsDelimiter(text[this.index]))
                {
                    this.index++;
                }

                if (SExprLiteral.ReadBoolean(text.AsSpan(start, this.index - start)) is null)
                {
                    throw new FormatException("Unsupported # literal in grammar pattern.");
                }
            }

            return new(text.Substring(start, this.index - start), default, null);
        }

        private PatternDatum ReadAtom()
        {
            int start = this.index;
            StringBuilder? name = null;
            while (this.index < text.Length && !SExprLiteral.IsDelimiter(text[this.index]))
            {
                char token = text[this.index++];
                if (token is not ('\\' or '|'))
                {
                    name?.Append(token);
                    continue;
                }

                name ??= new StringBuilder().Append(text, start, this.index - start - 1);
                if (token == '|')
                {
                    int end = text.IndexOf('|', this.index);
                    if (end < 0)
                    {
                        throw new FormatException("Unterminated quoted symbol in grammar pattern.");
                    }

                    name.Append(text, this.index, end - this.index);
                    this.index = end + 1;
                }
                else
                {
                    if (this.index == text.Length)
                    {
                        throw new FormatException("Incomplete symbol escape in grammar pattern.");
                    }

                    name.Append(text[this.index++]);
                }
            }

            if (name is not null)
            {
                return new(name.ToString(), default, null, IsEscapedSymbol: true);
            }

            var atom = text.AsSpan(start, this.index - start);
            if (SExprLiteral.StartsLikeNumber(atom))
            {
                bool parsed = SExprLiteral.IsFloat(atom)
                    ? SExprLiteral.TryReadFloat(atom, out _)
                    : SExprLiteral.TryReadInteger(atom, out _);
                if (!parsed)
                {
                    throw new FormatException(
                        "Unsupported or out-of-range number in grammar pattern.");
                }
            }

            return new(atom.ToString(), default, null);
        }

        private PatternDatum ReadList(char end, int depth)
        {
            var items = new List<PatternDatum>();
            while (!this.End && text[this.index] != end)
            {
                PatternDatum item = this.Read(depth);
                if (item.Atom == "." && !item.IsEscapedSymbol)
                {
                    if (items.Count == 0)
                    {
                        throw new FormatException("A dotted tail must follow an item or ellipsis.");
                    }

                    PatternDatum tail = this.Read(depth);
                    if ((tail.Atom == "." && !tail.IsEscapedSymbol)
                        || this.End || text[this.index++] != end)
                    {
                        throw new FormatException("Expected a single dotted tail.");
                    }

                    // A dotted maybe is one field, not two additional list items.
                    if (tail.Atom is null
                        && (tail.Items.Count == 0 || tail.Items[0].Atom != "maybe"))
                    {
                        items.AddRange(tail.Items);
                        return new(null, new(items), tail.Tail);
                    }

                    return new(null, new(items), tail);
                }

                items.Add(item);
            }

            if (this.End)
            {
                throw new FormatException("Unterminated list in grammar pattern.");
            }

            this.index++;
            return new(null, new(items), null);
        }

        private void SkipWhitespace()
        {
            while (this.index < text.Length)
            {
                if (char.IsWhiteSpace(text[this.index]))
                {
                    this.index++;
                }
                else if (text[this.index] == ';')
                {
                    while (this.index < text.Length && text[this.index] != '\n')
                    {
                        this.index++;
                    }
                }
                else
                {
                    break;
                }
            }
        }
    }
}
