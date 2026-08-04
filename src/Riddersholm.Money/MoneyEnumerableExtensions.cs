namespace Riddersholm.Money;

/// <summary>Aggregations over sequences of <see cref="Money"/>.</summary>
/// <remarks>
/// These exist because <c>Enumerable.Sum</c> has no <see cref="Money"/> overload, and because summing
/// money has a question LINQ's numeric overloads never face: what does an empty sequence, or a
/// mixed-currency one, mean? Each method below answers that explicitly.
/// </remarks>
public static class MoneyEnumerableExtensions
{
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

        return source.Aggregate(Money.AdditiveIdentity, (current, item) => current + selector(item));
    }

    /// <param name="source">The amounts to average.</param>
    extension(IEnumerable<Money> source)
    {
        /// <summary>The mean of a sequence of amounts, exact and unrounded.</summary>
        /// <remarks>
        /// The result is generally not a payable amount — the mean of 10 DKK and 5 DKK across three items
        /// is 5 DKK, but across seven it is not — so round it before display.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">The sequence is empty.</exception>
        /// <exception cref="CurrencyMismatchException">The sequence mixes currencies.</exception>
        public Money Average()
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
        /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">The sequence is empty.</exception>
        /// <exception cref="CurrencyMismatchException">The sequence mixes currencies.</exception>
        public Money Min() => Reduce(source, Money.Min);

        /// <summary>The largest amount in a sequence.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
        /// <exception cref="InvalidOperationException">The sequence is empty.</exception>
        /// <exception cref="CurrencyMismatchException">The sequence mixes currencies.</exception>
        public Money Max() => Reduce(source, Money.Max);

        /// <summary>Adds up a sequence of amounts.</summary>
        /// <returns>
        /// The total. An empty sequence gives <see cref="Money.AdditiveIdentity"/> — zero in
        /// <see cref="Currency.None"/> — rather than throwing, since there is no currency to return zero in.
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
        /// <exception cref="CurrencyMismatchException">The sequence mixes currencies.</exception>
        public Money Sum()
        {
            ArgumentNullException.ThrowIfNull(source);

            return source.Aggregate(Money.AdditiveIdentity, (current, item) => current + item);
        }
    }

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
