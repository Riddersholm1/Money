namespace Riddersholm.Money;

/// <content>
/// Comparison, where relational operators and <see cref="IComparable{T}"/> deliberately behave
/// differently.
/// </content>
public readonly partial record struct Money
{
    /// <summary>
    /// Orders two amounts, <b>never throwing</b>, by currency code first and then by amount.
    /// </summary>
    /// <param name="other">The amount to compare against.</param>
    /// <remarks>
    /// <para>
    /// This is what <c>List.Sort</c>, <c>OrderBy</c>, and <c>SortedSet</c> call, and a comparer that
    /// throws corrupts sorting rather than merely failing — it surfaces later as
    /// "IComparer.Compare() method returns inconsistent results". Sorting a mixed-currency list is a
    /// reasonable thing to do, so it produces a stable total order instead of an exception.
    /// </para>
    /// <para>
    /// The relational <b>operators</b> take the opposite view and throw, because
    /// <c>if (price &gt; budget)</c> across two currencies is a bug and should say so at the point it
    /// happens. Use <see cref="TryCompareTo"/> when you want neither.
    /// </para>
    /// </remarks>
    public int CompareTo(Money other)
    {
        int byCurrency = Currency.CompareTo(other.Currency);
        return byCurrency != 0 ? byCurrency : Amount.CompareTo(other.Amount);
    }

    /// <inheritdoc />
    int IComparable.CompareTo(object? obj) => obj switch
    {
        null => 1,
        Money other => CompareTo(other),
        _ => throw new ArgumentException($"Object must be of type {nameof(Money)}.", nameof(obj)),
    };

    /// <summary>Compares two amounts of the same currency without throwing.</summary>
    /// <param name="other">The amount to compare against.</param>
    /// <param name="result">Negative, zero, or positive, in the manner of <see cref="CompareTo"/>.</param>
    /// <returns><see langword="false"/> if the currencies differ, in which case comparison is meaningless.</returns>
    public bool TryCompareTo(Money other, out int result)
    {
        if (Currency != other.Currency)
        {
            result = 0;
            return false;
        }

        result = Amount.CompareTo(other.Amount);
        return true;
    }

    /// <summary>Whether the left amount is less than the right.</summary>
    /// <param name="left">The first amount.</param>
    /// <param name="right">The second amount.</param>
    /// <exception cref="CurrencyMismatchException">The currencies differ.</exception>
    public static bool operator <(Money left, Money right) => Compare(left, right) < 0;

    /// <summary>Whether the left amount is less than or equal to the right.</summary>
    /// <param name="left">The first amount.</param>
    /// <param name="right">The second amount.</param>
    /// <exception cref="CurrencyMismatchException">The currencies differ.</exception>
    public static bool operator <=(Money left, Money right) => Compare(left, right) <= 0;

    /// <summary>Whether the left amount is greater than the right.</summary>
    /// <param name="left">The first amount.</param>
    /// <param name="right">The second amount.</param>
    /// <exception cref="CurrencyMismatchException">The currencies differ.</exception>
    public static bool operator >(Money left, Money right) => Compare(left, right) > 0;

    /// <summary>Whether the left amount is greater than or equal to the right.</summary>
    /// <param name="left">The first amount.</param>
    /// <param name="right">The second amount.</param>
    /// <exception cref="CurrencyMismatchException">The currencies differ.</exception>
    public static bool operator >=(Money left, Money right) => Compare(left, right) >= 0;

    private static int Compare(Money left, Money right) =>
        left.Currency == right.Currency
            ? left.Amount.CompareTo(right.Amount)
            : throw new CurrencyMismatchException(left.Currency, right.Currency);
}
