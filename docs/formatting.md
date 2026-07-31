# Formatting

`Money` implements `IFormattable`, `ISpanFormattable`, and `IUtf8SpanFormattable`, so it works in
interpolated strings, `string.Format`, `TryFormat`, and anywhere else the BCL formats a value.

## Specifiers

| Format | Example (invariant) | Purpose |
|---|---|---|
| `G` (default) | `100.50 DKK` | Amount then ISO code. Human-readable and unambiguous. |
| `R` | `100.50 DKK` | Round-trippable. Always invariant; `Parse` reads it back exactly. |
| `C` | `kr. 100,50` (da-DK) | The culture's currency layout, with a symbol. |
| `I` | `DKK 100.50` | ISO layout, code first. |
| `N` | `1,234.50` | The number alone, with group separators. |
| `L` | `100.50 Danish Krone` | Amount and English currency name. |

A digit count may follow the letter to override the currency's precision: `C0`, `N4`, `G2`. `R` rejects
one, because a round-trip format that dropped precision would not round-trip.

```csharp
var price = new Money(1234.5m, Currency.DKK);

price.ToString();                                   // 1234.50 DKK  (current culture's number format)
price.ToString("R", CultureInfo.InvariantCulture);  // 1234.50 DKK
price.ToString("C", new CultureInfo("da-DK"));      // 1.234,50 kr.
price.ToString("I", CultureInfo.InvariantCulture);  // DKK 1234.50
price.ToString("N", new CultureInfo("en-US"));      // 1,234.50
price.ToString("C0", new CultureInfo("en-US"));     // kr1,235
```

## Precision comes from the currency

The BCL takes the digit count from the *culture*, so `decimal.ToString("C", enUS)` always produces two
decimal places whatever currency you meant:

```csharp
1234m.ToString("C", enUS);                            // $1,234.00      ← always 2
new Money(1234m, Currency.JPY).ToString("C", enUS);   // ¥1,234         ← yen has none
new Money(1.234m, Currency.KWD).ToString("C", enUS);  // KWD1.234       ← dinars have three
```

Only the digit count and the symbol come from the currency. Separators, grouping, and all sixteen of
`NumberFormatInfo`'s negative-currency patterns still come from the provider, because that is
information about the reader, not about the money.

## Symbols

`C` prefers the provider's own symbol when the provider's region actually uses that currency, and falls
back to the currency's culture-neutral symbol otherwise:

```csharp
new Money(100.5m, Currency.DKK).ToString("C", daDK);   // 100,50 kr.   ← Danish convention
new Money(100.5m, Currency.DKK).ToString("C", enUS);   // kr100.50     ← CLDR narrow form
```

Sixty-seven of the 166 currencies have no distinct symbol and use their code, which is the honest
answer rather than an invented glyph.

## Precision is never hidden

An amount carrying more precision than its currency renders all of it:

```csharp
new Money(100.005m, Currency.DKK).ToString();   // 100.005 DKK — not 100.00 DKK
```

Padding it down would be a lie about the value, and would make a non-canonical amount invisible in
exactly the logs where you need to see it. The digit count used is the larger of the currency's
precision and the amount's own, so ordinary amounts still look like money:

```csharp
new Money(100m, Currency.DKK).ToString();       // 100.00 DKK
```

Trailing zeros are ignored when choosing that count, so equal amounts always format identically —
`100m` and `100.00m` are the same money and must not render differently.

## Allocation-free formatting

Every path goes through `TryFormat` into a stack buffer, so `ToString` allocates only the string it
returns. Supply your own buffer and it allocates nothing at all:

```csharp
Span<char> buffer = stackalloc char[32];
if (price.TryFormat(buffer, out int written, "R", CultureInfo.InvariantCulture))
{
    // buffer[..written] is "1234.50 DKK"
}

Span<byte> utf8 = stackalloc byte[32];
price.TryFormat(utf8, out int bytes, "R", CultureInfo.InvariantCulture);
```

`TryFormat` returns `false` rather than truncating when the buffer is too small.

Interpolated strings take the span path automatically, so this never materialises an intermediate
string for the money:

```csharp
string line = $"Total: {price:R}";
```

The derived `NumberFormatInfo` that carries the currency's symbol and precision is cached per culture
and currency, so only the first format for a given pair pays to build it.

## Round-tripping

`R` is guaranteed: for any `Money`, in any currency, known or not,

```csharp
Money.Parse(value.ToString("R", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture) == value
```

This is property-tested across all 166 currencies. `G` and `I` also round-trip under the invariant
culture; `C` round-trips within the culture that produced it. `N` does not — it has no currency.
