using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Riddersholm.Money.EntityFrameworkCore;

/// <summary>Maps <see cref="Money"/> and <see cref="Currency"/> onto database columns.</summary>
/// <remarks>
/// <para>
/// There are two mappings, and the choice matters.
/// </para>
/// <para>
/// <b>Two columns</b> — <c>HasMoney</c> — maps the amount and currency separately using
/// an EF complex type. This is the one to reach for: the amount stays a real numeric column, so
/// <c>SUM</c>, <c>ORDER BY</c>, range predicates and indexes all work in SQL.
/// </para>
/// <para>
/// <b>One column</b> — <see cref="ConfigureMoneyConventions"/> — stores <c>100.50 DKK</c> as text. It is
/// a single line for the whole model and keeps schemas narrow, at the cost of being opaque to the
/// database: nothing can be aggregated or compared server-side.
/// </para>
/// </remarks>
public static class MoneyModelBuilderExtensions
{
    /// <summary>
    /// The default precision. Twelve integral digits and four decimals covers every ISO 4217 currency
    /// exactly, including the four-decimal CLF and UYW.
    /// </summary>
    /// <remarks>
    /// EF's own default for <see cref="decimal"/> on SQL Server is <c>decimal(18,2)</c>, which silently
    /// truncates the three-decimal dinars and every four-decimal unit. Raise <c>scale</c> further for
    /// registered currencies with finer precision — Bitcoin needs 8 — or if you store unrounded
    /// intermediate results rather than canonical amounts.
    /// </remarks>
    public const int DefaultPrecision = 19;

    /// <inheritdoc cref="DefaultPrecision" />
    public const int DefaultScale = 4;

    /// <summary>Maps a <see cref="Money"/> property to an amount column and a currency column.</summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="entity">The entity being configured.</param>
    /// <param name="propertyExpression">The <see cref="Money"/> property to map.</param>
    /// <param name="precision">Total digits stored for the amount.</param>
    /// <param name="scale">Decimal digits stored for the amount. Amounts finer than this are truncated by the database.</param>
    /// <returns><paramref name="entity"/>, for chaining.</returns>
    /// <example>
    /// <code>
    /// modelBuilder.Entity&lt;Product&gt;().HasMoney(p =&gt; p.Price);
    /// // columns: Price_Amount decimal(19,4), Price_Currency char(3)
    /// </code>
    /// </example>
    /// <exception cref="ArgumentNullException"><paramref name="entity"/> or <paramref name="propertyExpression"/> is <see langword="null"/>.</exception>
    public static EntityTypeBuilder<TEntity> HasMoney<TEntity>(
        this EntityTypeBuilder<TEntity> entity,
        Expression<Func<TEntity, Money>> propertyExpression,
        int precision = DefaultPrecision,
        int scale = DefaultScale)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(propertyExpression);

        entity.ComplexProperty(propertyExpression, money => money.ConfigureMoney(precision, scale));

        return entity;
    }

    /// <summary>Maps an optional <see cref="Money"/> property to an amount column and a currency column.</summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="entity">The entity being configured.</param>
    /// <param name="propertyExpression">The nullable <see cref="Money"/> property to map.</param>
    /// <param name="precision">Total digits stored for the amount.</param>
    /// <param name="scale">Decimal digits stored for the amount.</param>
    /// <returns><paramref name="entity"/>, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// An absent amount is stored as <see langword="null"/> in <em>both</em> columns, so "no price" is
    /// distinguishable from "zero in an unspecified currency" — which is what
    /// <c>default(Money)</c> would otherwise persist as.
    /// </para>
    /// <para>
    /// Note that a nullable complex property makes the amount column nullable too, so a
    /// <c>SUM</c> over it follows SQL's usual null-skipping rules rather than treating absence as zero.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// modelBuilder.Entity&lt;Product&gt;().HasMoney(p =&gt; p.Discount);
    /// // columns: Discount_Amount decimal(19,4) NULL, Discount_Currency char(3) NULL
    /// </code>
    /// </example>
    /// <exception cref="ArgumentNullException"><paramref name="entity"/> or <paramref name="propertyExpression"/> is <see langword="null"/>.</exception>
    [SuppressMessage(
        "ApiDesign",
        "RS0026:Do not add multiple overloads with optional parameters",
        Justification = "Both overloads ship in the same version, so no compiled caller can rebind. " +
                        "They are distinguished by the lambda's return type, which overload resolution " +
                        "settles unambiguously, and naming the nullable one differently would make the " +
                        "obvious call for an optional price fail to compile.")]
    public static EntityTypeBuilder<TEntity> HasMoney<TEntity>(
        this EntityTypeBuilder<TEntity> entity,
        Expression<Func<TEntity, Money?>> propertyExpression,
        int precision = DefaultPrecision,
        int scale = DefaultScale)
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(propertyExpression);

        entity.ComplexProperty(propertyExpression, money =>
        {
            money.IsRequired(false);
            money.ConfigureMoney(precision, scale);
        });

        return entity;
    }

    /// <summary>Configures the amount and currency of a <see cref="Money"/> complex property.</summary>
    /// <param name="money">The complex property being configured.</param>
    /// <param name="precision">Total digits stored for the amount.</param>
    /// <param name="scale">Decimal digits stored for the amount.</param>
    /// <returns><paramref name="money"/>, for chaining.</returns>
    /// <remarks>Use this directly when you need to override column names or nullability.</remarks>
    /// <exception cref="ArgumentNullException"><paramref name="money"/> is <see langword="null"/>.</exception>
    public static ComplexPropertyBuilder<Money> ConfigureMoney(
        this ComplexPropertyBuilder<Money> money,
        int precision = DefaultPrecision,
        int scale = DefaultScale)
    {
        ArgumentNullException.ThrowIfNull(money);

        money.Property(m => m.Amount).HasPrecision(precision, scale);
        money.Property(m => m.Currency).HasCurrencyConversion();

        return money;
    }

    /// <summary>Stores a <see cref="Currency"/> property as a fixed three-character ISO code.</summary>
    /// <param name="property">The property being configured.</param>
    /// <returns><paramref name="property"/>, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="property"/> is <see langword="null"/>.</exception>
    public static PropertyBuilder<Currency> HasCurrencyConversion(this PropertyBuilder<Currency> property)
    {
        ArgumentNullException.ThrowIfNull(property);

        return property
            .HasConversion<CurrencyValueConverter, CurrencyValueComparer>()
            .HasMaxLength(3)
            .IsFixedLength()
            .IsUnicode(false);
    }

    /// <summary>Stores a <see cref="Currency"/> inside a complex type as a fixed three-character ISO code.</summary>
    /// <param name="property">The property being configured.</param>
    /// <returns><paramref name="property"/>, for chaining.</returns>
    /// <remarks>
    /// EF exposes complex-type members through their own builder type, so this mirrors
    /// <see cref="HasCurrencyConversion(PropertyBuilder{Currency})"/> for that context.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="property"/> is <see langword="null"/>.</exception>
    public static ComplexTypePropertyBuilder<Currency> HasCurrencyConversion(
        this ComplexTypePropertyBuilder<Currency> property)
    {
        ArgumentNullException.ThrowIfNull(property);

        return property
            .HasConversion<CurrencyValueConverter, CurrencyValueComparer>()
            .HasMaxLength(3)
            .IsFixedLength()
            .IsUnicode(false);
    }

    /// <summary>Stores a <see cref="Money"/> property in a single text column, as <c>100.50 DKK</c>.</summary>
    /// <param name="property">The property being configured.</param>
    /// <returns><paramref name="property"/>, for chaining.</returns>
    /// <remarks>
    /// The database cannot aggregate, order or index this. Prefer <c>HasMoney</c> unless
    /// the column is genuinely write-and-read-whole.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="property"/> is <see langword="null"/>.</exception>
    public static PropertyBuilder<Money> HasMoneyConversion(this PropertyBuilder<Money> property)
    {
        ArgumentNullException.ThrowIfNull(property);

        return property
            .HasConversion<MoneyValueConverter, MoneyValueComparer>()
            // 30 digits of decimal, a sign, a point, a space and three letters, with room to spare.
            .HasMaxLength(48)
            .IsUnicode(false);
    }

    /// <summary>
    /// Registers model-wide conventions so that every <see cref="Money"/> and <see cref="Currency"/>
    /// property maps without per-property configuration.
    /// </summary>
    /// <param name="configurationBuilder">The convention builder, from <c>DbContext.ConfigureConventions</c>.</param>
    /// <returns><paramref name="configurationBuilder"/>, for chaining.</returns>
    /// <remarks>
    /// This gives <see cref="Money"/> the single-column text mapping. Properties that should be split
    /// across two columns still opt in with <c>HasMoney</c>, which takes precedence.
    /// </remarks>
    /// <example>
    /// <code>
    /// protected override void ConfigureConventions(ModelConfigurationBuilder builder) =>
    ///     builder.ConfigureMoneyConventions();
    /// </code>
    /// </example>
    /// <exception cref="ArgumentNullException"><paramref name="configurationBuilder"/> is <see langword="null"/>.</exception>
    public static ModelConfigurationBuilder ConfigureMoneyConventions(this ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        configurationBuilder.Properties<Money>()
            .HaveConversion<MoneyValueConverter, MoneyValueComparer>()
            .HaveMaxLength(48)
            .AreUnicode(false);

        configurationBuilder.Properties<Currency>()
            .HaveConversion<CurrencyValueConverter, CurrencyValueComparer>()
            .HaveMaxLength(3)
            .AreFixedLength()
            .AreUnicode(false);

        return configurationBuilder;
    }
}
