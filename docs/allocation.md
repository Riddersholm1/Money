# Allocation

Splitting an amount so that the parts add back up to exactly the whole.

## The problem division cannot solve

```csharp
var third = new Money(10m, Currency.DKK) / 3;   // 3.3333333333333333333333333333 DKK
third.Round() * 3;                              // 9.99 DKK
```

A øre has evaporated. Round the other way and you invent one. There is no rounding mode that fixes
this, because 10 is simply not divisible by 3 in whole øre.

Allocation distributes the indivisible remainder instead:

```csharp
new Money(10m, Currency.DKK).Allocate(3);   // 3.34, 3.33, 3.33 — sums to exactly 10.00
```

## Equal allocation

Each part gets the same number of minor units; the leftover units go to the earliest parts, one each.

```csharp
new Money(10m, Currency.DKK).Allocate(3);     // 3.34, 3.33, 3.33
new Money(100m, Currency.DKK).Allocate(4);    // 25.00 × 4
new Money(0.02m, Currency.DKK).Allocate(5);   // 0.01, 0.01, 0, 0, 0
```

Negative amounts behave symmetrically — negating an amount negates every part:

```csharp
new Money(-10m, Currency.DKK).Allocate(3);    // -3.34, -3.33, -3.33
```

Giving the extra units to the earliest parts is arbitrary but *deterministic*, which is what matters:
the same input always produces the same split, so a recalculation never disagrees with a stored result.

## Ratio allocation

```csharp
new Money(100m, Currency.DKK).Allocate([70, 30]);      // 70.00, 30.00
new Money(100m, Currency.DKK).Allocate([2, 1, 1]);     // 50.00, 25.00, 25.00
new Money(100m, Currency.DKK).Allocate([0.7m, 0.3m]);  // 70.00, 30.00
```

Ratios need not be normalised — `70:30` and `7:3` describe the same split — and zero weights are
allowed, receiving nothing but keeping their position in the result.

Where the split is not exact, remaining units go to the parts with the largest fractional shortfall —
the **largest-remainder method**, also called Hamilton's method. Ties go to the earlier position.

```csharp
new Money(0.05m, Currency.DKK).Allocate([3, 7]);   // 0.02, 0.03
```

*(5 units × 0.3 = 1.5 → 1 with 0.5 outstanding; 5 × 0.7 = 3.5 → 3 with 0.5 outstanding. Four units
assigned, one left, tie on shortfall, so it goes to index 0.)*

Negative weights throw, and weights summing to zero throw.

## Guarantees

For every allocation, whatever the amount, count, ratios, or sign:

1. **The parts sum to exactly the original amount.** No unit is lost or invented.
2. **Every part is canonical** — a whole number of the currency's minor units, so each is payable.
3. **The result is deterministic.** Same input, same output, always.
4. **Equal allocation parts differ by at most one minor unit.**
5. **Negation commutes:** `(-m).Allocate(n)` equals `m.Allocate(n)` with every part negated.

All five are property-tested over hundreds of random amounts, counts, and ratios, including negatives.
Guarantee 1 is the reason this API exists.

## What is refused

**Non-canonical amounts.**

```csharp
new Money(10.005m, Currency.DKK).Allocate(3);   // InvalidOperationException
new Money(10.005m, Currency.DKK).Round().Allocate(3);   // fine
```

No set of payable parts can sum to 10.005 DKK, so the choice is between refusing and silently rounding.
Rounding the total without being asked would break guarantee 1 in a way nobody would notice. This is
the one place where exact-by-default has an ergonomic cost, and it is deliberate: the rounding decision
becomes visible in the code.

**Currencies with no minor unit** (`XXX`, `XTS`) — there is no indivisible unit to distribute.

**Unrecognised currencies** — the minor unit is unknown, and guessing it would produce parts that may
not be payable.

## Allocation-free overloads

The array-returning overloads allocate the array. Where that matters, write into a buffer you already
own:

```csharp
Span<Money> parts = stackalloc Money[3];
new Money(10m, Currency.DKK).Allocate(parts);

Span<Money> shares = stackalloc Money[3];
new Money(100m, Currency.DKK).Allocate([50, 30, 20], shares);
```

These allocate nothing at all, and are several times faster — see
[performance.md](performance.md), where a 64-way split into a caller-owned span costs 148 ns against
903 ns for the array overload.

## Recipes

**Split a bill, giving the remainder to the payer rather than the earliest guest:**

```csharp
var parts = total.Allocate(guests);
var payerShare = parts[^1] + (total - parts.Sum());   // always zero, but explicit
```

**Distribute a discount across line items in proportion to their value:**

```csharp
int[] weights = [.. lines.Select(l => (int)(l.Total.Amount * 100))];
var discounts = discount.Allocate(weights);
```

**Split into weekly instalments with the odd unit first:**

```csharp
var instalments = loan.Allocate(52);   // instalments[0] carries any remainder
```
