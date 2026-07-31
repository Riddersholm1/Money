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
/// usefully. Prefer
/// <see cref="MoneyModelBuilderExtensions.HasMoneyConversion{TEntity}(Microsoft.EntityFrameworkCore.Metadata.Builders.ComplexPropertyBuilder{Money})"/>,
/// which maps the amount and currency to their own columns.
/// </para>
/// <para>
/// The stored form is the round-trippable <c>R</c> format, always invariant, so the column reads the
/// same regardless of the server's locale.
/// </para>
/// </remarks>
public sealed class MoneyValueConverter : ValueConverter<Money, string>
{
    /// <summary>Creates the converter.</summary>
    public MoneyValueConverter()
        : base(
            money => money.ToString("R", CultureInfo.InvariantCulture),
            text => Money.Parse(text, CultureInfo.InvariantCulture))
    {
    }
}
