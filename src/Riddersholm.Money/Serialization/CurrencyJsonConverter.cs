using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Riddersholm.Money.Serialization;

/// <summary>
/// Reads and writes <see cref="Currency"/> as its ISO 4217 alphabetic code.
/// </summary>
/// <remarks>
/// Applied automatically: <see cref="Currency"/> carries a <see cref="JsonConverterAttribute"/>, so no
/// registration is needed. Currencies also work as dictionary keys.
/// </remarks>
public sealed class CurrencyJsonConverter : JsonConverter<Currency>
{
    /// <inheritdoc />
    public override Currency Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            // Not Currency.None. XXX is a currency someone can write explicitly, so silently reading
            // null as XXX would make "no value supplied" and "explicitly no currency" the same thing.
            // Declare the property as Currency? when it is genuinely optional.
            throw new JsonException(
                "Cannot read null as Currency. Write \"XXX\" for no currency, or use Currency? if the value is optional.");
        }

        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"Expected a currency code string but found {reader.TokenType}.");
        }

        return ReadCode(ref reader);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, Currency value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        Span<byte> code = stackalloc byte[3];
        CurrencyCodec.UnpackUtf8(value.PackedValue, code);
        writer.WriteStringValue(code);
    }

    /// <inheritdoc />
    public override Currency ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        ReadCode(ref reader);

    /// <inheritdoc />
    public override void WriteAsPropertyName(Utf8JsonWriter writer, Currency value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        Span<byte> code = stackalloc byte[3];
        CurrencyCodec.UnpackUtf8(value.PackedValue, code);
        writer.WritePropertyName(code);
    }

    private static Currency ReadCode(ref Utf8JsonReader reader)
    {
        // The common case is three unescaped ASCII bytes, which never touches a string.
        if (!reader.HasValueSequence && !reader.ValueIsEscaped)
        {
            if (Currency.TryParse(reader.ValueSpan, CultureInfo.InvariantCulture, out Currency currency))
            {
                return currency;
            }

            throw new JsonException(
                $"'{System.Text.Encoding.UTF8.GetString(reader.ValueSpan)}' is not a valid ISO 4217 alphabetic code.");
        }

        string? text = reader.GetString();

        return Currency.TryParse(text, CultureInfo.InvariantCulture, out Currency parsed)
            ? parsed
            : throw new JsonException($"'{text}' is not a valid ISO 4217 alphabetic code.");
    }
}
