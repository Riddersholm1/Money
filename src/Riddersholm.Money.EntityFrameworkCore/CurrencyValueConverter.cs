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
/// ISO after this version was built would not survive a round trip.
/// </remarks>
public sealed class CurrencyNumericValueConverter : ValueConverter<Currency, short>
{
    /// <summary>Creates the converter.</summary>
    public CurrencyNumericValueConverter()
        : base(
            currency => currency.NumericCode,
            code => Convert(code))
    {
    }

    private static Currency Convert(short numericCode) =>
        Currency.TryFromNumericCode(numericCode, out Currency currency)
            ? currency
            : throw new InvalidOperationException(
                string.Create(CultureInfo.InvariantCulture, $"ISO 4217 numeric code {numericCode} is not a known currency."));
}
