using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Riddersholm.Money.Serialization;

/// <summary>
/// Reads and writes <see cref="Money"/> as <c>{"amount":100.50,"currency":"DKK"}</c>.
/// </summary>
/// <remarks>
/// <para>
/// Applied automatically: <see cref="Money"/> carries a <see cref="JsonConverterAttribute"/>, so the
/// default shape needs no registration. To choose a different write format, add a configured instance
/// to <see cref="JsonSerializerOptions.Converters"/> — or call
/// <see cref="MoneyJsonExtensions.AddMoney(JsonSerializerOptions, MoneyJsonFormat)"/> — which takes
/// precedence over the attribute.
/// </para>
/// <para>
/// Reading accepts every format this converter can write, plus a string amount inside the object form,
/// so a document written under one setting still deserialises under another.
/// </para>
/// <para>
/// Amounts are written exactly. A non-canonical <c>100.005 DKK</c> serialises with all three decimals
/// rather than being quietly rounded to the currency's two — round-tripping through JSON must not
/// change an amount.
/// </para>
/// </remarks>
public sealed class MoneyJsonConverter : JsonConverter<Money>
{
    private static readonly JsonEncodedText AmountName = JsonEncodedText.Encode("amount");
    private static readonly JsonEncodedText CurrencyName = JsonEncodedText.Encode("currency");

    /// <summary>Creates a converter that writes the default object form.</summary>
    public MoneyJsonConverter()
        : this(MoneyJsonFormat.NumericAmount)
    {
    }

    /// <summary>Creates a converter that writes the given form.</summary>
    /// <param name="format">How to write amounts. Reading always accepts all supported forms.</param>
    public MoneyJsonConverter(MoneyJsonFormat format) => Format = format;

    /// <summary>How this converter writes amounts.</summary>
    public MoneyJsonFormat Format { get; }

    /// <inheritdoc />
    public override Money Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                // Deliberately an error rather than default(Money). An absent amount is not zero, and
                // turning one into the other is the worst failure a money library can have: it is
                // indistinguishable from an intentional zero and every downstream total is quietly
                // wrong. Declare the property as Money? when absence is a legitimate value — that
                // deserialises null as null, which says what it means.
                throw new JsonException(
                    "Cannot read null as Money. Use Money? if the value is genuinely optional.");

            case JsonTokenType.String:
                return ReadCompact(ref reader);

            case JsonTokenType.StartObject:
                return ReadObject(ref reader);

            default:
                throw new JsonException($"Expected an object or string for Money but found {reader.TokenType}.");
        }
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, Money value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        if (Format == MoneyJsonFormat.Compact)
        {
            Span<byte> buffer = stackalloc byte[64];

            if (value.TryFormat(buffer, out int written, "R", CultureInfo.InvariantCulture))
            {
                writer.WriteStringValue(buffer[..written]);
            }
            else
            {
                writer.WriteStringValue(value.ToString("R", CultureInfo.InvariantCulture));
            }

            return;
        }

        writer.WriteStartObject();

        if (Format == MoneyJsonFormat.StringAmount)
        {
            writer.WriteString(AmountName, value.Amount.ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            writer.WriteNumber(AmountName, value.Amount);
        }

        Span<byte> code = stackalloc byte[3];
        CurrencyCodec.UnpackUtf8(value.Currency.PackedValue, code);
        writer.WriteString(CurrencyName, code);

        writer.WriteEndObject();
    }

    /// <inheritdoc />
    public override Money ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        ReadCompact(ref reader);

    /// <inheritdoc />
    public override void WriteAsPropertyName(Utf8JsonWriter writer, Money value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WritePropertyName(value.ToString("R", CultureInfo.InvariantCulture));
    }

    private static Money ReadCompact(ref Utf8JsonReader reader)
    {
        string? text = reader.GetString();

        return Money.TryParse(text, CultureInfo.InvariantCulture, out Money value)
            ? value
            : throw new JsonException($"'{text}' is not a recognisable amount of money.");
    }

    private static Money ReadObject(ref Utf8JsonReader reader)
    {
        decimal? amount = null;
        Currency currency = Currency.None;
        bool currencySeen = false;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return amount is null
                    ? throw new JsonException("Money requires an 'amount' property.")
                    : !currencySeen
                        ? throw new JsonException("Money requires a 'currency' property.")
                        : new Money(amount.Value, currency);
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException($"Unexpected token {reader.TokenType} while reading Money.");
            }

            // Property names are matched case-insensitively so that documents from camelCase and
            // PascalCase producers both read, whatever the options say.
            if (reader.ValueTextEquals(AmountName.EncodedUtf8Bytes) || reader.ValueTextEquals("Amount"u8))
            {
                reader.Read();
                amount = ReadAmount(ref reader);
            }
            else if (reader.ValueTextEquals(CurrencyName.EncodedUtf8Bytes) || reader.ValueTextEquals("Currency"u8))
            {
                reader.Read();
                currency = ReadCurrency(ref reader);
                currencySeen = true;
            }
            else
            {
                reader.Read();
                reader.Skip();
            }
        }

        throw new JsonException("Unexpected end of JSON while reading Money.");
    }

    private static decimal ReadAmount(ref Utf8JsonReader reader) => reader.TokenType switch
    {
        JsonTokenType.Number => reader.GetDecimal(),
        // Accepted whichever format was configured, so a quoted amount never fails to load.
        JsonTokenType.String => decimal.TryParse(
            reader.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsed)
                ? parsed
                : throw new JsonException("The 'amount' property is not a number."),
        _ => throw new JsonException($"Expected a number for 'amount' but found {reader.TokenType}."),
    };

    private static Currency ReadCurrency(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            // "currency": null would otherwise become XXX, silently relabelling a real amount as
            // having no currency. If the document means XXX, it can say "XXX".
            throw new JsonException(
                "The 'currency' property is null. Write the ISO 4217 code, or \"XXX\" for no currency.");
        }

        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"Expected a currency code string but found {reader.TokenType}.");
        }

        string? code = reader.GetString();

        return Currency.TryParse(code, CultureInfo.InvariantCulture, out Currency currency)
            ? currency
            : throw new JsonException($"'{code}' is not a valid ISO 4217 alphabetic code.");
    }
}
