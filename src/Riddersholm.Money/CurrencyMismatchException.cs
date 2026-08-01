namespace Riddersholm.Money;

/// <summary>
/// Thrown when an operation combines two amounts in different currencies.
/// </summary>
/// <remarks>
/// <para>
/// Adding 100 DKK to 50 EUR has no answer without an exchange rate, and quietly picking one — or
/// quietly picking a currency for the result — is how money goes missing. Use
/// <see cref="ExchangeRate"/> to convert deliberately.
/// </para>
/// <para>
/// Note that equality never throws: <c>100 DKK == 100 EUR</c> is <see langword="false"/>, because
/// "are these the same amount of money?" has a correct answer and it is "no". Only operations whose
/// result would be meaningless raise this.
/// </para>
/// </remarks>
public sealed class CurrencyMismatchException : InvalidOperationException
{
    /// <summary>Creates an exception describing a mismatch between two currencies.</summary>
    /// <param name="left">The currency of the left operand.</param>
    /// <param name="right">The currency of the right operand.</param>
    public CurrencyMismatchException(Currency left, Currency right)
        : base($"Cannot combine {left.Code} and {right.Code}: the currencies differ. Convert one of them with an ExchangeRate first.")
    {
        Left = left;
        Right = right;
    }

    /// <summary>Creates an exception with a custom message.</summary>
    /// <param name="message">The message that describes the error.</param>
    public CurrencyMismatchException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with a custom message and inner exception.</summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public CurrencyMismatchException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates an exception with no particular currencies attached.</summary>
    public CurrencyMismatchException()
        : base("Cannot combine amounts in different currencies.")
    {
    }

    /// <summary>The currency of the left operand.</summary>
    public Currency Left { get; }

    /// <summary>The currency of the right operand.</summary>
    public Currency Right { get; }
}
