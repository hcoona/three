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
#if !NETSTANDARD2_0
using System.Buffers;
#endif
using System.Globalization;
using System.Text;

namespace Hcoona.Nanopass;

internal static class SExprLiteral
{
    private const NumberStyles FloatStyle = NumberStyles.AllowLeadingSign
        | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent;

#if !NETSTANDARD2_0
    // Include all characters accepted by char.IsWhiteSpace.
    private const string Delimiters =
        " \t\n\v\f\r\u0085\u00A0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006"
        + "\u2007\u2008\u2009\u200A\u2028\u2029\u202F\u205F\u3000()[]{}\"'`,;";

    private static readonly SearchValues<char> DelimiterValues = SearchValues.Create(Delimiters);
    private static readonly SearchValues<char> AtomBoundaries =
        SearchValues.Create(Delimiters + "\\|");
#endif

    internal static bool IsDelimiter(char ch) =>
        char.IsWhiteSpace(ch) || ch is '(' or ')' or '[' or ']' or '{' or '}'
            or '"' or '\'' or '`' or ',' or ';';

    internal static int TokenLength(ReadOnlySpan<char> text, bool stopAtEscape = false)
    {
#if NETSTANDARD2_0
        int i = 0;
        while (i < text.Length && !IsDelimiter(text[i])
            && !(stopAtEscape && text[i] is '\\' or '|'))
        {
            i++;
        }

        return i;
#else
        int end = text.IndexOfAny(stopAtEscape ? AtomBoundaries : DelimiterValues);
        return end < 0 ? text.Length : end;
#endif
    }

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
                ParseInput(token), FloatStyle, CultureInfo.InvariantCulture, out value)
            && !double.IsInfinity(value);
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

        position += TokenLength(text.AsSpan(position));

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
        int start = ++position;
        var remainder = text.AsSpan(start);
        int boundary = remainder.IndexOfAny('"', '\\');
        if (boundary < 0)
        {
            position = text.Length;
            throw error("Unterminated string", position);
        }

        position += boundary;
        if (text[position] == '"')
        {
            position++;
            return text.Substring(start, boundary);
        }

        var value = new StringBuilder().Append(text, start, boundary);
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
