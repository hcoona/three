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
using System.Globalization;
using System.Linq;
using System.Text;

namespace Hcoona.Nanopass.Generators;

internal static class LanguageMetadataCodec
{
    private const string Prefix = "(nanopass-v1 ";

    internal static string Encode(LanguageModel model)
    {
        var buffer = new StringBuilder(Prefix);
        Text(model.Name);
        Text(model.QualifiedName);
        Text(model.Namespace);
        Text(model.Accessibility);
        Strings(model.Containers);
        Text(model.Entry);
        Integer(model.Terminals.Count);
        foreach (var terminal in model.Terminals)
        {
            Text(terminal.Name);
            Text(terminal.Type);
            Strings(terminal.MetaVars);
            Text(terminal.Predicate);
            Text(terminal.Prettifier);
            Text(terminal.Owner);
        }

        Integer(model.NonTerminals.Count);
        foreach (var nonTerminal in model.NonTerminals)
        {
            Text(nonTerminal.Name);
            Strings(nonTerminal.MetaVars);
            Integer((int)nonTerminal.Declared);
            Integer(nonTerminal.Productions.Count);
            foreach (var rule in nonTerminal.Productions)
            {
                Text(rule.Name);
                Text(rule.Syntax);
                Text(rule.Pretty);
                Text(rule.PrettyHandler?.Name);
                if (rule.PrettyHandler is not null)
                {
                    Text(rule.PrettyHandler.Owner);
                }

                Integer(rule.Fields.Count);
                foreach (var field in rule.Fields)
                {
                    Field(field);
                }

                Bool(rule.Alternative is not null);
                if (rule.Alternative is not null)
                {
                    Field(rule.Alternative);
                }

                Text(rule.RecordPath);
                Bool(rule.IsDeclared);
                Bool(rule.IsImplicit);
            }
        }

        buffer[buffer.Length - 1] = ')';
        return buffer.ToString();

        void Text(string? text)
        {
            if (text is null)
            {
                buffer.Append("#f ");
            }
            else
            {
                var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
                buffer.Append('"').Append(encoded).Append("\" ");
            }
        }

        void Integer(int number) =>
            buffer.Append(number.ToString(CultureInfo.InvariantCulture)).Append(' ');

        void Bool(bool value) => Integer(value ? 1 : 0);

        void Strings(EquatableArray<string> strings)
        {
            Integer(strings.Count);
            foreach (string value in strings)
            {
                Text(value);
            }
        }

        void Field(LanguageModel.Field field)
        {
            Text(field.MetaVar);
            Text(field.Property);
            Text(field.Target);
            Bool(field.IsNonTerminal);
            Integer(field.SequenceDepth);
            Bool(field.IsMaybe);
        }
    }

    internal static LanguageModel? Decode(string payload)
    {
        if (!payload.StartsWith(Prefix, StringComparison.Ordinal)
            || !payload.EndsWith(")", StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            int length = payload.Length - Prefix.Length - 1;
            var reader = new Reader(payload.Substring(Prefix.Length, length));
            var model = new LanguageModel(
                reader.Text(),
                reader.Text(),
                reader.Text(),
                reader.Text(),
                reader.Array(reader.Text),
                reader.Text(),
                reader.Array(reader.Terminal),
                reader.Array(reader.NonTerminal));
            return reader.End ? model : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private sealed class Reader(string payload)
    {
        private readonly string[] tokens = payload.Split(' ');
        private int index;

        internal bool End => this.index == this.tokens.Length;

        internal string Text() =>
            this.NullableText() ?? throw new FormatException("Unexpected null metadata field.");

        internal LanguageModel.Terminal Terminal() => new(
            this.Text(),
            this.Text(),
            this.Array(this.Text),
            this.NullableText(),
            this.NullableText(),
            this.Text());

        internal LanguageModel.NonTerminal NonTerminal()
        {
            var name = this.Text();
            var metas = this.Array(this.Text);
            var declaration = (LanguageModel.Declaration)this.Integer();
            return new(name, metas, this.Array(this.Production), declaration);
        }

        internal EquatableArray<T> Array<T>(Func<T> read)
        {
            int count = this.Integer();
            if (count < 0 || count > this.tokens.Length - this.index)
            {
                throw new FormatException("Invalid array length in language metadata.");
            }

            return new(Enumerable.Range(0, count).Select(_ => read()));
        }

        private string Next() => this.End
            ? throw new FormatException("Incomplete language metadata.")
            : this.tokens[this.index++];

        private string? NullableText()
        {
            string value = this.Next();
            if (string.Equals(value, "#f", StringComparison.Ordinal))
            {
                return null;
            }

            if (value.Length < 2 || value[0] != '"' || value[value.Length - 1] != '"')
            {
                throw new FormatException("Invalid metadata string.");
            }

            var decoded = Convert.FromBase64String(value.Substring(1, value.Length - 2));
            return Encoding.UTF8.GetString(decoded);
        }

        private int Integer()
        {
            bool valid = int.TryParse(
                this.Next(), NumberStyles.None, CultureInfo.InvariantCulture, out int result);
            return valid ? result : throw new FormatException("Invalid metadata integer.");
        }

        private bool Bool() => this.Integer() switch
        {
            0 => false,
            1 => true,
            _ => throw new FormatException("Invalid metadata boolean."),
        };

        private LanguageModel.Field Field() =>
            new(this.Text(), this.Text(), this.Text(), this.Bool(), this.Integer(), this.Bool());

        private LanguageModel.Production Production() => new(
            this.Text(),
            this.Text(),
            this.NullableText(),
            this.Handler(),
            this.Array(this.Field),
            this.Bool() ? this.Field() : null,
            this.Text(),
            this.Bool(),
            this.Bool());

        private LanguageModel.Handler? Handler() =>
            this.NullableText() is { } name ? new(name, this.Text()) : null;
    }
}
