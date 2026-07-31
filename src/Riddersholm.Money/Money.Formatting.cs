using System.Globalization;

namespace Riddersholm.Money;

/// <content>Textual representation. The full formatting surface is defined here.</content>
public readonly partial record struct Money
{
    /// <summary>Returns the amount followed by its ISO 4217 code, using the current culture's number format.</summary>
    public override string ToString() => ToString(null, null);

    /// <summary>Formats the amount.</summary>
    /// <param name="format">The format specifier; <see langword="null"/> means <c>G</c>.</param>
    /// <param name="formatProvider">Supplies the number format; <see langword="null"/> means the current culture.</param>
    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        NumberFormatInfo numberFormat = NumberFormatInfo.GetInstance(formatProvider);
        return $"{Amount.ToString("0.############################", numberFormat)} {Currency.Code}";
    }
}
