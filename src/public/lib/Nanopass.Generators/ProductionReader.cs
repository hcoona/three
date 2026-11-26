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
using System.Globalization;
using System.Linq;
using System.Text;

namespace Hcoona.Nanopass.Generators;

internal static class ProductionReader
{
    internal static LanguageModel.Production Read(
        string text, IReadOnlyDictionary<string, LanguageModel.Field> metaVars)
    {
        var (syntax, pretty) = PatternDatum.Parse(text);
        if (syntax.IsSymbol)
        {
            var alternative = Resolve(syntax.Atom!, metaVars);
            if (pretty is not null)
            {
                ValidatePretty(pretty, new([alternative]));
            }

            return new(
                string.Empty,
                syntax.ToString(),
                pretty?.ToString(),
                null,
                default,
                alternative,
                string.Empty,
                false,
                false);
        }

        if (syntax.Atom is not null)
        {
            throw new FormatException("Expected a metavariable or a list production.");
        }

        string? tag = null;
        if (syntax.Items.Count > 0 && syntax.Items[0].Atom is { } atom)
        {
            if (!syntax.Items[0].IsSymbol || atom is "..." or "=>")
            {
                throw new FormatException(
                    "Expected a symbol keyword or metavariable at the start of the production.");
            }

            if (!metaVars.ContainsKey(BaseMetaVar(atom)))
            {
                tag = atom;
            }
        }

        var fields = new List<LanguageModel.Field>();
        ReadFields(syntax, 0, tag is null ? 0 : 1, metaVars, fields);
        var fieldNames = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < fields.Count; i++)
        {
            var field = fields[i];
            var baseName = Identifier(field.MetaVar, "Field")
                + (field.SequenceDepth > 0 ? "List" : string.Empty);
            var property = baseName;
            for (int suffix = 2; !fieldNames.Add(property); suffix++)
            {
                property = baseName + suffix.ToString(CultureInfo.InvariantCulture);
            }

            fields[i] = field with { Property = property };
        }

        var name = tag is not null
            ? Identifier(tag, "Form")
            : syntax.Items.Count == 0 ? "Empty" : syntax.Tail is not null ? "Pair" : "Implicit";
        var bound = new EquatableArray<LanguageModel.Field>(fields);
        if (pretty is not null)
        {
            ValidatePretty(pretty, bound);
        }

        return new(
            name,
            syntax.ToString(),
            pretty?.ToString(),
            null,
            bound,
            null,
            name,
            false,
            tag is null);
    }

    internal static string ReadPretty(string text, EquatableArray<LanguageModel.Field> fields)
    {
        var (template, extra) = PatternDatum.Parse(text);
        if (extra is not null)
        {
            throw new FormatException("Unexpected '=>' inside a pretty template.");
        }

        ValidatePretty(template, fields);
        return template.ToString();
    }

    internal static string BaseMetaVar(string value)
    {
        int end = value.Length;
        while (end > 0 && (value[end - 1] is (>= '0' and <= '9') or '*' or '?' or '^'))
        {
            end--;
        }

        return value.Substring(0, end);
    }

    internal static string Identifier(string value, string fallback)
    {
        var builder = new StringBuilder();
        bool upper = true;
        foreach (char character in value)
        {
            if (char.IsLetter(character) || ((builder.Length > 0) && char.IsDigit(character)))
            {
                builder.Append(upper ? char.ToUpperInvariant(character) : character);
                upper = false;
            }
            else
            {
                upper = true;
            }
        }

        return builder.Length == 0 ? fallback : builder.ToString();
    }

    private static LanguageModel.Field Resolve(
        string name, IReadOnlyDictionary<string, LanguageModel.Field> metaVars) =>
        metaVars.TryGetValue(BaseMetaVar(name), out var field)
            ? field with { MetaVar = name }
            : throw new FormatException($"Unrecognized metavariable '{name}'.");

    private static void ReadFields(
        PatternDatum pattern,
        int depth,
        int start,
        IReadOnlyDictionary<string, LanguageModel.Field> metaVars,
        List<LanguageModel.Field> fields)
    {
        if (pattern.Atom is not null)
        {
            AddField(pattern, depth, false, metaVars, fields);
            return;
        }

        if (start == 0 && pattern.Items.Count > 0 && pattern.Items[0].Atom == "maybe")
        {
            if (pattern is not { Items.Count: 2, Tail: null })
            {
                throw new FormatException("'maybe' expects exactly one metavariable.");
            }

            AddField(pattern.Items[1], depth, true, metaVars, fields);
            return;
        }

        var repeated = false;
        for (int i = start; i < pattern.Items.Count; i++)
        {
            PatternDatum item = pattern.Items[i];
            if (i + 1 < pattern.Items.Count && pattern.Items[i + 1].Atom == "...")
            {
                if (repeated)
                {
                    throw new FormatException("Only one ellipsis is allowed at each list level.");
                }

                int first = fields.Count;
                ReadFields(item, depth + 1, 0, metaVars, fields);
                if (first == fields.Count)
                {
                    throw new FormatException("An ellipsis must repeat at least one metavariable.");
                }

                repeated = true;
                i++;
            }
            else
            {
                ReadFields(item, depth, 0, metaVars, fields);
            }
        }

        if (pattern.Tail is not null)
        {
            ReadFields(pattern.Tail, depth, 0, metaVars, fields);
        }
    }

    private static void AddField(
        PatternDatum datum,
        int depth,
        bool maybe,
        IReadOnlyDictionary<string, LanguageModel.Field> metaVars,
        List<LanguageModel.Field> fields)
    {
        if (!datum.IsSymbol || datum.Atom is "..." or "." or "=>")
        {
            throw new FormatException(
                "Expected a metavariable reference or pattern in the production.");
        }

        LanguageModel.Field field = Resolve(datum.Atom!, metaVars);
        if (fields.Any(previous =>
            string.Equals(previous.MetaVar, field.MetaVar, StringComparison.Ordinal)))
        {
            throw new FormatException(
                $"Duplicate metavariable '{field.MetaVar}' in the production.");
        }

        fields.Add(field with { SequenceDepth = depth, IsMaybe = maybe });
    }

    private static void ValidatePretty(
        PatternDatum template, EquatableArray<LanguageModel.Field> fields)
    {
        var byName = fields.ToDictionary(static field => field.MetaVar, StringComparer.Ordinal);
        Visit(template, 0);

        void Visit(PatternDatum item, int depth)
        {
            if (item.Atom is { } atom)
            {
                if (atom == "..." || (atom == "." && !item.IsSymbol))
                {
                    throw new FormatException($"Unexpected '{atom}' in pretty template.");
                }

                if (byName.TryGetValue(atom, out var field) && field.SequenceDepth > depth)
                {
                    throw new FormatException($"Missing ellipsis for pretty field '{atom}'.");
                }

                return;
            }

            for (int i = 0; i < item.Items.Count; i++)
            {
                var child = item.Items[i];
                if (i + 1 < item.Items.Count && item.Items[i + 1].Atom == "...")
                {
                    if (!RepeatsField(child, depth))
                    {
                        throw new FormatException("A pretty ellipsis needs a sequence field.");
                    }

                    Visit(child, depth + 1);
                    i++;
                }
                else
                {
                    Visit(child, depth);
                }
            }

            if (item.Tail is not null)
            {
                Visit(item.Tail, depth);
            }
        }

        bool RepeatsField(PatternDatum item, int depth)
        {
            if (item.Atom is { } atom)
            {
                return byName.TryGetValue(atom, out var field) && field.SequenceDepth > depth;
            }

            return item.Items.Any(child => RepeatsField(child, depth))
                || (item.Tail is not null && RepeatsField(item.Tail, depth));
        }
    }
}
