using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Riddersholm.Money.EntityFrameworkCore;

/// <summary>
/// Compares <see cref="Money"/> values for change tracking.
/// </summary>
/// <remarks>
/// <para>
/// EF Core's default comparer for a converted type compares the <em>converted</em> value, which for the
/// text mapping means comparing strings. That is subtly wrong: <c>100 DKK</c> and <c>100.00 DKK</c> are
/// the same money but different strings, so a value that was merely reformatted would be reported as
/// modified and written back needlessly.
/// </para>
/// <para>
/// This comparer uses <see cref="Money"/>'s own equality, which — like <see cref="decimal"/>'s —
/// ignores trailing zeros. Snapshots are cheap: <see cref="Money"/> is an immutable struct, so the
/// value is its own snapshot.
/// </para>
/// </remarks>
public sealed class MoneyValueComparer : ValueComparer<Money>
{
    /// <summary>Creates the comparer.</summary>
    public MoneyValueComparer()
        : base(
            (left, right) => left == right,
            money => money.GetHashCode(),
            money => money)
    {
    }
}

/// <summary>Compares <see cref="Currency"/> values for change tracking.</summary>
/// <remarks>Equality is a single integer comparison, and the struct is its own snapshot.</remarks>
public sealed class CurrencyValueComparer : ValueComparer<Currency>
{
    /// <summary>Creates the comparer.</summary>
    public CurrencyValueComparer()
        : base(
            (left, right) => left == right,
            currency => currency.GetHashCode(),
            currency => currency)
    {
    }
}
