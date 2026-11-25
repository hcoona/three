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

    private static bool IsDelimiter(char token) =>
        char.IsWhiteSpace(token) || token is '(' or ')' or '[' or ']' or '{' or '}'
            or '"' or '\'' or '`' or ',' or ';';

    private static bool IsBareSymbol(string value)
    {
        if (value.Length == 0 || value[0] == '#'
            || value is "." or "+inf.0" or "-inf.0" or "+nan.0" or "-nan.0")
        {
            return false;
        }

        int start = value[0] is '+' or '-' ? 1 : 0;
        bool numeric = start < value.Length
            && (IsDigit(value[start])
                || (value[start] == '.' && start + 1 < value.Length && IsDigit(value[start + 1])));
        return !numeric && !value.Any(static token => token is '\\' or '|' || IsDelimiter(token));
    }

    private static bool IsDigit(char token) => token is >= '0' and <= '9';

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
            if (IsDelimiter(token) || token is '\\' or '|' || (result.Length == 0 && token == '#'))
            {
                result.Append('\\');
            }

            result.Append(token);
        }

        return result.ToString();
    }

    private sealed class Reader(string text)
    {
        private int index;

        internal bool End
        {
            get
            {
                this.SkipWhitespace();
                return this.index == text.Length;
            }
        }

        internal PatternDatum Read()
        {
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
                    return this.ReadList(token switch
                    {
                        '(' => ')',
                        '[' => ']',
                        _ => '}',
                    });
                case ')':
                case ']':
                case '}':
                    throw new FormatException("Unexpected closing delimiter.");
                case '"':
                    return new(this.ReadQuoted(start, token), default, null);
                case '\'':
                    return this.ReadAbbreviation("quote");
                case '`':
                    return this.ReadAbbreviation("quasiquote");
                case ',':
                    if (this.index < text.Length && text[this.index] == '@')
                    {
                        this.index++;
                        return this.ReadAbbreviation("unquote-splicing");
                    }

                    return this.ReadAbbreviation("unquote");
                default:
                    this.index = start;
                    return this.ReadAtom();
            }
        }

        private PatternDatum ReadAbbreviation(string name) =>
            new(null, new([new(name, default, null), this.Read()]), null);

        private PatternDatum ReadAtom()
        {
            int start = this.index;
            StringBuilder? name = null;
            while (this.index < text.Length && !IsDelimiter(text[this.index]))
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

            return name is null
                ? new(text.Substring(start, this.index - start), default, null)
                : new(name.ToString(), default, null, IsEscapedSymbol: true);
        }

        private PatternDatum ReadList(char end)
        {
            var items = new List<PatternDatum>();
            while (!this.End && text[this.index] != end)
            {
                PatternDatum item = this.Read();
                if (item.Atom == "." && !item.IsEscapedSymbol)
                {
                    if (items.Count == 0)
                    {
                        throw new FormatException("A dotted tail must follow an item or ellipsis.");
                    }

                    PatternDatum tail = this.Read();
                    if ((tail.Atom == "." && !tail.IsEscapedSymbol)
                        || this.End || text[this.index++] != end)
                    {
                        throw new FormatException("Expected a single dotted tail.");
                    }

                    if (tail.Atom is null)
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

        private string ReadQuoted(int start, char delimiter)
        {
            while (this.index < text.Length)
            {
                char next = text[this.index++];
                if (next == delimiter)
                {
                    return text.Substring(start, this.index - start);
                }

                if (next == '\\' && this.index < text.Length)
                {
                    this.index++;
                }
            }

            throw new FormatException("Unterminated quoted token in grammar pattern.");
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
