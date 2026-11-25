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
using System.Runtime.CompilerServices;
using System.Text;

namespace Hcoona.Nanopass;

internal static class SExprFormatter
{
    internal static string Format(SExpr value, bool debug)
    {
        StringBuilder output = new();
        Append(output, value, debug);
        return output.ToString();
    }

    private static void Append(StringBuilder output, SExpr value, bool debug)
    {
        RuntimeHelpers.EnsureSufficientExecutionStack();
        switch (value)
        {
            case SExpr.Nil:
                output.Append("()");
                break;
            case SExpr.Bool boolean:
                output.Append(boolean.Value ? "#t" : "#f");
                break;
            case SExpr.Int integer:
                output.Append(integer.Value.ToString(CultureInfo.InvariantCulture));
                break;
            case SExpr.Float floating:
                AppendFloat(output, floating.Value);
                break;
            case SExpr.Char character:
                AppendCharacter(output, character.Value);
                break;
            case SExpr.Str text:
                AppendString(output, text.Value);
                break;
            case SExpr.Sym symbol:
                AppendSymbol(output, symbol.Value.Name);
                break;
            case SExpr.Pair pair:
                AppendList(output, pair, debug);
                break;
            case SExpr.Vector vector:
                output.Append("#(");
                for (int i = 0; i < vector.Items.Length; i++)
                {
                    if (i != 0)
                    {
                        output.Append(' ');
                    }

                    Append(output, vector.Items[i], debug);
                }

                output.Append(')');
                break;
            case SExpr.Boxed boxed when debug:
                output.Append("#<boxed ").Append(boxed.Value.GetType().Name).Append('>');
                break;
            case SExpr.Boxed:
                throw new InvalidOperationException(
                    "Opaque CLR values cannot be formatted as S-expression text.");
        }
    }

    private static void AppendList(StringBuilder output, SExpr.Pair pair, bool debug)
    {
        output.Append('(');
        while (true)
        {
            Append(output, pair.Car, debug);
            if (pair.Cdr is not SExpr.Pair next)
            {
                if (pair.Cdr is not SExpr.Nil)
                {
                    output.Append(" . ");
                    Append(output, pair.Cdr, debug);
                }

                break;
            }

            output.Append(' ');
            pair = next;
        }

        output.Append(')');
    }

    private static void AppendFloat(StringBuilder output, double value)
    {
        string token = value switch
        {
            double.PositiveInfinity => "+inf.0",
            double.NegativeInfinity => "-inf.0",
            double.NaN => "+nan.0",
            _ => value.ToString("R", CultureInfo.InvariantCulture),
        };
        output.Append(token);
        if (token.AsSpan().IndexOfAny('.', 'E', 'e') < 0)
        {
            output.Append(".0");
        }
    }

    private static void AppendCharacter(StringBuilder output, char value)
    {
        output.Append("#\\");
        switch (value)
        {
            case ' ':
                output.Append("space");
                break;
            case '\n':
                output.Append("newline");
                break;
            case '\r':
                output.Append("return");
                break;
            case '\t':
                output.Append("tab");
                break;
            default:
                if (char.IsWhiteSpace(value) || char.IsControl(value) || char.IsSurrogate(value))
                {
                    output.Append('u');
                    AppendHex(output, value);
                }
                else
                {
                    output.Append(value);
                }

                break;
        }
    }

    private static void AppendSymbol(StringBuilder output, string name)
    {
        if (IsBareSymbol(name))
        {
            output.Append(name);
            return;
        }

        if (!name.Contains('|'))
        {
            output.Append('|').Append(name).Append('|');
            return;
        }

        for (int i = 0; i < name.Length; i++)
        {
            char ch = name[i];
            if (NeedsSymbolEscape(ch) || (i == 0 && ch == '#'))
            {
                output.Append('\\');
            }

            output.Append(ch);
        }
    }

    private static bool IsBareSymbol(string name)
    {
        if (name.Length == 0 || name is "." || name[0] == '#')
        {
            return false;
        }

        foreach (char ch in name)
        {
            if (NeedsSymbolEscape(ch))
            {
                return false;
            }
        }

        return SExprReader.ClassifyAtom(name) is SExpr.Sym;
    }

    private static bool NeedsSymbolEscape(char ch) =>
        ch is '|' or '\\' || SExprReader.IsDelimiter(ch) || char.IsControl(ch);

    private static void AppendString(StringBuilder output, string text)
    {
        output.Append('"');
        foreach (char ch in text)
        {
            switch (ch)
            {
                case '\\':
                    output.Append(@"\\");
                    break;
                case '\n':
                    output.Append(@"\n");
                    break;
                case '\r':
                    output.Append(@"\r");
                    break;
                case '\t':
                    output.Append(@"\t");
                    break;
                case '"':
                    output.Append("\\\"");
                    break;
                default:
                    if (char.IsControl(ch))
                    {
                        output.Append(@"\u");
                        AppendHex(output, ch);
                    }
                    else
                    {
                        output.Append(ch);
                    }

                    break;
            }
        }

        output.Append('"');
    }

    private static void AppendHex(StringBuilder output, char value) =>
        output.Append(((ushort)value).ToString("x4", CultureInfo.InvariantCulture));
}
