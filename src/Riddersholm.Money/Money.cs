using System.Diagnostics;
using System.Globalization;
using System.Numerics;

namespace Riddersholm.Money;

/// <summary>
/// An immutable amount of money: a <see cref="decimal"/> paired with a <see cref="Currency"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Amounts are exact.</b> <c>new Money(100.005m, Currency.DKK)</c> keeps <c>100.005</c>, and
/// multiplication and division never round. Rounding is a policy decision — whether VAT is rounded per
/// line or per invoice is a domain question with legal consequences — so the library refuses to make
/// it on your behalf. Call <see cref="Round()"/> when you mean to, which is usually when persisting or
/// displaying. <see cref="IsCanonical"/> tells you whether an amount is representable in its currency.
/// </para>
/// <para>
/// The type is a <c>readonly record struct</c>, so equality is structural, copies are cheap, and no
/// instance ever allocates. Its properties are deliberately get-only, which makes
/// <c>price with { Currency = Currency.EUR }</c> a compile error rather than an unrecorded currency
/// conversion.
/// </para>
/// <para>
/// <c>default(Money)</c> is zero in <see cref="Currency.None"/>, and that value is the additive
/// identity: adding it to any amount returns the other operand, so <c>Sum</c> and <c>Aggregate</c> work
/// with a default seed. A <em>non-zero</em> amount in <c>XXX</c> still refuses to mix with real
/// currencies.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var price = new Money(100m, Currency.DKK);
/// var total = price * 3;                       // 300 DKK
/// var each  = total.Allocate(3);               // 100, 100, 100 — never loses a øre
/// Console.WriteLine(total);                    // 300 DKK
/// </code>
/// </example>
[DebuggerDisplay("{Amount} {Currency.Code,nq}")]
public readonly partial record struct Money :
    IAdditionOperators<Money, Money, Money>,
    ISubtractionOperators<Money, Money, Money>,
    IUnaryNegationOperators<Money, Money>,
    IUnaryPlusOperators<Money, Money>,
    IMultiplyOperators<Money, decimal, Money>,
    IDivisionOperators<Money, decimal, Money>,
    IDivisionOperators<Money, Money, decimal>,
    IComparisonOperators<Money, Money, bool>,
    IAdditiveIdentity<Money, Money>,
    IComparable<Money>,
    IComparable
{
    /// <summary>Creates an amount in a currency, preserving the value exactly.</summary>
    /// <param name="amount">The amount. It is stored as given and is not rounded to the currency's precision.</param>
    /// <param name="currency">The currency the amount is denominated in.</param>
    public Money(decimal amount, Currency currency)
    {
        Amount = amount;
        Currency = currency;
    }

    /// <summary>Creates an amount in a currency identified by its ISO 4217 alphabetic code.</summary>
    /// <param name="amount">The amount, stored exactly.</param>
    /// <param name="currencyCode">Three ASCII letters, for example <c>DKK</c>.</param>
    /// <exception cref="ArgumentException"><paramref name="currencyCode"/> is not a valid code.</exception>
    public Money(decimal amount, ReadOnlySpan<char> currencyCode)
        : this(amount, Currency.FromCode(currencyCode))
    {
    }

    /// <summary>The exact amount. Not necessarily representable in <see cref="Currency"/> — see <see cref="IsCanonical"/>.</summary>
    public decimal Amount { get; }

    /// <summary>The currency this amount is denominated in.</summary>
    public Currency Currency { get; }

    /// <summary>Zero in the given currency.</summary>
    /// <param name="currency">The currency of the resulting amount.</param>
    public static Money Zero(Currency currency) => new(0m, currency);

    /// <summary>
    /// Zero in <see cref="Currency.None"/> — the value that adding to any amount leaves unchanged.
    /// </summary>
    /// <remarks>
    /// Equal to <c>default(Money)</c>. This is what makes <c>moneys.Sum()</c> and
    /// <c>moneys.Aggregate((a, b) =&gt; a + b)</c> work without a currency-specific seed.
    /// </remarks>
    public static Money AdditiveIdentity => default;

    /// <summary>Whether the amount is exactly zero, in any currency.</summary>
    public bool IsZero => Amount == 0m;

    /// <summary>Whether the amount is greater than zero.</summary>
    public bool IsPositive => Amount > 0m;

    /// <summary>Whether the amount is less than zero.</summary>
    public bool IsNegative => Amount < 0m;

    /// <summary>The sign of the amount: <c>-1</c>, <c>0</c>, or <c>1</c>.</summary>
    public int Sign => Math.Sign(Amount);

    /// <summary>
    /// Whether this is the additive identity — zero in <see cref="Currency.None"/>, the one value that
    /// combines freely with any currency.
    /// </summary>
    internal bool IsAdditiveIdentity => Amount == 0m && Currency.IsNone;

    /// <summary>Splits the amount and currency, for pattern matching and deconstruction.</summary>
    /// <param name="amount">Receives <see cref="Amount"/>.</param>
    /// <param name="currency">Receives <see cref="Currency"/>.</param>
    public void Deconstruct(out decimal amount, out Currency currency)
    {
        amount = Amount;
        currency = Currency;
    }

    /// <summary>The absolute value, keeping the currency.</summary>
    public Money Abs() => new(Math.Abs(Amount), Currency);

    /// <summary>The amount with its sign flipped.</summary>
    public Money Negate() => new(-Amount, Currency);

    /// <summary>The same amount in a different currency, <b>without converting it</b>.</summary>
    /// <param name="currency">The currency to reinterpret the amount as.</param>
    /// <remarks>
    /// This relabels rather than converts, so it is almost always the wrong tool: 100 DKK is not 100
    /// EUR. It exists for the narrow case of correcting data that was stored under the wrong code. To
    /// change currency by value, use <see cref="ExchangeRate.Convert(Money)"/>.
    /// </remarks>
    public Money WithCurrency(Currency currency) => new(Amount, currency);

    /// <summary>The smaller of two amounts.</summary>
    /// <param name="left">The first amount.</param>
    /// <param name="right">The second amount.</param>
    /// <exception cref="CurrencyMismatchException">The currencies differ.</exception>
    public static Money Min(Money left, Money right) =>
        EnsureComparable(left, right) <= 0 ? left : right;

    /// <summary>The larger of two amounts.</summary>
    /// <param name="left">The first amount.</param>
    /// <param name="right">The second amount.</param>
    /// <exception cref="CurrencyMismatchException">The currencies differ.</exception>
    public static Money Max(Money left, Money right) =>
        EnsureComparable(left, right) >= 0 ? left : right;

    /// <summary>Clamps the amount to a range.</summary>
    /// <param name="value">The amount to clamp.</param>
    /// <param name="min">The inclusive lower bound.</param>
    /// <param name="max">The inclusive upper bound.</param>
    /// <exception cref="CurrencyMismatchException">The currencies differ.</exception>
    /// <exception cref="ArgumentException"><paramref name="min"/> is greater than <paramref name="max"/>.</exception>
    public static Money Clamp(Money value, Money min, Money max)
    {
        if (EnsureComparable(min, max) > 0)
        {
            throw new ArgumentException($"'{min}' cannot be greater than '{max}'.", nameof(min));
        }

        return Min(Max(value, min), max);
    }

    private static int EnsureComparable(Money left, Money right)
    {
        if (left.Currency != right.Currency)
        {
            throw new CurrencyMismatchException(left.Currency, right.Currency);
        }

        return left.Amount.CompareTo(right.Amount);
    }
}
