using System.Globalization;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Riddersholm.Money.EntityFrameworkCore;

/// <summary>
/// Stores a <see cref="Currency"/> as its ISO 4217 alphabetic code in a three-character column.
/// </summary>
/// <remarks>
/// The code is stored rather than a surrogate key, so the column is readable in a database client,
/// portable between systems, and unaffected by this library ever changing its internal representation.
/// A code the library does not recognise round-trips unchanged rather than being rejected.
/// </remarks>
public sealed class CurrencyValueConverter : ValueConverter<Currency, string>
{
    /// <summary>Creates the converter.</summary>
    public CurrencyValueConverter()
        : base(
            currency => currency.Code,
            code => Read(code))
    {
    }

    /// <summary>
    /// Turns a stored code back into a currency, naming the offending value when the column holds
    /// something that is not one.
    /// </summary>
    /// <remarks>
    /// Without this, corrupt data surfaces as a bare <see cref="ArgumentException"/> from deep inside
    /// EF's materialisation, with no indication of which row or value was at fault.
    /// </remarks>
    private static Currency Read(string code) =>
        Currency.TryFromCode(code, out Currency currency)
            ? currency
            : throw new InvalidOperationException(
                $"Cannot read a Currency from the stored value '{code}': an ISO 4217 alphabetic code "
              + "is three ASCII letters. The column contains data this library did not write.");
}

/// <summary>
/// Stores a <see cref="Currency"/> as its ISO 4217 numeric code.
/// </summary>
/// <remarks>
/// For schemas that already use numeric codes. Prefer <see cref="CurrencyValueConverter"/> where the
/// choice is free: numeric codes only resolve for currencies the library knows, so a currency added by
/// ISO after this version was built cannot be stored at all — this converter refuses to write it rather
/// than persisting a placeholder.
/// </remarks>
public sealed class CurrencyNumericValueConverter : ValueConverter<Currency, short>
{
    /// <summary>Creates the converter.</summary>
    public CurrencyNumericValueConverter()
        : base(
            currency => Write(currency),
            code => Convert(code))
    {
    }

    /// <summary>
    /// Turns a currency into its numeric code, refusing any currency that does not have one.
    /// </summary>
    /// <remarks>
    /// <see cref="Currency.NumericCode"/> returns <c>0</c> for a currency the library does not know,
    /// and <c>0</c> is not an ISO 4217 numeric code. Writing it would store a row that identifies no
    /// currency and only fails when something later reads it back — a write that silently loses the
    /// data it was given. Failing at the point of the mistake is the whole difference between a bug
    /// found in a test and a corrupt ledger found in an audit.
    /// </remarks>
    private static short Write(Currency currency) =>
        currency.NumericCode != 0
            ? currency.NumericCode
            : throw new InvalidOperationException(
                $"'{currency.Code}' has no ISO 4217 numeric code, so it cannot be stored by "
              + $"{nameof(CurrencyNumericValueConverter)}. Register it through CurrencyRegistry with a "
              + $"numeric code, or map the column with {nameof(CurrencyValueConverter)}, which stores "
              + "the alphabetic code and round-trips any currency.");

    private static Currency Convert(short numericCode) =>
        Currency.TryFromNumericCode(numericCode, out Currency currency)
            ? currency
            : throw new InvalidOperationException(
                string.Create(CultureInfo.InvariantCulture, $"ISO 4217 numeric code {numericCode} is not a known currency."));
}
