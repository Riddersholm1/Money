using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Riddersholm.Money.EntityFrameworkCore;

/// <summary>
/// Stores a <see cref="Money"/> in a single text column as <c>100.50 DKK</c>.
/// </summary>
/// <remarks>
/// <para>
/// Convenient, but the weaker of the two mappings. A single column cannot be aggregated
/// (<c>SUM(Price)</c> is impossible), cannot be compared or ordered in SQL, and cannot be indexed
/// usefully. Prefer <c>MoneyModelBuilderExtensions.HasMoney</c>, which maps the amount and
/// currency to their own columns.
/// </para>
/// <para>
/// The stored form is the round-trippable <c>R</c> format, always invariant, so the column reads the
/// same regardless of the server's locale.
/// </para>
/// </remarks>
public sealed class MoneyValueConverter : ValueConverter<Money, string>
{
    private const MoneyStyles StoredStyles =
        Money.RoundTripStyles | MoneyStyles.AllowLeadingWhite | MoneyStyles.AllowTrailingWhite;

    /// <summary>Creates the converter.</summary>
    public MoneyValueConverter()
        : base(money => money.ToString("R", CultureInfo.InvariantCulture),
            text => Read(text))
    {
    }

    /// <summary>
    /// Parses a stored amount, naming the offending value when the column holds something else.
    /// </summary>
    /// <remarks>
    /// Without this, corrupt data surfaces as a bare <see cref="FormatException"/> from deep inside
    /// EF's materialisation, with no indication of which row or value was at fault.
    /// </remarks>
    private static Money Read(string text) =>
        // Read back with the shape the 'R' format writes. The general styles accept group separators,
        // which under the invariant culture would read a hand-edited "1,5 DKK" as fifteen kroner rather
        // than refusing it. Surrounding whitespace stays acceptable because it is unambiguous, and a
        // column someone has mapped as fixed-length char comes back padded with it.
        Money.TryParse(text, StoredStyles, CultureInfo.InvariantCulture, out Money money)
            ? money
            : throw new InvalidOperationException(
                $"Cannot read a Money from the stored value '{text}': the expected form is an amount "
              + "and an ISO 4217 code, such as '100.50 DKK'. The column contains data this library did not write.");
}
