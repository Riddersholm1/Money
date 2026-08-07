namespace Riddersholm.Money.Tests;

/// <summary>
/// Shorthand for the amounts that appear all over this suite.
/// </summary>
/// <remarks>
/// DKK is the default subject because it is the ordinary case — two decimals, a hundredth-of-a-major
/// minor unit — so a test that does not care about the currency does not have to say which one it means.
/// Tests that <em>are</em> about a currency (JPY's zero digits, KWD's three, MRU's fifths) name it
/// explicitly, and should keep doing so.
/// </remarks>
internal static class TestMoney
{
    /// <summary>An amount in Danish kroner.</summary>
    public static Money Dkk(decimal amount) => new(amount, Currency.DKK);
}
