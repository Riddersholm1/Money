namespace Riddersholm.Money;

/// <summary>
/// Thrown when an operation needs metadata for a currency the library does not recognise.
/// </summary>
/// <remarks>
/// <para>
/// An unknown currency is still a usable value — it compares, formats, parses, and persists exactly.
/// Reading its metadata returns a declared fallback rather than throwing, so loading unfamiliar data
/// never crashes.
/// </para>
/// <para>
/// What is refused is <em>changing an amount</em> based on a guessed precision. Rounding to two
/// decimals because that is the ISO default would silently alter money whenever the guess is wrong,
/// so <see cref="Money.Round(System.MidpointRounding)"/> raises this instead. Rounding to an explicit number of decimals
/// works for any currency.
/// </para>
/// </remarks>
public sealed class UnknownCurrencyException : InvalidOperationException
{
    /// <summary>Creates an exception for an unrecognised currency.</summary>
    /// <param name="currency">The currency that has no known metadata.</param>
    public UnknownCurrencyException(Currency currency)
        : base($"'{currency.Code}' is not a known currency, so its precision is unknown. "
             + "Round to an explicit number of decimals, or register the currency with CurrencyRegistry.")
    {
        Currency = currency;
    }

    /// <summary>Creates an exception with a custom message.</summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="currency">The currency whose metadata was missing.</param>
    public UnknownCurrencyException(string message, Currency currency)
        : base(message)
    {
        Currency = currency;
    }

    /// <summary>Creates an exception with a custom message.</summary>
    /// <param name="message">The message that describes the error.</param>
    /// <remarks>
    /// Prefer <see cref="UnknownCurrencyException(string, Riddersholm.Money.Currency)"/> so that
    /// <see cref="Currency"/> carries the offending value rather than defaulting.
    /// </remarks>
    // The parameter type above is fully qualified because the Currency property shadows the type of the
    // same name inside this class. Roslyn resolves the short form correctly — the emitted XML names the
    // constructor overload, not the property — but ReSharper reports CS1580 against it, so the short
    // form costs a false warning in Rider for no gain.
    public UnknownCurrencyException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with a custom message and inner exception.</summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public UnknownCurrencyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates an exception with no particular currency attached.</summary>
    public UnknownCurrencyException()
        : base("The currency is not known.")
    {
    }

    /// <summary>The currency whose metadata was missing.</summary>
    public Currency Currency { get; }
}
