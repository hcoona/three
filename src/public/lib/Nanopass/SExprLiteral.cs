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
using System.Text;

namespace Hcoona.Nanopass;

internal static class SExprLiteral
{
    private const NumberStyles FloatStyle = NumberStyles.AllowLeadingSign
        | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent;

    internal static bool IsDelimiter(char ch) =>
        char.IsWhiteSpace(ch) || ch is '(' or ')' or '[' or ']' or '{' or '}'
            or '"' or '\'' or '`' or ',' or ';';

    internal static bool? ReadBoolean(ReadOnlySpan<char> token) => token switch
    {
        "#t" or "#true" => true,
        "#f" or "#false" => false,
        _ => null,
    };

    internal static double? ReadSpecialFloat(ReadOnlySpan<char> token) => token switch
    {
        "+inf.0" => double.PositiveInfinity,
        "-inf.0" => double.NegativeInfinity,
        "+nan.0" or "-nan.0" => double.NaN,
        _ => null,
    };

    internal static bool StartsLikeNumber(ReadOnlySpan<char> token)
    {
        if (!token.IsEmpty && token[0] is '+' or '-')
        {
            token = token.Slice(1);
        }

        return !token.IsEmpty && (IsDigit(token[0])
            || (token[0] == '.' && token.Length > 1 && IsDigit(token[1])));
    }

    internal static bool IsFloat(ReadOnlySpan<char> token) =>
        token.IndexOfAny('.', 'e', 'E') >= 0;

    internal static bool TryReadFloat(ReadOnlySpan<char> token, out double value)
    {
        value = default;
        return !HasTrailingNull(token)
            && double.TryParse(
                ParseInput(token), FloatStyle, CultureInfo.InvariantCulture, out value);
    }

    internal static bool TryReadInteger(ReadOnlySpan<char> token, out long value)
    {
        value = default;
        return !HasTrailingNull(token) && long.TryParse(
            ParseInput(token),
            NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,
            out value);
    }

    internal static char ReadCharacter(
        string text, ref int position, Func<string, int, Exception> error)
    {
        if (position == text.Length)
        {
            throw error("Expected a character", position);
        }

        int start = position++;
        if (IsDelimiter(text[start]))
        {
            return text[start];
        }

        while (position < text.Length && !IsDelimiter(text[position]))
        {
            position++;
        }

        ReadOnlySpan<char> token = text.AsSpan(start, position - start);
        return token switch
        {
            "space" => ' ',
            "newline" => '\n',
            "tab" => '\t',
            "return" => '\r',
            _ when token.Length == 1 => token[0],
            _ when token.Length == 5 && token[0] == 'u'
                && TryReadHex(token.Slice(1), out ushort code) => (char)code,
            _ => throw error("Unknown character literal", start),
        };
    }

    internal static string ReadString(
        string text, ref int position, Func<string, int, Exception> error)
    {
        position++;
        StringBuilder value = new();
        while (position < text.Length)
        {
            char ch = text[position++];
            if (ch == '"')
            {
                return value.ToString();
            }

            if (ch == '\\')
            {
                if (position == text.Length)
                {
                    break;
                }

                ch = text[position++] switch
                {
                    '"' => '"',
                    '\\' => '\\',
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    'u' => ReadHexEscape(text, ref position, error),
                    _ => throw error("Unknown escape sequence", position),
                };
            }

            value.Append(ch);
        }

        throw error("Unterminated string", position);
    }

    private static bool IsDigit(char token) => token is >= '0' and <= '9';

    // The numeric parsers accept trailing NULs, but they are not part of a number token.
    private static bool HasTrailingNull(ReadOnlySpan<char> token) =>
        !token.IsEmpty && token[token.Length - 1] == '\0';

    private static bool TryReadHex(ReadOnlySpan<char> token, out ushort code) =>
        ushort.TryParse(
            ParseInput(token),
            NumberStyles.AllowHexSpecifier,
            CultureInfo.InvariantCulture,
            out code);

    private static char ReadHexEscape(
        string text, ref int position, Func<string, int, Exception> error)
    {
        if (text.Length - position < 4 || !TryReadHex(text.AsSpan(position, 4), out ushort code))
        {
            throw error("Expected four hexadecimal digits", position);
        }

        position += 4;
        return (char)code;
    }

#if NETSTANDARD2_0
    private static string ParseInput(ReadOnlySpan<char> token) => token.ToString();
#else
    private static ReadOnlySpan<char> ParseInput(ReadOnlySpan<char> token) => token;
#endif
}
