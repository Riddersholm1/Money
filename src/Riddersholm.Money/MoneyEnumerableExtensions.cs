namespace Riddersholm.Money;

/// <summary>Aggregations over sequences of <see cref="Money"/>.</summary>
/// <remarks>
/// <para>
/// These exist because <c>Enumerable.Sum</c> has no <see cref="Money"/> overload, and because summing
/// money has a question LINQ's numeric overloads never face: what does an empty sequence, or a
/// mixed-currency one, mean? Each method below answers that explicitly.
/// </para>
/// <para>
/// They are written as classic extension methods rather than C# 14 extension members, deliberately.
/// <c>Microsoft.CodeAnalysis.PublicApiAnalyzers</c> only half-models extension members today: it
/// reports a new one as untracked (RS0016) but does <em>not</em> report a tracked one that has
/// disappeared (RS0017). A public surface the API tracker cannot see a removal from is not something
/// this library can promise stability on, and the aggregations behave identically either way — so the
/// newer syntax buys nothing and costs the guarantee. Revisit when the analyzer catches up.
/// </para>
/// </remarks>
public static class MoneyEnumerableExtensions
{
    /// <summary>Adds up a sequence of amounts.</summary>
    /// <param name="source">The amounts to add.</param>
    /// <returns>
    /// The total. An empty sequence gives <see cref="Money.AdditiveIdentity"/> — zero in
    /// <see cref="Currency.None"/> — rather than throwing, since there is no currency to return zero in.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    /// <exception cref="CurrencyMismatchException">The sequence mixes currencies.</exception>
    public static Money Sum(this IEnumerable<Money> source)
    {
        ArgumentNullException.ThrowIfNull(source);

        // A plain loop rather than Aggregate: this is the most-called aggregate in the library, and
        // Aggregate allocates a delegate on every call for no gain in clarity.
        Money total = Money.AdditiveIdentity;

        foreach (Money item in source)
        {
            total += item;
        }

        return total;
    }

    /// <summary>Adds up a projection of a sequence.</summary>
    /// <typeparam name="TSource">The element type.</typeparam>
    /// <param name="source">The elements.</param>
    /// <param name="selector">Projects each element to an amount.</param>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> or <paramref name="selector"/> is <see langword="null"/>.</exception>
    /// <exception cref="CurrencyMismatchException">The projected amounts mix currencies.</exception>
    public static Money Sum<TSource>(this IEnumerable<TSource> source, Func<TSource, Money> selector)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(selector);

        Money total = Money.AdditiveIdentity;

        foreach (TSource item in source)
        {
            total += selector(item);
        }

        return total;
    }

    /// <summary>The mean of a sequence of amounts, exact and unrounded.</summary>
    /// <param name="source">The amounts to average.</param>
    /// <remarks>
    /// The result is generally not a payable amount — the mean of 10 DKK and 5 DKK across three items
    /// is 5 DKK, but across seven it is not — so round it before display.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The sequence is empty.</exception>
    /// <exception cref="CurrencyMismatchException">The sequence mixes currencies.</exception>
    public static Money Average(this IEnumerable<Money> source)
    {
        ArgumentNullException.ThrowIfNull(source);

        Money total = Money.AdditiveIdentity;
        int count = 0;

        foreach (Money item in source)
        {
            total += item;
            count++;
        }

        return count == 0
            ? throw new InvalidOperationException("Cannot average an empty sequence.")
            : total / count;
    }

    /// <summary>The smallest amount in a sequence.</summary>
    /// <param name="source">The amounts to inspect.</param>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The sequence is empty.</exception>
    /// <exception cref="CurrencyMismatchException">The sequence mixes currencies.</exception>
    public static Money Min(this IEnumerable<Money> source) => Reduce(source, Money.Min);

    /// <summary>The largest amount in a sequence.</summary>
    /// <param name="source">The amounts to inspect.</param>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The sequence is empty.</exception>
    /// <exception cref="CurrencyMismatchException">The sequence mixes currencies.</exception>
    public static Money Max(this IEnumerable<Money> source) => Reduce(source, Money.Max);

    private static Money Reduce(IEnumerable<Money> source, Func<Money, Money, Money> combine)
    {
        ArgumentNullException.ThrowIfNull(source);

        using IEnumerator<Money> enumerator = source.GetEnumerator();

        if (!enumerator.MoveNext())
        {
            throw new InvalidOperationException("The sequence contains no elements.");
        }

        Money result = enumerator.Current;

        while (enumerator.MoveNext())
        {
            result = combine(result, enumerator.Current);
        }

        return result;
    }
}
