using System.Buffers;
using System.Globalization;
using System.Text;

namespace Riddersholm.Money;

/// <content>
/// Formatting. Every path writes through <c>TryFormat</c> into a stack buffer, so producing text
/// allocates at most the final string — and nothing at all when the caller supplies the buffer.
/// </content>
public readonly partial record struct Money
{
    /// <summary>
    /// Comfortably fits the widest output any ISO currency can produce: 30 characters of
    /// <see cref="decimal"/>, group separators, and the longest name in the generated table.
    /// </summary>
    private const int StackBufferSize = 128;

    /// <summary>
    /// The first fallback size for output that exceeds <see cref="StackBufferSize"/>, which only a
    /// runtime-registered currency with an unusually long name can do. The buffer doubles from here
    /// until the text fits, so this is a starting point rather than a limit.
    /// </summary>
    private const int LargeBufferSize = 1024;

    /// <summary>Returns the amount followed by its ISO 4217 code, in the current culture's number format.</summary>
    /// <remarks>
    /// Never hides precision: <c>100.005 DKK</c> renders all three decimals even though DKK has two, so
    /// a non-canonical amount is visible rather than silently rounded in the output.
    /// </remarks>
    public override string ToString() => ToString(null, null);

    /// <summary>Formats the amount.</summary>
    /// <param name="format">
    /// <list type="table">
    /// <item><term><c>G</c></term><description>Default. <c>100.50 DKK</c> — amount then ISO code, in the provider's number format.</description></item>
    /// <item><term><c>R</c></term><description>Round-trippable. Always invariant; <c>Parse</c> reads it back exactly.</description></item>
    /// <item><term><c>C</c></term><description>Culture currency layout with the symbol: <c>kr. 100,50</c>.</description></item>
    /// <item><term><c>I</c></term><description>ISO layout, code first: <c>DKK 100.50</c>.</description></item>
    /// <item><term><c>N</c></term><description>The number alone, with group separators: <c>1,234.50</c>.</description></item>
    /// <item><term><c>L</c></term><description>Amount and English currency name: <c>100.50 Danish Krone</c>.</description></item>
    /// </list>
    /// A digit count may follow the letter (<c>C0</c>, <c>N4</c>) to override the currency's precision.
    /// </param>
    /// <param name="formatProvider">Supplies separators and layout; <see langword="null"/> means the current culture.</param>
    /// <exception cref="FormatException"><paramref name="format"/> is not recognised.</exception>
    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        Span<char> buffer = stackalloc char[StackBufferSize];

        if (TryFormat(buffer, out int written, format, formatProvider))
        {
            return new string(buffer[..written]);
        }

        // Only reachable for a currency with an unusually long name; borrow a larger buffer.
        char[] rented = RentFormatted(format, formatProvider, out written);

        try
        {
            return new string(rented, 0, written);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(rented);
        }
    }

    /// <inheritdoc cref="ToString(string?, IFormatProvider?)" />
    /// <param name="destination">Receives the formatted text.</param>
    /// <param name="charsWritten">The number of characters written.</param>
    /// <param name="format">The format specifier.</param>
    /// <param name="provider">The format provider.</param>
    /// <returns><see langword="false"/> if <paramref name="destination"/> is too small.</returns>
    public bool TryFormat(
        Span<char> destination,
        out int charsWritten,
        ReadOnlySpan<char> format = default,
        IFormatProvider? provider = null)
    {
        charsWritten = 0;

        (MoneyFormat kind, int? digits) = ParseFormat(format);

        if (kind == MoneyFormat.RoundTrip)
        {
            provider = CultureInfo.InvariantCulture;
        }

        NumberFormatInfo numberFormat = kind == MoneyFormat.Currency
            ? CurrencyFormatCache.ForCurrency(provider, Currency, digits)
            : NumberFormatInfo.GetInstance(provider);

        int position = 0;

        // Code-first layouts put the currency before the number.
        if (kind == MoneyFormat.Iso && !TryWriteCode(destination, ref position, separator: true))
        {
            return false;
        }

        if (!TryWriteAmount(destination[position..], out int amountLength, kind, digits, numberFormat))
        {
            return false;
        }

        position += amountLength;

        switch (kind)
        {
            case MoneyFormat.General or MoneyFormat.RoundTrip:
            {
                if (!TryWriteCode(destination, ref position, separator: false))
                {
                    return false;
                }

                break;
            }
            case MoneyFormat.Name:
            {
                if (!TryWriteText(Currency.EnglishName, destination, ref position, separator: true))
                {
                    return false;
                }

                break;
            }
        }

        charsWritten = position;
        return true;
    }

    /// <inheritdoc cref="TryFormat(Span{char}, out int, ReadOnlySpan{char}, IFormatProvider?)" />
    /// <param name="utf8Destination">Receives the formatted UTF-8 text.</param>
    /// <param name="bytesWritten">The number of bytes written.</param>
    /// <param name="format">The format specifier.</param>
    /// <param name="provider">The format provider.</param>
    public bool TryFormat(
        Span<byte> utf8Destination,
        out int bytesWritten,
        ReadOnlySpan<char> format = default,
        IFormatProvider? provider = null)
    {
        // Formatting to UTF-16 first and transcoding once keeps a single implementation of the layout
        // rules. The intermediate lives on the stack for every realistic input.
        Span<char> buffer = stackalloc char[StackBufferSize];

        if (TryFormat(buffer, out int written, format, provider))
        {
            return Encoding.UTF8.TryGetBytes(buffer[..written], utf8Destination, out bytesWritten);
        }

        // The interface contract says false means *the caller's* buffer was too small, so a shortfall
        // in this method's own scratch space must not be reported as one. A registered currency with a
        // very long name is the case that reaches here.
        return TryFormatUtf8Large(utf8Destination, out bytesWritten, format, provider);
    }

    private bool TryFormatUtf8Large(
        Span<byte> utf8Destination,
        out int bytesWritten,
        ReadOnlySpan<char> format,
        IFormatProvider? provider)
    {
        char[] rented = RentFormatted(format, provider, out int written);

        try
        {
            // The scratch buffer is guaranteed to have held the whole text, so a false here means what
            // the interface says it means: the caller's buffer was too small.
            return Encoding.UTF8.TryGetBytes(rented.AsSpan(0, written), utf8Destination, out bytesWritten);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(rented);
        }
    }

    /// <summary>
    /// Formats into a pooled buffer that doubles until the text fits, and hands the buffer back for the
    /// caller to consume and return.
    /// </summary>
    /// <remarks>
    /// A fixed fallback size would reintroduce the defect this replaced: <c>TryFormat</c> returning
    /// <see langword="false"/> for a destination that was in fact large enough, because an internal
    /// buffer — not the caller's — ran out. Growing until it fits means the only bound on the output is
    /// the length of the currency's own name, which is exactly the contract both overloads document.
    /// </remarks>
    /// <exception cref="FormatException">
    /// The text does not fit the largest array the runtime can allocate, which takes a currency name of
    /// roughly two billion characters.
    /// </exception>
    private char[] RentFormatted(ReadOnlySpan<char> format, IFormatProvider? provider, out int written)
    {
        int size = LargeBufferSize;

        while (true)
        {
            char[] rented = ArrayPool<char>.Shared.Rent(size);
            bool fits;

            try
            {
                fits = TryFormat(rented, out written, format, provider);
            }
            catch
            {
                // An unsupported format string throws out of here; the buffer still goes back.
                ArrayPool<char>.Shared.Return(rented);
                throw;
            }

            if (fits)
            {
                return rented;
            }

            // Rent may hand back more than was asked for, so grow from what was actually tried —
            // otherwise the next attempt could be no larger and the loop would not terminate.
            size = rented.Length;
            ArrayPool<char>.Shared.Return(rented);

            if (size >= Array.MaxLength)
            {
                throw new FormatException($"Could not format '{Amount}' with format '{format}': the result is too large.");
            }

            size = (int)Math.Min((long)size * 2, Array.MaxLength);
        }
    }

    private bool TryWriteAmount(
        Span<char> destination,
        out int written,
        MoneyFormat kind,
        int? digits,
        NumberFormatInfo numberFormat)
    {
        int precision = digits ?? SignificantDigits(Amount, Currency.DecimalDigits);

        Span<char> specifier = stackalloc char[4];
        char letter = kind switch
        {
            MoneyFormat.Currency => 'C',
            MoneyFormat.Number => 'N',
            // 'F' rather than 'N': no group separators, so the output stays trivially parseable.
            _ => 'F'
        };

        specifier[0] = letter;

        // Precision is capped at 28, so two digits always fit the remaining three chars. Checking the
        // result anyway keeps the invariant honest rather than assumed.
        if (!precision.TryFormat(specifier[1..], out int specifierLength, provider: CultureInfo.InvariantCulture))
        {
            written = 0;
            return false;
        }

        return Amount.TryFormat(destination, out written, specifier[..(specifierLength + 1)], numberFormat);
    }

    private bool TryWriteCode(Span<char> destination, ref int position, bool separator)
    {
        int needed = 3 + (position > 0 || separator ? 1 : 0);

        if (destination.Length - position < needed)
        {
            return false;
        }

        if (position > 0)
        {
            destination[position++] = ' ';
        }

        CurrencyCodec.Unpack(Currency.PackedValue, destination[position..]);
        position += 3;

        if (separator && position < destination.Length)
        {
            destination[position++] = ' ';
        }

        return true;
    }

    private static bool TryWriteText(string text, Span<char> destination, ref int position, bool separator)
    {
        int needed = text.Length + (separator ? 1 : 0);

        if (destination.Length - position < needed)
        {
            return false;
        }

        if (separator)
        {
            destination[position++] = ' ';
        }

        text.CopyTo(destination[position..]);
        position += text.Length;
        return true;
    }

    /// <summary>
    /// How many decimals are needed to show the value honestly: at least the currency's precision, and
    /// more when the amount actually carries more.
    /// </summary>
    /// <remarks>
    /// Trailing zeros are ignored, so equal amounts always format identically — <c>100m</c> and
    /// <c>100.00m</c> are the same money and must not render differently.
    /// </remarks>
    private static int SignificantDigits(decimal amount, byte currencyDigits)
    {
        int scale = amount.Scale;

        while (scale > 0 && Math.Round(amount, scale - 1) == amount)
        {
            scale--;
        }

        return Math.Max(scale, currencyDigits);
    }

    private static (MoneyFormat Kind, int? Digits) ParseFormat(ReadOnlySpan<char> format)
    {
        if (format.IsEmpty)
        {
            return (MoneyFormat.General, null);
        }

        MoneyFormat kind = format[0] switch
        {
            'G' or 'g' => MoneyFormat.General,
            'R' or 'r' => MoneyFormat.RoundTrip,
            'C' or 'c' => MoneyFormat.Currency,
            'I' or 'i' => MoneyFormat.Iso,
            'N' or 'n' => MoneyFormat.Number,
            'L' or 'l' => MoneyFormat.Name,
            _ => throw new FormatException(
                $"'{format}' is not a supported Money format string. Use G, R, C, I, N, or L, optionally followed by a digit count.")
        };

        if (format.Length == 1)
        {
            return (kind, null);
        }

        if (!int.TryParse(format[1..], NumberStyles.None, CultureInfo.InvariantCulture, out int digits) || digits > 28)
        {
            throw new FormatException($"'{format}' is not a supported Money format string: expected 0 to 28 digits after '{format[0]}'.");
        }

        // A round-trip format that dropped precision would not round-trip.
        return kind == MoneyFormat.RoundTrip
            ? throw new FormatException("The round-trip format 'R' cannot take a digit count: it must preserve the exact amount.")
            : (kind, digits);
    }

    private enum MoneyFormat
    {
        General,
        RoundTrip,
        Currency,
        Iso,
        Number,
        Name
    }
}
