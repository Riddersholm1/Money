namespace Riddersholm.Money.Tests;

/// <summary>
/// Shorthand for the amounts and currencies that appear all over this suite.
/// </summary>
/// <remarks>
/// DKK is the default subject because it is the ordinary case — two decimals, a hundredth-of-a-major
/// minor unit — so a test that does not care about the currency does not have to say which one it means.
/// Tests that <em>are</em> about a currency (JPY's zero digits, KWD's three) name it explicitly, and
/// should keep doing so.
/// </remarks>
internal static class TestMoney
{
    /// <summary>
    /// A registered currency divided into <em>fifths</em>: two decimal places, five minor units per major,
    /// and cash rounding to 0.20.
    /// </summary>
    /// <remarks>
    /// This is the shape MRU and MGA physically have. The shipped table follows ISO 4217, which counts both
    /// in hundredths, so no ISO currency exercises a minor unit that is not a power of ten any more — but a
    /// consumer can still register one, and the exact-arithmetic rounding and allocation paths exist for
    /// it. Tests that need such a currency use this one.
    /// </remarks>
    public static Currency Fifths { get; } = RegisterFifths();

    /// <summary>An amount in Danish kroner.</summary>
    public static Money Dkk(decimal amount) => new(amount, Currency.DKK);

    private static Currency RegisterFifths()
    {
        // Registering identical metadata again is a no-op, so tests running in parallel are safe.
        CurrencyRegistry.Register(new CurrencyInfo(
            code: "QFI",
            numericCode: 0,
            englishName: "Fifth-unit test currency",
            symbol: "QFI",
            decimalDigits: 2,
            minorUnitsPerMajor: 5,
            cashDecimalDigits: 2,
            cashRoundingStep: 20));

        return Currency.FromCode("QFI");
    }
}
