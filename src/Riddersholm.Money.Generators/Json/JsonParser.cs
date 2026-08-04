using System.Globalization;
using System.Text;

namespace Riddersholm.Money.Generators.Json;

/// <summary>A small recursive-descent JSON reader. See <see cref="JsonValue"/> for why this exists.</summary>
internal static class JsonParser
{
    /// <summary>Parses <paramref name="text"/>, or returns <see langword="null"/> if it is not valid JSON.</summary>
    public static JsonValue? Parse(string text)
    {
        try
        {
            int index = 0;
            JsonValue value = ParseValue(text, ref index);
            SkipWhitespace(text, ref index);
            return index == text.Length ? value : null;
        }
        catch (FormatException)
        {
            return null;
        }
        catch (IndexOutOfRangeException)
        {
            return null;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static JsonValue ParseValue(string text, ref int index)
    {
        SkipWhitespace(text, ref index);

        if (index >= text.Length)
        {
            throw new FormatException("Unexpected end of input.");
        }

        return text[index] switch
        {
            '{' => ParseObject(text, ref index),
            '[' => ParseArray(text, ref index),
            '"' => JsonValue.From(ParseString(text, ref index)),
            't' => ParseLiteral(text, ref index, "true", JsonValue.From(true)),
            'f' => ParseLiteral(text, ref index, "false", JsonValue.From(false)),
            'n' => ParseLiteral(text, ref index, "null", JsonValue.Null),
            _ => ParseNumber(text, ref index)
        };
    }

    private static JsonValue ParseObject(string text, ref int index)
    {
        Dictionary<string, JsonValue> result = new(StringComparer.Ordinal);
        index++; // '{'

        SkipWhitespace(text, ref index);
        if (text[index] == '}')
        {
            index++;
            return JsonValue.From(result);
        }

        while (true)
        {
            SkipWhitespace(text, ref index);
            string key = ParseString(text, ref index);

            SkipWhitespace(text, ref index);
            Expect(text, ref index, ':');

            result[key] = ParseValue(text, ref index);

            SkipWhitespace(text, ref index);
            char c = text[index++];
            if (c == '}')
            {
                return JsonValue.From(result);
            }

            if (c != ',')
            {
                throw new FormatException($"Expected ',' or '}}' but found '{c}'.");
            }
        }
    }

    private static JsonValue ParseArray(string text, ref int index)
    {
        List<JsonValue> result = [];
        index++; // '['

        SkipWhitespace(text, ref index);
        if (text[index] == ']')
        {
            index++;
            return JsonValue.From(result);
        }

        while (true)
        {
            result.Add(ParseValue(text, ref index));

            SkipWhitespace(text, ref index);
            char c = text[index++];
            if (c == ']')
            {
                return JsonValue.From(result);
            }

            if (c != ',')
            {
                throw new FormatException($"Expected ',' or ']' but found '{c}'.");
            }
        }
    }

    private static string ParseString(string text, ref int index)
    {
        Expect(text, ref index, '"');
        StringBuilder builder = new();

        while (true)
        {
            char c = text[index++];

            if (c == '"')
            {
                return builder.ToString();
            }

            if (c != '\\')
            {
                builder.Append(c);
                continue;
            }

            char escape = text[index++];
            switch (escape)
            {
                case '"':
                    builder.Append('"');
                    break;
                case '\\':
                    builder.Append('\\');
                    break;
                case '/':
                    builder.Append('/');
                    break;
                case 'b':
                    builder.Append('\b');
                    break;
                case 'f':
                    builder.Append('\f');
                    break;
                case 'n':
                    builder.Append('\n');
                    break;
                case 'r':
                    builder.Append('\r');
                    break;
                case 't':
                    builder.Append('\t');
                    break;
                case 'u':
                    builder.Append((char)ushort.Parse(
                        text.Substring(index, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    index += 4;
                    break;
                default:
                    throw new FormatException($"Unrecognised escape '\\{escape}'.");
            }
        }
    }

    private static JsonValue ParseNumber(string text, ref int index)
    {
        int start = index;

        while (index < text.Length && (char.IsDigit(text[index]) || text[index] is '-' or '+' or '.' or 'e' or 'E'))
        {
            index++;
        }

        string literal = text.Substring(start, index - start);

        return double.TryParse(literal, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? JsonValue.From(value)
            : throw new FormatException($"'{literal}' is not a number.");
    }

    private static JsonValue ParseLiteral(string text, ref int index, string literal, JsonValue value)
    {
        if (index + literal.Length > text.Length ||
            string.CompareOrdinal(text, index, literal, 0, literal.Length) != 0)
        {
            throw new FormatException($"Expected '{literal}'.");
        }

        index += literal.Length;
        return value;
    }

    private static void Expect(string text, ref int index, char expected)
    {
        if (text[index] != expected)
        {
            throw new FormatException($"Expected '{expected}' but found '{text[index]}'.");
        }

        index++;
    }

    private static void SkipWhitespace(string text, ref int index)
    {
        while (index < text.Length && char.IsWhiteSpace(text[index]))
        {
            index++;
        }
    }
}
