namespace Riddersholm.Money;

/// <content>
/// Arithmetic. Results are exact: nothing here rounds, so a chain such as
/// <c>unitPrice * quantity * taxRate</c> accumulates no error before you decide where rounding belongs.
/// </content>
public readonly partial record struct Money
{
    /// <summary>Adds two amounts in the same currency.</summary>
    /// <param name="left">The first amount.</param>
    /// <param name="right">The second amount.</param>
    /// <returns>The sum, in the shared currency.</returns>
    /// <exception cref="CurrencyMismatchException">
    /// The currencies differ and neither operand is the additive identity.
    /// </exception>
    /// <exception cref="OverflowException">The result is outside the range of <see cref="decimal"/>.</exception>
    public static Money operator +(Money left, Money right)
    {
        if (left.Currency == right.Currency)
        {
            return new Money(left.Amount + right.Amount, left.Currency);
        }

        // Zero in Currency.None is the additive identity, so a default seed folds cleanly. A
        // *non-zero* amount in XXX is still a real mismatch and falls through to the throw.
        if (left.IsAdditiveIdentity)
        {
            return right;
        }

        if (right.IsAdditiveIdentity)
        {
            return left;
        }

        throw new CurrencyMismatchException(left.Currency, right.Currency);
    }

    /// <summary>Subtracts one amount from another in the same currency.</summary>
    /// <param name="left">The amount to subtract from.</param>
    /// <param name="right">The amount to subtract.</param>
    /// <returns>The difference, which may be negative.</returns>
    /// <exception cref="CurrencyMismatchException">
    /// The currencies differ and neither operand is the additive identity.
    /// </exception>
    /// <exception cref="OverflowException">The result is outside the range of <see cref="decimal"/>.</exception>
    public static Money operator -(Money left, Money right)
    {
        if (left.Currency == right.Currency)
        {
            return new Money(left.Amount - right.Amount, left.Currency);
        }

        if (left.IsAdditiveIdentity)
        {
            return -right;
        }

        if (right.IsAdditiveIdentity)
        {
            return left;
        }

        throw new CurrencyMismatchException(left.Currency, right.Currency);
    }

    /// <summary>Negates an amount.</summary>
    /// <param name="value">The amount to negate.</param>
    public static Money operator -(Money value) => new(-value.Amount, value.Currency);

    /// <summary>Returns the amount unchanged.</summary>
    /// <param name="value">The amount.</param>
    public static Money operator +(Money value) => value;

    /// <summary>Scales an amount. The result is exact and is not rounded to the currency's precision.</summary>
    /// <param name="left">The amount.</param>
    /// <param name="right">The factor.</param>
    public static Money operator *(Money left, decimal right) => new(left.Amount * right, left.Currency);

    /// <inheritdoc cref="op_Multiply(Money, decimal)" />
    public static Money operator *(decimal left, Money right) => new(left * right.Amount, right.Currency);

    /// <inheritdoc cref="op_Multiply(Money, decimal)" />
    public static Money operator *(Money left, int right) => new(left.Amount * right, left.Currency);

    /// <inheritdoc cref="op_Multiply(Money, decimal)" />
    public static Money operator *(int left, Money right) => new(left * right.Amount, right.Currency);

    /// <inheritdoc cref="op_Multiply(Money, decimal)" />
    public static Money operator *(Money left, long right) => new(left.Amount * right, left.Currency);

    /// <inheritdoc cref="op_Multiply(Money, decimal)" />
    public static Money operator *(long left, Money right) => new(left * right.Amount, right.Currency);

    /// <summary>Not supported: binary floating point has no place in a monetary calculation.</summary>
    /// <param name="left">The amount.</param>
    /// <param name="right">The factor.</param>
    /// <remarks>
    /// This overload exists only so that <c>price * 1.1</c> fails with an explanation instead of a bare
    /// "no such operator". <c>double</c> cannot represent <c>1.1</c>, and a rate that is imperceptibly
    /// wrong produces an invoice that is visibly wrong. Convert deliberately: <c>price * 1.1m</c>, or
    /// <c>price * (decimal)rate</c> if the value genuinely arrives as a <c>double</c>.
    /// </remarks>
    [Obsolete("Multiplying money by double or float is not supported: binary floating point cannot represent decimal rates exactly. Use a decimal factor (1.1m) or convert explicitly ((decimal)rate).", error: true)]
    public static Money operator *(Money left, double right) =>
        throw new NotSupportedException("Multiplying money by a binary floating-point number is not supported.");

    /// <inheritdoc cref="op_Multiply(Money, double)" />
    [Obsolete("Multiplying money by double or float is not supported: binary floating point cannot represent decimal rates exactly. Use a decimal factor (1.1m) or convert explicitly ((decimal)rate).", error: true)]
    public static Money operator *(double left, Money right) =>
        throw new NotSupportedException("Multiplying money by a binary floating-point number is not supported.");

    /// <summary>Divides an amount by a number. The result is exact and is not rounded.</summary>
    /// <param name="left">The amount.</param>
    /// <param name="right">The divisor.</param>
    /// <remarks>
    /// The result keeps <see cref="decimal"/>'s full precision, so <c>100 DKK / 3</c> is
    /// <c>33.333333333333333333333333333 DKK</c> rather than a rounded third. To split an amount
    /// between recipients without losing anything, use <see cref="Allocate(int)"/> instead.
    /// </remarks>
    /// <exception cref="DivideByZeroException"><paramref name="right"/> is zero.</exception>
    public static Money operator /(Money left, decimal right) => new(left.Amount / right, left.Currency);

    /// <inheritdoc cref="op_Division(Money, decimal)" />
    public static Money operator /(Money left, int right) => new(left.Amount / right, left.Currency);

    /// <inheritdoc cref="op_Division(Money, decimal)" />
    public static Money operator /(Money left, long right) => new(left.Amount / right, left.Currency);

    /// <summary>Not supported: binary floating point has no place in a monetary calculation.</summary>
    /// <param name="left">The amount.</param>
    /// <param name="right">The divisor.</param>
    /// <remarks>See <see cref="op_Multiply(Money, double)"/>.</remarks>
    [Obsolete("Dividing money by double or float is not supported: binary floating point cannot represent decimal rates exactly. Use a decimal divisor (3m) or convert explicitly ((decimal)rate).", error: true)]
    public static Money operator /(Money left, double right) =>
        throw new NotSupportedException("Dividing money by a binary floating-point number is not supported.");

    /// <summary>Divides one amount by another, giving the dimensionless ratio between them.</summary>
    /// <param name="left">The numerator.</param>
    /// <param name="right">The denominator.</param>
    /// <returns>How many times <paramref name="right"/> goes into <paramref name="left"/>.</returns>
    /// <exception cref="CurrencyMismatchException">The currencies differ.</exception>
    /// <exception cref="DivideByZeroException"><paramref name="right"/> is zero.</exception>
    public static decimal operator /(Money left, Money right) =>
        left.Currency == right.Currency
            ? left.Amount / right.Amount
            : throw new CurrencyMismatchException(left.Currency, right.Currency);

    /// <summary>Adds two amounts in the same currency.</summary>
    /// <param name="other">The amount to add.</param>
    /// <exception cref="CurrencyMismatchException">The currencies differ.</exception>
    public Money Add(Money other) => this + other;

    /// <summary>Subtracts an amount in the same currency.</summary>
    /// <param name="other">The amount to subtract.</param>
    /// <exception cref="CurrencyMismatchException">The currencies differ.</exception>
    public Money Subtract(Money other) => this - other;

    /// <summary>Scales the amount. The result is exact and is not rounded.</summary>
    /// <param name="factor">The factor.</param>
    public Money Multiply(decimal factor) => this * factor;

    /// <summary>Divides the amount. The result is exact and is not rounded.</summary>
    /// <param name="divisor">The divisor.</param>
    /// <exception cref="DivideByZeroException"><paramref name="divisor"/> is zero.</exception>
    public Money Divide(decimal divisor) => this / divisor;

    /// <summary>Divides by another amount in the same currency, giving their ratio.</summary>
    /// <param name="other">The denominator.</param>
    /// <exception cref="CurrencyMismatchException">The currencies differ.</exception>
    /// <exception cref="DivideByZeroException"><paramref name="other"/> is zero.</exception>
    public decimal DivideBy(Money other) => this / other;
}
