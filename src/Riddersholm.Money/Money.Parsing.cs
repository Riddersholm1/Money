using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace Riddersholm.Money;

/// <content>
/// Parsing.
/// </content>
/// <remarks>
/// <para>
/// The governing rule is that the parser never guesses a currency. An ISO code is unambiguous and is
/// always accepted. A <em>symbol</em> is not: <c>kr</c> is DKK, NOK, SEK, and ISK; <c>$</c> covers a
/// dozen currencies; <c>£</c> is GBP, EGP, and SYP. So a symbol is only resolved against the culture
/// the caller supplied, where exactly one answer is possible. Text that cannot be resolved fails
/// instead of being guessed at.
/// </para>
/// <para>
/// Once the currency token is dealt with, the number itself is handed to
/// <see cref="decimal.TryParse(ReadOnlySpan{char}, NumberStyles, IFormatProvider, out decimal)"/>, so
/// signs, parentheses, group separators and every culture's negative-currency pattern behave exactly as
/// they do everywhere else in .NET.
/// </para>
/// </remarks>
public readonly partial record struct Money :
    IParsable<Money>,
    ISpanParsable<Money>,
    IUtf8SpanParsable<Money>
{
    /// <summary>The default styles: an amount in any usual accounting form, with an identifiable currency.</summary>
    public const MoneyStyles DefaultStyles = MoneyStyles.Currency;

    /// <summary>Parses an amount of money.</summary>
    /// <param name="s">Text such as <c>100.50 DKK</c>, <c>DKK 100.50</c>, or — with a Danish culture — <c>100,50 kr.</c></param>
    /// <param name="provider">
    /// Supplies the number format, and the one currency symbol that may be recognised. Pass a
    /// <see cref="CultureInfo"/> to accept symbols; without one, only ISO codes are understood.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="s"/> is <see langword="null"/>.</exception>
    /// <exception cref="FormatException">The text is not a recognisable amount, or its currency is ambiguous.</exception>
    public static Money Parse(string s, IFormatProvider? provider = null)
    {
        ArgumentNullException.ThrowIfNull(s);
        return Parse(s.AsSpan(), DefaultStyles, provider);
    }

    /// <inheritdoc cref="Parse(string, IFormatProvider?)" />
    /// <param name="s">The text to parse.</param>
    /// <param name="style">Which forms to accept.</param>
    /// <param name="provider">The format provider.</param>
    public static Money Parse(string s, MoneyStyles style, IFormatProvider? provider = null)
    {
        ArgumentNullException.ThrowIfNull(s);
        return Parse(s.AsSpan(), style, provider);
    }

    /// <inheritdoc cref="Parse(string, IFormatProvider?)" />
    public static Money Parse(ReadOnlySpan<char> s, IFormatProvider? provider = null) =>
        Parse(s, DefaultStyles, provider);

    /// <inheritdoc cref="Parse(string, MoneyStyles, IFormatProvider?)" />
    public static Money Parse(ReadOnlySpan<char> s, MoneyStyles style, IFormatProvider? provider) =>
        TryParse(s, style, provider, out Money result)
            ? result
            : throw new FormatException(
                $"'{s}' is not a recognisable amount of money. Amounts need an unambiguous currency: an ISO 4217 "
              + "code such as 'DKK', or a symbol belonging to the culture passed as the format provider.");

    /// <inheritdoc cref="Parse(string, IFormatProvider?)" />
    /// <param name="utf8Text">UTF-8 encoded text.</param>
    /// <param name="provider">The format provider.</param>
    public static Money Parse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider = null) =>
        Parse(utf8Text, DefaultStyles, provider);

    /// <inheritdoc cref="Parse(ReadOnlySpan{byte}, IFormatProvider?)" />
    /// <param name="utf8Text">UTF-8 encoded text.</param>
    /// <param name="style">Which forms to accept.</param>
    /// <param name="provider">The format provider.</param>
    public static Money Parse(ReadOnlySpan<byte> utf8Text, MoneyStyles style, IFormatProvider? provider) =>
        TryParse(utf8Text, style, provider, out Money result)
            ? result
            : throw new FormatException(
                $"'{Encoding.UTF8.GetString(utf8Text)}' is not a recognisable amount of money.");

    /// <summary>Parses an amount of money, reporting failure rather than throwing.</summary>
    /// <param name="s">The text to parse.</param>
    /// <param name="provider">The format provider.</param>
    /// <param name="result">The parsed amount, or <c>default</c> on failure.</param>
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out Money result) =>
        TryParse(s, DefaultStyles, provider, out result);

    /// <inheritdoc cref="TryParse(string?, IFormatProvider?, out Money)" />
    /// <param name="s">The text to parse.</param>
    /// <param name="style">Which forms to accept.</param>
    /// <param name="provider">The format provider.</param>
    /// <param name="result">The parsed amount, or <c>default</c> on failure.</param>
    public static bool TryParse(
        [NotNullWhen(true)] string? s,
        MoneyStyles style,
        IFormatProvider? provider,
        out Money result)
    {
        if (s is null)
        {
            result = default;
            return false;
        }

        return TryParse(s.AsSpan(), style, provider, out result);
    }

    /// <inheritdoc cref="TryParse(string?, IFormatProvider?, out Money)" />
    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out Money result) =>
        TryParse(s, DefaultStyles, provider, out result);

    /// <inheritdoc cref="TryParse(string?, MoneyStyles, IFormatProvider?, out Money)" />
    public static bool TryParse(ReadOnlySpan<char> s, MoneyStyles style, IFormatProvider? provider, out Money result)
    {
        result = default;

        ReadOnlySpan<char> text = s.Trim();

        if (text.IsEmpty)
        {
            return false;
        }

        TakeCurrency(ref text, style, provider, out Currency currency, out bool found);

        // 'found' is deliberately distinct from 'currency is None': XXX is a real ISO code, so
        // "1234.5 XXX" names a currency explicitly and must round-trip like any other.
        if (!found && style.HasFlag(MoneyStyles.RequireCurrency))
        {
            return false;
        }

        // Checked here rather than inside TakeCurrency so that "found but unknown" stays distinct from
        // "not found": a caller using RequireKnownCurrency without RequireCurrency still accepts text
        // with no currency at all, which is what the two flags separately mean.
        if (found && style.HasFlag(MoneyStyles.RequireKnownCurrency) && !currency.IsKnown)
        {
            return false;
        }

        // The provider always governs the *number*, whichever way the currency was identified —
        // "1.234,50 DKK" under da-DK is twelve hundred kroner, not one. Only currency resolution is
        // restricted, because only symbols are ambiguous.
        //
        // Any matched symbol is left in the text for decimal.TryParse, whose AllowCurrencySymbol knows
        // where every culture puts it, including all sixteen negative-currency patterns.
        if (!decimal.TryParse(text.Trim(), ToNumberStyles(style), NumberFormatInfo.GetInstance(provider), out decimal amount))
        {
            return false;
        }

        result = new Money(amount, currency);
        return true;
    }

    /// <inheritdoc cref="TryParse(string?, IFormatProvider?, out Money)" />
    /// <param name="utf8Text">UTF-8 encoded text.</param>
    /// <param name="provider">The format provider.</param>
    /// <param name="result">The parsed amount, or <c>default</c> on failure.</param>
    public static bool TryParse(ReadOnlySpan<byte> utf8Text, IFormatProvider? provider, out Money result) =>
        TryParse(utf8Text, DefaultStyles, provider, out result);

    /// <inheritdoc cref="TryParse(ReadOnlySpan{byte}, IFormatProvider?, out Money)" />
    /// <param name="utf8Text">UTF-8 encoded text.</param>
    /// <param name="style">Which forms to accept.</param>
    /// <param name="provider">The format provider.</param>
    /// <param name="result">The parsed amount, or <c>default</c> on failure.</param>
    public static bool TryParse(
        ReadOnlySpan<byte> utf8Text,
        MoneyStyles style,
        IFormatProvider? provider,
        out Money result)
    {
        result = default;

        // Transcoding once onto the stack keeps a single implementation of the parsing rules. The
        // upper bound is generous: UTF-8 never produces more UTF-16 units than it has bytes.
        if (utf8Text.Length > 256)
        {
            return TryParse(Encoding.UTF8.GetString(utf8Text).AsSpan(), style, provider, out result);
        }

        Span<char> buffer = stackalloc char[256];

        return Encoding.UTF8.TryGetChars(utf8Text, buffer, out int written)
            && TryParse(buffer[..written], style, provider, out result);
    }

    /// <summary>
    /// Removes the currency token from <paramref name="text"/> and reports what it named.
    /// </summary>
    /// <param name="text">The remaining text, with any currency token removed.</param>
    /// <param name="style">Which currency forms may be recognised.</param>
    /// <param name="provider">Supplies the one culture whose symbol may be resolved.</param>
    /// <param name="currency">The currency named by the token, or <see cref="Currency.None"/>.</param>
    /// <param name="found">
    /// Whether a currency token was present at all. Distinct from <paramref name="currency"/> being
    /// <see cref="Currency.None"/>, because <c>XXX</c> is itself a currency someone can write.
    /// </param>
    private static void TakeCurrency(
        ref ReadOnlySpan<char> text,
        MoneyStyles style,
        IFormatProvider? provider,
        out Currency currency,
        out bool found)
    {
        currency = Currency.None;
        found = false;

        // An ISO code is unambiguous, so it is tried first and wins outright.
        if (style.HasFlag(MoneyStyles.AllowCurrencyCode) && TryTakeIsoCode(ref text, out currency))
        {
            found = true;
            return;
        }

        if (!style.HasFlag(MoneyStyles.AllowCurrencySymbol))
        {
            return;
        }

        // A symbol only means something relative to a culture. Without one there is nothing to resolve
        // it against, and guessing between DKK, NOK, SEK and ISK is exactly what this library refuses
        // to do.
        if (provider is not CultureInfo culture)
        {
            return;
        }

        string symbol = culture.NumberFormat.CurrencySymbol;

        if (symbol.Length == 0 || !ContainsSymbol(text, symbol))
        {
            return;
        }

        // The culture has a symbol but may have no region to attribute it to, in which case the
        // symbol identifies nothing and the text stays unresolved.
        found = TryGetRegionCurrency(culture, out currency);
    }

    private static bool TryTakeIsoCode(ref ReadOnlySpan<char> text, out Currency currency)
    {
        currency = Currency.None;

        if (text.Length < 4 && text.Length != 3)
        {
            return false;
        }

        // Trailing code: "100.50 DKK". The character before must not be a letter, so "100 kroner"
        // does not surrender its last three letters.
        if (IsCodeAt(text, text.Length - 3) && (text.Length == 3 || !char.IsAsciiLetter(text[^4])))
        {
            if (Currency.TryFromCode(text[^3..], out currency))
            {
                text = text[..^3].TrimEnd();
                return true;
            }
        }

        // Leading code: "DKK 100.50".
        if (IsCodeAt(text, 0) && (text.Length == 3 || !char.IsAsciiLetter(text[3])))
        {
            if (Currency.TryFromCode(text[..3], out currency))
            {
                text = text[3..].TrimStart();
                return true;
            }
        }

        return false;
    }

    private static bool IsCodeAt(ReadOnlySpan<char> text, int start) =>
        start >= 0
        && start + 3 <= text.Length
        && char.IsAsciiLetter(text[start])
        && char.IsAsciiLetter(text[start + 1])
        && char.IsAsciiLetter(text[start + 2]);

    /// <summary>Whether the culture's symbol appears at all, so a currency can be attributed.</summary>
    /// <remarks>
    /// <para>
    /// Deliberately permissive. This decides only <em>which</em> currency the text might be in; whether
    /// the text actually parses is settled afterwards by <see cref="decimal.TryParse(ReadOnlySpan{char}, NumberStyles, IFormatProvider, out decimal)"/>
    /// with <see cref="NumberStyles.AllowCurrencySymbol"/>, which knows where each culture puts the
    /// symbol. A false positive here — "kr" inside "kroner" — costs nothing, because the number parse
    /// then fails and the whole attempt returns <see langword="false"/>.
    /// </para>
    /// <para>
    /// An earlier version restricted this to a leading or trailing match after trimming ASCII signs and
    /// brackets. That looked tidier and broke right-to-left cultures outright: <c>fa-IR</c> writes a
    /// negative amount as <c>U+200E U+2212 ریال…</c>, where neither the mark nor the minus sign is
    /// ASCII, so the symbol no longer led or trailed and the currency became unresolvable. The culture
    /// matrix test exists because of that regression.
    /// </para>
    /// </remarks>
    private static bool ContainsSymbol(ReadOnlySpan<char> text, string symbol) =>
        text.Contains(symbol, StringComparison.OrdinalIgnoreCase);

    private static bool TryGetRegionCurrency(CultureInfo culture, out Currency currency)
    {
        currency = Currency.None;

        if (culture.Name.Length == 0)
        {
            return false;
        }

        try
        {
            return Currency.TryFromCode(new RegionInfo(culture.Name).ISOCurrencySymbol, out currency);
        }
        catch (ArgumentException)
        {
            // Neutral or unrecognised cultures have no region, and so no currency.
            return false;
        }
    }

    private static NumberStyles ToNumberStyles(MoneyStyles style)
    {
        NumberStyles result = NumberStyles.None;

        if (style.HasFlag(MoneyStyles.AllowLeadingWhite))
        {
            result |= NumberStyles.AllowLeadingWhite;
        }

        if (style.HasFlag(MoneyStyles.AllowTrailingWhite))
        {
            result |= NumberStyles.AllowTrailingWhite;
        }

        if (style.HasFlag(MoneyStyles.AllowLeadingSign))
        {
            result |= NumberStyles.AllowLeadingSign;
        }

        if (style.HasFlag(MoneyStyles.AllowTrailingSign))
        {
            result |= NumberStyles.AllowTrailingSign;
        }

        if (style.HasFlag(MoneyStyles.AllowParentheses))
        {
            result |= NumberStyles.AllowParentheses;
        }

        if (style.HasFlag(MoneyStyles.AllowThousands))
        {
            result |= NumberStyles.AllowThousands;
        }

        if (style.HasFlag(MoneyStyles.AllowDecimalPoint))
        {
            result |= NumberStyles.AllowDecimalPoint;
        }

        if (style.HasFlag(MoneyStyles.AllowCurrencySymbol))
        {
            result |= NumberStyles.AllowCurrencySymbol;
        }

        return result;
    }
}
