using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Riddersholm.Money.EntityFrameworkCore;

/// <summary>
/// Refuses to save a <see cref="Money"/> whose amount does not fit the column it is mapped to, instead
/// of letting the database round it.
/// </summary>
/// <remarks>
/// <para>
/// <c>HasMoney</c> maps the amount to <c>decimal(19,4)</c> by default. Saving <c>13.40123 EUR</c> into
/// that column does not fail: SQL Server and PostgreSQL round it to <c>13.4012</c> without a word, and
/// the value read back is no longer the value written. Everywhere else this library refuses to round
/// on your behalf — the database must not do it either. Register this interceptor and the save throws
/// <see cref="InvalidOperationException"/> naming the entity, the property and the amount, before
/// anything is sent.
/// </para>
/// <para>
/// It checks every added or modified entity's <see cref="Money"/> complex properties, including ones
/// nested inside other complex types, against the precision and scale the model declares for their
/// amount column. Properties mapped without a declared precision, and the single-column text mapping
/// (which stores the amount exactly), are not checked.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// services.AddDbContext&lt;LedgerContext&gt;(options =&gt; options
///     .UseSqlServer(connectionString)
///     .UseMoneyPrecisionGuard());
/// </code>
/// </example>
public sealed class MoneyPrecisionInterceptor : SaveChangesInterceptor
{
    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Validate(eventData.Context);
        return result;
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Validate(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private static void Validate(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        // Entries() runs change detection first, so a Money replaced on a tracked entity is seen here.
        foreach (EntityEntry entry in context.ChangeTracker.Entries())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                Validate(entry.Metadata.DisplayName(), entry.ComplexProperties);
            }
        }
    }

    private static void Validate(string owner, IEnumerable<ComplexPropertyEntry> complexProperties)
    {
        foreach (ComplexPropertyEntry complex in complexProperties)
        {
            // An optional Money? that is null stores nulls, and there is nothing to round.
            if (complex.CurrentValue is null)
            {
                continue;
            }

            string path = owner + "." + complex.Metadata.Name;

            if (complex.CurrentValue is Money money)
            {
                PropertyEntry amount = complex.Property(nameof(Money.Amount));
                Check(path, money, amount.Metadata.GetPrecision(), amount.Metadata.GetScale());
                continue;
            }

            Validate(path, complex.ComplexProperties);
        }
    }

    private static void Check(string path, Money money, int? precision, int? scale)
    {
        if (scale is not { } columnScale)
        {
            return;
        }

        int needed = SignificantScale(money.Amount);

        if (needed > columnScale)
        {
            string value = money.ToString("R", CultureInfo.InvariantCulture);

            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"Cannot save {path}: {value} has {needed} decimal places, but its column stores {columnScale}, so the database would round it without reporting it. Round the amount before saving it — money.Round() for the currency's own precision — or map the property with a larger scale."));
        }

        if (precision is { } columnPrecision && IntegerDigits(money.Amount) > columnPrecision - columnScale)
        {
            string value = money.ToString("R", CultureInfo.InvariantCulture);

            throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture,
                $"Cannot save {path}: {value} has more integer digits than its decimal({columnPrecision},{columnScale}) column holds. Map the property with a larger precision."));
        }
    }

    /// <summary>Decimal places the value actually needs: trailing zeros are formatting, not precision.</summary>
    private static int SignificantScale(decimal amount)
    {
        int scale = amount.Scale;

        while (scale > 0 && Math.Round(amount, scale - 1) == amount)
        {
            scale--;
        }

        return scale;
    }

    private static int IntegerDigits(decimal amount)
    {
        decimal whole = Math.Abs(decimal.Truncate(amount));
        int digits = 0;

        while (whole >= 1m)
        {
            whole = decimal.Truncate(whole / 10m);
            digits++;
        }

        return digits;
    }
}

/// <summary>Registers <see cref="MoneyPrecisionInterceptor"/> on a context's options.</summary>
public static class MoneyDbContextOptionsBuilderExtensions
{
    /// <summary>
    /// Makes <c>SaveChanges</c> refuse a <see cref="Money"/> amount that its column would round.
    /// </summary>
    /// <param name="optionsBuilder">The options being configured.</param>
    /// <returns><paramref name="optionsBuilder"/>, for chaining.</returns>
    /// <remarks>See <see cref="MoneyPrecisionInterceptor"/>.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="optionsBuilder"/> is <see langword="null"/>.</exception>
    public static DbContextOptionsBuilder UseMoneyPrecisionGuard(this DbContextOptionsBuilder optionsBuilder)
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);

        return optionsBuilder.AddInterceptors(new MoneyPrecisionInterceptor());
    }

    /// <inheritdoc cref="UseMoneyPrecisionGuard(DbContextOptionsBuilder)" />
    /// <typeparam name="TContext">The context type.</typeparam>
    public static DbContextOptionsBuilder<TContext> UseMoneyPrecisionGuard<TContext>(this DbContextOptionsBuilder<TContext> optionsBuilder)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(optionsBuilder);

        return optionsBuilder.AddInterceptors(new MoneyPrecisionInterceptor());
    }
}
