using System.Globalization;

namespace Riddersholm.Money;

/// <summary>
/// Which textual forms <see cref="Money.Parse(string, IFormatProvider?)"/> will accept.
/// </summary>
/// <remarks>
/// Mirrors <see cref="NumberStyles"/> in spirit, with the currency-specific choices made explicit at
/// the call site rather than buried in a parser's assumptions.
/// </remarks>
[Flags]
public enum MoneyStyles
{
    /// <summary>Accept nothing beyond bare digits.</summary>
    None = 0,

    /// <summary>Ignore whitespace before the value.</summary>
    AllowLeadingWhite = 1 << 0,

    /// <summary>Ignore whitespace after the value.</summary>
    AllowTrailingWhite = 1 << 1,

    /// <summary>Accept a sign before the number, as in <c>-100 DKK</c>.</summary>
    AllowLeadingSign = 1 << 2,

    /// <summary>Accept a sign after the number, as in <c>100- DKK</c>.</summary>
    AllowTrailingSign = 1 << 3,

    /// <summary>Accept parentheses as a negative marker, as in <c>(100) DKK</c>.</summary>
    AllowParentheses = 1 << 4,

    /// <summary>Accept group separators, as in <c>1,234.50</c>.</summary>
    AllowThousands = 1 << 5,

    /// <summary>Accept a fractional part.</summary>
    AllowDecimalPoint = 1 << 6,

    /// <summary>
    /// Accept an ISO 4217 alphabetic code at either end, as in <c>100 DKK</c> or <c>DKK 100</c>.
    /// </summary>
    AllowCurrencyCode = 1 << 7,

    /// <summary>
    /// Accept a currency symbol, as in <c>kr. 100,50</c>.
    /// </summary>
    /// <remarks>
    /// Only the symbol belonging to the supplied culture is accepted, and only when a culture is
    /// supplied. Symbols are not unique — <c>kr</c> is DKK, NOK, SEK, and ISK, and <c>$</c> covers a
    /// dozen currencies — so resolving one without knowing the culture it was written in is guesswork.
    /// </remarks>
    AllowCurrencySymbol = 1 << 8,

    /// <summary>
    /// Fail when no currency can be identified, rather than yielding <see cref="Currency.None"/>.
    /// </summary>
    RequireCurrency = 1 << 9,

    /// <summary>
    /// Fail unless the currency is one the library has metadata for — see <see cref="Currency.IsKnown"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// By default any three ASCII letters parse, so <c>100.00 ZZZ</c> succeeds and yields a currency
    /// whose precision is a documented guess. That is deliberate: an ISO code must round-trip
    /// byte-for-byte even when this build has never heard of it, or storing and reloading a row would
    /// lose data, and ISO adds currencies faster than libraries are rebuilt.
    /// </para>
    /// <para>
    /// Reading an inbound payment file wants the opposite. There a typo'd or hostile code should be
    /// rejected at the boundary rather than becoming an amount that rounds to a guessed precision, so
    /// add this flag to the style. It is not part of <see cref="Currency"/>, because turning it on by
    /// default would make the library reject currencies that are perfectly real and merely newer than
    /// the version installed.
    /// </para>
    /// </remarks>
    RequireKnownCurrency = 1 << 10,

    /// <summary>Everything needed for a plain number: whitespace, a leading sign, groups, and decimals.</summary>
    Number = AllowLeadingWhite | AllowTrailingWhite | AllowLeadingSign | AllowThousands | AllowDecimalPoint,

    /// <summary>
    /// The default: a number in any of the usual accounting forms, carrying a currency that must be
    /// identifiable.
    /// </summary>
    Currency = Number | AllowTrailingSign | AllowParentheses | AllowCurrencyCode | AllowCurrencySymbol | RequireCurrency
}
