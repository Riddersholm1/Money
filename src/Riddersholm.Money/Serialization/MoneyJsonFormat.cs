namespace Riddersholm.Money.Serialization;

/// <summary>How <see cref="Money"/> is written to JSON.</summary>
/// <remarks>
/// All three forms are <em>read</em> regardless of which one is configured, so changing the write
/// format never breaks documents that are already stored.
/// </remarks>
public enum MoneyJsonFormat
{
    /// <summary>
    /// The default: <c>{"amount":100.50,"currency":"DKK"}</c>.
    /// </summary>
    NumericAmount,

    /// <summary>
    /// <c>{"amount":"100.50","currency":"DKK"}</c> — the same shape with the amount quoted.
    /// </summary>
    /// <remarks>
    /// JSON numbers are IEEE 754 doubles in JavaScript, so a browser reading a sufficiently precise or
    /// sufficiently large amount silently loses digits. Quoting keeps it exact for consumers that have
    /// no decimal type of their own.
    /// </remarks>
    StringAmount,

    /// <summary>
    /// <c>"100.50 DKK"</c> — the compact round-trippable form produced by <c>ToString("R")</c>.
    /// </summary>
    Compact
}
