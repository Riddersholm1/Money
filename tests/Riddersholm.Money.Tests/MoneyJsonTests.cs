using System.Text.Json;
using Riddersholm.Money.Serialization;
using Xunit;

namespace Riddersholm.Money.Tests;

public sealed class MoneyJsonTests
{
    private static Money Dkk(decimal amount) => new(amount, Currency.DKK);

    [Fact]
    public void The_default_shape_needs_no_configuration()
    {
        // Money and Currency carry converter attributes, so this works out of the box.
        Assert.Equal("""{"amount":100.50,"currency":"DKK"}""", JsonSerializer.Serialize(Dkk(100.50m)));
    }

    [Fact]
    public void The_default_shape_round_trips()
    {
        Money original = Dkk(100.50m);
        string json = JsonSerializer.Serialize(original);

        Assert.Equal(original, JsonSerializer.Deserialize<Money>(json));
    }

    [Fact]
    public void Currency_serialises_as_its_iso_code()
    {
        Assert.Equal("\"DKK\"", JsonSerializer.Serialize(Currency.DKK));
        Assert.Equal(Currency.DKK, JsonSerializer.Deserialize<Currency>("\"DKK\""));
    }

    [Fact]
    public void Amounts_are_written_exactly()
    {
        // Round-tripping through JSON must not change an amount, even a non-canonical one.
        string json = JsonSerializer.Serialize(Dkk(100.005m));

        Assert.Equal("""{"amount":100.005,"currency":"DKK"}""", json);
        Assert.Equal(100.005m, JsonSerializer.Deserialize<Money>(json).Amount);
    }

    [Fact]
    public void A_string_amount_can_be_written_for_javascript_consumers()
    {
        // JSON numbers are doubles in JavaScript; quoting keeps large or precise amounts exact.
        JsonSerializerOptions options = new JsonSerializerOptions().AddMoney(MoneyJsonFormat.StringAmount);

        Assert.Equal(
            """{"amount":"100.50","currency":"DKK"}""",
            JsonSerializer.Serialize(Dkk(100.50m), options));
    }

    [Fact]
    public void The_compact_form_is_a_single_string()
    {
        JsonSerializerOptions options = new JsonSerializerOptions().AddMoney(MoneyJsonFormat.Compact);

        Assert.Equal("\"100.50 DKK\"", JsonSerializer.Serialize(Dkk(100.50m), options));
    }

    [Theory]
    [InlineData("""{"amount":100.50,"currency":"DKK"}""")]
    [InlineData("""{"amount":"100.50","currency":"DKK"}""")]
    [InlineData("""{"currency":"DKK","amount":100.50}""")]
    [InlineData("""{"Amount":100.50,"Currency":"DKK"}""")]
    [InlineData("\"100.50 DKK\"")]
    public void Every_written_form_is_readable_whatever_the_configuration(string json) =>
        Assert.Equal(Dkk(100.50m), JsonSerializer.Deserialize<Money>(json));

    [Fact]
    public void Changing_the_write_format_does_not_break_stored_documents()
    {
        foreach (MoneyJsonFormat format in Enum.GetValues<MoneyJsonFormat>())
        {
            JsonSerializerOptions writeOptions = new JsonSerializerOptions().AddMoney(format);
            string json = JsonSerializer.Serialize(Dkk(100.50m), writeOptions);

            foreach (MoneyJsonFormat readFormat in Enum.GetValues<MoneyJsonFormat>())
            {
                JsonSerializerOptions readOptions = new JsonSerializerOptions().AddMoney(readFormat);
                Assert.Equal(Dkk(100.50m), JsonSerializer.Deserialize<Money>(json, readOptions));
            }
        }
    }

    [Fact]
    public void Unknown_properties_are_ignored() =>
        Assert.Equal(
            Dkk(100.50m),
            JsonSerializer.Deserialize<Money>("""{"note":{"nested":true},"amount":100.50,"currency":"DKK"}"""));

    [Theory]
    [InlineData("""{"amount":100.50}""")]
    [InlineData("""{"currency":"DKK"}""")]
    [InlineData("""{"amount":100.50,"currency":"NOTACODE"}""")]
    [InlineData("""{"amount":"not a number","currency":"DKK"}""")]
    [InlineData("123")]
    public void Malformed_documents_are_rejected(string json) =>
        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize<Money>(json));

    [Fact]
    public void Null_is_refused_rather_than_read_as_a_zero_amount()
    {
        // This used to return default(Money) — zero in XXX. Turning an absent amount into a definite
        // zero is the one failure a money library must not have: nothing downstream can distinguish it
        // from an intentional zero, so a missing figure becomes a wrong total in silence.
        // BankingHardeningTests covers the surrounding behaviour, including that Money? still reads
        // null as null, which is how absence should be expressed.
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Money>("null"));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Currency>("null"));
    }

    [Fact]
    public void Default_money_round_trips_as_the_iso_no_currency_code()
    {
        string json = JsonSerializer.Serialize(default(Money));

        Assert.Equal("""{"amount":0,"currency":"XXX"}""", json);
        Assert.Equal(default(Money), JsonSerializer.Deserialize<Money>(json));
    }

    [Fact]
    public void Unknown_currencies_survive_a_round_trip()
    {
        // The reason Currency packs its code rather than an index into a table.
        Money original = new(100m, Currency.FromCode("QQQ"));
        Money restored = JsonSerializer.Deserialize<Money>(JsonSerializer.Serialize(original));

        Assert.Equal(original, restored);
        Assert.Equal("QQQ", restored.Currency.Code);
    }

    [Fact]
    public void Currencies_work_as_dictionary_keys()
    {
        Dictionary<Currency, Money> balances = new()
        {
            [Currency.DKK] = Dkk(100m),
            [Currency.EUR] = new Money(50m, Currency.EUR)
        };

        string json = JsonSerializer.Serialize(balances);

        Assert.Contains("\"DKK\":", json, StringComparison.Ordinal);
        Assert.Equal(balances, JsonSerializer.Deserialize<Dictionary<Currency, Money>>(json));
    }

    [Fact]
    public void Money_nested_in_a_dto_round_trips()
    {
        Invoice original = new("INV-1", Dkk(1250.75m));
        string json = JsonSerializer.Serialize(original);

        Assert.Equal("""{"Reference":"INV-1","Total":{"amount":1250.75,"currency":"DKK"}}""", json);
        Assert.Equal(original, JsonSerializer.Deserialize<Invoice>(json));
    }

    [Fact]
    public void The_source_generated_context_serialises_without_reflection()
    {
        // The path NativeAOT and full trimming take.
        string json = JsonSerializer.Serialize(Dkk(100.50m), MoneyJsonSerializerContext.Default.Money);

        Assert.Equal("""{"amount":100.50,"currency":"DKK"}""", json);
        Assert.Equal(Dkk(100.50m), JsonSerializer.Deserialize("""{"amount":100.50,"currency":"DKK"}""", MoneyJsonSerializerContext.Default.Money));
    }

    [Fact]
    public void Every_currency_round_trips_through_json()
    {
        foreach (Currency currency in Currency.Known)
        {
            Money original = new(1234.5m, currency);

            Assert.Equal(original, JsonSerializer.Deserialize<Money>(JsonSerializer.Serialize(original)));
        }
    }

    [Fact]
    public void Utf8_serialisation_avoids_a_string()
    {
        byte[] utf8 = JsonSerializer.SerializeToUtf8Bytes(Dkk(100.50m));

        Assert.Equal("""{"amount":100.50,"currency":"DKK"}""", System.Text.Encoding.UTF8.GetString(utf8));
        Assert.Equal(Dkk(100.50m), JsonSerializer.Deserialize<Money>(utf8));
    }

    private sealed record Invoice(string Reference, Money Total);
}
