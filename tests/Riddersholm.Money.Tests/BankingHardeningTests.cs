using System.Globalization;
using System.Text.Json;
using Xunit;

namespace Riddersholm.Money.Tests;

/// <summary>
/// Regression tests for the banking-readiness review: every case where the library used to answer
/// quietly and wrongly instead of loudly and not at all.
/// </summary>
/// <remarks>
/// The theme is that silence is the enemy. A library used inside a bank may crash, refuse, or throw —
/// all of those get noticed. What it must never do is return a plausible number that is wrong, because
/// nothing downstream can tell that apart from a right one.
/// </remarks>
public sealed class BankingHardeningTests
{
    // ---- B2: default(ExchangeRate) ------------------------------------------------------------

    [Fact]
    public void An_unspecified_exchange_rate_refuses_to_convert_rather_than_returning_zero()
    {
        // Rate defaults to 0, which the constructor forbids, so Convert used to multiply a million
        // kroner by zero and hand back "0 XXX" without a word.
        ExchangeRate unspecified = default;

        Assert.False(unspecified.IsSpecified);
        Assert.Throws<InvalidOperationException>(() => unspecified.Convert(new Money(1_000_000m, Currency.DKK)));
        Assert.Throws<InvalidOperationException>(() => unspecified.ConvertBack(new Money(1_000_000m, Currency.EUR)));
        Assert.Throws<InvalidOperationException>(() => unspecified.Invert());
    }

    [Fact]
    public void An_unspecified_exchange_rate_is_reachable_the_ordinary_ways()
    {
        // Not a contrived value: this is what an array of them contains before anything is assigned.
        var rates = new ExchangeRate[3];

        Assert.All(rates, rate => Assert.False(rate.IsSpecified));
    }

    [Fact]
    public void A_constructed_exchange_rate_is_specified_and_converts()
    {
        ExchangeRate rate = new(Currency.DKK, Currency.EUR, 0.134m);

        Assert.True(rate.IsSpecified);
        Assert.Equal(new Money(13.4m, Currency.EUR), rate.Convert(new Money(100m, Currency.DKK)));
        Assert.Equal(new Money(100m, Currency.DKK), rate.ConvertBack(new Money(13.4m, Currency.EUR)));
    }

    [Fact]
    public void A_currency_cannot_trade_against_itself_at_anything_but_one()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(
            () => new ExchangeRate(Currency.DKK, Currency.DKK, 1.05m));

        Assert.Equal("rate", error.ParamName);

        // The identity rate is legitimate and stays allowed — a generic conversion routine handed a
        // same-currency pair should not have to special-case it.
        ExchangeRate identity = new(Currency.DKK, Currency.DKK, 1m);

        Assert.Equal(new Money(100m, Currency.DKK), identity.Convert(new Money(100m, Currency.DKK)));
    }

    // ---- B3: JSON null ------------------------------------------------------------------------

    [Fact]
    public void Null_is_not_read_as_a_zero_amount()
    {
        // Previously this returned default(Money) — 0 XXX. A missing amount in a payment message is
        // not a zero amount, and the two must not be the same value.
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Money>("null"));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Currency>("null"));
    }

    [Fact]
    public void A_null_currency_property_inside_the_object_form_is_also_refused()
    {
        Assert.Throws<JsonException>(
            () => JsonSerializer.Deserialize<Money>("""{"amount":100.50,"currency":null}"""));
    }

    [Fact]
    public void Optional_money_still_round_trips_null()
    {
        // The supported way to say "no amount". This is what the refusal above pushes callers towards,
        // so it has to keep working.
        Assert.Null(JsonSerializer.Deserialize<Money?>("null"));
        Assert.Equal("null", JsonSerializer.Serialize<Money?>(null));

        Money? present = JsonSerializer.Deserialize<Money?>("""{"amount":100.50,"currency":"DKK"}""");

        Assert.Equal(new Money(100.50m, Currency.DKK), present);

        Assert.Null(JsonSerializer.Deserialize<Currency?>("null"));
        Assert.Equal(Currency.DKK, JsonSerializer.Deserialize<Currency?>("\"DKK\""));
    }

    [Fact]
    public void Explicit_XXX_is_still_readable_because_it_says_what_it_means()
    {
        // Refusing null must not refuse the currency that legitimately means "none".
        Money none = JsonSerializer.Deserialize<Money>("""{"amount":0,"currency":"XXX"}""");

        Assert.Equal(Currency.XXX, none.Currency);
        Assert.Equal(Currency.None, none.Currency);
    }

    // ---- B5: requiring a currency that exists -------------------------------------------------

    [Fact]
    public void An_unrecognised_code_parses_by_default_and_is_refused_on_request()
    {
        const string Text = "100.00 ZZZ";

        // The default stays permissive: an ISO code this build has not heard of must round-trip, or
        // storing and reloading a row would lose data.
        Assert.True(Money.TryParse(Text, MoneyStyles.Currency, CultureInfo.InvariantCulture, out Money loose));
        Assert.Equal("ZZZ", loose.Currency.Code);
        Assert.False(loose.Currency.IsKnown);

        // At a trust boundary the opposite is wanted.
        Assert.False(Money.TryParse(
            Text,
            MoneyStyles.Currency | MoneyStyles.RequireKnownCurrency,
            CultureInfo.InvariantCulture,
            out _));
    }

    [Fact]
    public void Requiring_a_known_currency_still_accepts_real_ones()
    {
        foreach (Currency currency in Currency.Known)
        {
            string text = new Money(100.50m, currency).ToString("R", CultureInfo.InvariantCulture);

            Assert.True(
                Money.TryParse(
                    text,
                    MoneyStyles.Currency | MoneyStyles.RequireKnownCurrency,
                    CultureInfo.InvariantCulture,
                    out Money parsed),
                $"'{text}' was refused despite {currency.Code} being a known currency.");

            Assert.Equal(currency, parsed.Currency);
        }
    }

    [Fact]
    public void FromKnownCode_accepts_only_currencies_the_library_has_metadata_for()
    {
        Assert.Equal(Currency.DKK, Currency.FromKnownCode("DKK"));
        Assert.Equal(Currency.DKK, Currency.FromKnownCode("dkk"));

        Assert.Throws<ArgumentException>(() => Currency.FromKnownCode("ZZZ"));
        Assert.Throws<ArgumentException>(() => Currency.FromKnownCode("D1"));
        Assert.Throws<ArgumentNullException>(() => Currency.FromKnownCode(null!));

        Assert.False(Currency.TryFromKnownCode("ZZZ", out Currency none));
        Assert.Equal(Currency.None, none);

        Assert.True(Currency.TryFromKnownCode("EUR", out Currency eur));
        Assert.Equal(Currency.EUR, eur);
    }

    [Fact]
    public void FromCode_stays_permissive_so_unknown_codes_still_round_trip()
    {
        // The whole reason the strict variant is opt-in.
        var unknown = Currency.FromCode("ZZZ");

        Assert.Equal("ZZZ", unknown.Code);
        Assert.False(unknown.IsKnown);
    }

    // ---- B6: the additive identity's deliberate asymmetry -------------------------------------

    [Fact]
    public void The_additive_identity_folds_into_addition_but_is_not_ordered_against_a_currency()
    {
        Money price = new(100m, Currency.DKK);

        // Addition accepts it, which is what makes Sum() work without a currency-specific seed.
        Assert.Equal(price, default(Money) + price);
        Assert.Equal(price, price + default(Money));
        Assert.Equal(price, new[] { price }.Sum());

        // Comparison refuses it, and that is not an oversight. CompareTo orders by currency code, so
        // default(Money) — XXX — sorts after every DKK amount. If '<' compared by amount instead, the
        // operator and the sort order would contradict each other, and a SortedSet or a binary search
        // built on the two would disagree about where a value belongs.
        Assert.Throws<CurrencyMismatchException>(() => default(Money) < price);
        Assert.Throws<CurrencyMismatchException>(() => Money.Min(default(Money), price));

        // The two views, shown agreeing with themselves.
        Assert.True(default(Money).CompareTo(price) > 0);
        Assert.Equal(
            ["DKK", "XXX"],
            new List<Money> { default, price }.Order().Select(m => m.Currency.Code));
    }

    [Fact]
    public void Summing_an_empty_sequence_gives_the_identity_rather_than_throwing()
    {
        Assert.Equal(Money.AdditiveIdentity, Array.Empty<Money>().Sum());
        Assert.Equal(default(Money), Array.Empty<Money>().Sum());
    }
}
