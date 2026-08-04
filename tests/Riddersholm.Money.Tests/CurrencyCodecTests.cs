using Xunit;

namespace Riddersholm.Money.Tests;

/// <summary>
/// Guards the packed representation. These tests protect the two properties the whole design rests
/// on: any well-formed code round-trips exactly, and <c>XXX</c> occupies the zero slot.
/// </summary>
public sealed class CurrencyCodecTests
{
    [Fact]
    public void Default_none_and_xxx_are_the_same_value()
    {
        Assert.Equal(Currency.XXX, default(Currency));
        Assert.Equal(Currency.XXX, Currency.None);
        Assert.Equal(Currency.XXX, Currency.FromCode("XXX"));
        Assert.True(default(Currency).IsNone);
    }

    [Fact]
    public void Default_currency_reports_the_iso_no_currency_code()
    {
        // Serialising default(Money) must produce a real ISO code, not an empty string.
        Assert.Equal("XXX", default(Currency).Code);
        Assert.Equal("XXX", default(Currency).ToString());
    }

    [Theory]
    [InlineData("DKK")]
    [InlineData("USD")]
    [InlineData("XXX")]
    [InlineData("AAA")]
    [InlineData("ZZZ")]
    public void Codes_round_trip_exactly(string code) =>
        Assert.Equal(code, Currency.FromCode(code).Code);

    [Fact]
    public void Every_known_currency_round_trips_through_its_code()
    {
        foreach (Currency currency in Currency.Known)
        {
            Assert.Equal(currency, Currency.FromCode(currency.Code));
        }
    }

    [Fact]
    public void Unknown_but_well_formed_codes_round_trip()
    {
        // The point of packing the code rather than an index: a currency loaded from a database
        // survives intact even when this library has never heard of it.
        var unknown = Currency.FromCode("QQQ");

        Assert.False(unknown.IsKnown);
        Assert.Equal("QQQ", unknown.Code);
        Assert.Equal(unknown, Currency.FromCode(unknown.Code));
    }

    [Fact]
    public void All_three_letter_combinations_round_trip()
    {
        Span<char> buffer = stackalloc char[3];

        for (char a = 'A'; a <= 'Z'; a++)
        {
            for (char b = 'A'; b <= 'Z'; b++)
            {
                for (char c = 'A'; c <= 'Z'; c++)
                {
                    buffer[0] = a;
                    buffer[1] = b;
                    buffer[2] = c;

                    var currency = Currency.FromCode(buffer);
                    Assert.Equal(new string(buffer), currency.Code);
                }
            }
        }
    }

    [Fact]
    public void Packed_values_are_unique_across_all_combinations()
    {
        HashSet<Currency> seen = [];
        Span<char> buffer = stackalloc char[3];

        for (char a = 'A'; a <= 'Z'; a++)
        {
            for (char b = 'A'; b <= 'Z'; b++)
            {
                for (char c = 'A'; c <= 'Z'; c++)
                {
                    buffer[0] = a;
                    buffer[1] = b;
                    buffer[2] = c;
                    Assert.True(seen.Add(Currency.FromCode(buffer)), $"Collision on {new string(buffer)}.");
                }
            }
        }

        Assert.Equal(26 * 26 * 26, seen.Count);
    }

    [Theory]
    [InlineData("dkk")]
    [InlineData("Dkk")]
    [InlineData("dKK")]
    public void Parsing_accepts_any_case_and_normalises_to_upper(string code)
    {
        var currency = Currency.FromCode(code);

        Assert.Equal(Currency.DKK, currency);
        Assert.Equal("DKK", currency.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("DK")]
    [InlineData("DKKK")]
    [InlineData("DK1")]
    [InlineData("DK ")]
    [InlineData("Æ98")]
    public void Malformed_codes_are_rejected(string code)
    {
        Assert.False(Currency.TryFromCode(code, out _));
        Assert.Throws<ArgumentException>(() => Currency.FromCode(code));
    }

    [Fact]
    public void Generated_constants_agree_with_the_runtime_codec()
    {
        // The generator carries its own copy of the packing algorithm because a Roslyn component
        // cannot reference this assembly. This is the test that stops the two from drifting apart.
        foreach (Currency currency in Currency.Known)
        {
            var viaRuntime = Currency.FromCode(currency.Code);

            Assert.Equal(currency, viaRuntime);
            Assert.Equal(currency.GetHashCode(), viaRuntime.GetHashCode());
        }
    }
}
