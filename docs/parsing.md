# Parsing

`Money` implements `IParsable`, `ISpanParsable`, and `IUtf8SpanParsable`, so it works with
`Parse`/`TryParse`, with minimal-API route and query binding, and anywhere else .NET parses a value.

**The parser never guesses a currency.** Everything below follows from that.

## What is accepted

```csharp
var inv = CultureInfo.InvariantCulture;

Money.Parse("100 DKK", inv);            // 100.00 DKK
Money.Parse("DKK 100", inv);            // 100.00 DKK
Money.Parse("100.50 DKK", inv);         // 100.50 DKK
Money.Parse("100.50DKK", inv);          // 100.50 DKK — the space is optional
Money.Parse("-100.50 DKK", inv);        // -100.50 DKK
Money.Parse("(100.50) DKK", inv);       // -100.50 DKK — accounting parentheses
Money.Parse("1,234,567.89 USD", inv);   // 1234567.89 USD
```

With a culture, that culture's own symbol is understood too:

```csharp
var da = new CultureInfo("da-DK");

Money.Parse("100,50 kr.", da);   // 100.50 DKK
Money.Parse("kr. 100", da);      // 100.00 DKK
Money.Parse("$100", enUS);       // 100.00 USD
```

## What is refused, and why

```csharp
Money.TryParse("100 kr.", inv, out _);      // false
Money.TryParse("kr. 100", null, out _);     // false
Money.TryParse("$100", inv, out _);         // false
Money.TryParse("kr. 100", enUS, out _);     // false
```

`kr` is DKK, NOK, SEK, **and** ISK. `$` is USD, CAD, AUD, MXN, SGD, HKD, NZD and more. `£` is GBP, EGP,
SYP. Without knowing who wrote the text there is no correct answer, so there is no answer — a library
that guesses here will guess wrong in production, and silently.

Give it a culture and there is exactly one candidate, so no guess is involved. `en-US` knows `$` means
USD; it has nothing to say about `kr.`

Also refused:

```csharp
Money.TryParse("100", inv, out _);          // false — no currency (see RequireCurrency)
Money.TryParse("100 kroner", inv, out _);   // false — "ner" is not a code
Money.TryParse("abc", inv, out _);          // false
```

An ISO code is only recognised when it is genuinely a three-letter token: the character next to it must
not be another letter, so `"100 kroner"` does not surrender its last three letters.

## An ISO code always wins

```csharp
Money.Parse("100 DKK", enUS);   // 100.00 DKK, not USD
```

Explicit beats implicit. The text said DKK.

## The provider governs the number, always

Even when an ISO code identified the currency, the caller's culture still decides what the digits mean:

```csharp
Money.Parse("1.234,50 DKK", da);     // 1234.50 DKK — Danish separators
Money.Parse("1,234.50 DKK", enUS);   // 1234.50 DKK — American separators
```

Only *currency resolution* is restricted, because only symbols are ambiguous. Once the currency token
is dealt with, the number goes to `decimal.TryParse`, so signs, parentheses, group separators and every
culture's negative-currency pattern behave exactly as they do everywhere else in .NET.

## `MoneyStyles`

The default is `MoneyStyles.Currency`: whitespace, leading and trailing signs, parentheses, group
separators, decimals, ISO codes, symbols, and `RequireCurrency`.

```csharp
// Accept a bare number, yielding Currency.None
var styles = Money.DefaultStyles & ~MoneyStyles.RequireCurrency;
Money.TryParse("100", styles, inv, out var m);   // true; m.Currency is Currency.None

// Reject accounting parentheses
Money.TryParse("(100) DKK", Money.DefaultStyles & ~MoneyStyles.AllowParentheses, inv, out _);  // false

// ISO codes only, no symbols, whatever culture is supplied
var strict = Money.DefaultStyles & ~MoneyStyles.AllowCurrencySymbol;
```

`RequireCurrency` is on by default because money without a currency is rarely what was meant.

## Unrecognised currency codes parse

```csharp
Money.TryParse("100 QQQ", inv, out var m);   // true
m.Currency.Code;                             // "QQQ"
m.Currency.IsKnown;                          // false
```

This is required by round-tripping: text produced from a currency this build does not recognise has to
read back, and ISO adds currencies faster than libraries are rebuilt. It does make the parser
permissive — `"100 AND"` yields 100 of currency "AND".

### Requiring a currency that exists

At a trust boundary the opposite is wanted, so that a typo'd or hostile code is rejected rather than
becoming an amount that rounds to a guessed two-decimal precision. Add `RequireKnownCurrency`:

```csharp
var strict = Money.DefaultStyles | MoneyStyles.RequireKnownCurrency;

Money.TryParse("100.00 DKK", strict, inv, out _);   // true
Money.TryParse("100.00 ZZZ", strict, inv, out _);   // false — no such currency
```

The same choice exists for a bare code:

```csharp
Currency.FromCode("ZZZ");        // succeeds; IsKnown is false
Currency.FromKnownCode("ZZZ");   // throws ArgumentException
Currency.TryFromKnownCode("ZZZ", out _);   // false
```

Reading an inbound payment file wants the strict form. Loading rows this library previously wrote wants
the permissive one, or a currency added by ISO after the build would fail to load. The flag is opt-in
rather than default for exactly that reason: rejecting real currencies because they are newer than the
installed version is its own kind of wrong answer.

Note that `XXX` is a currency someone can write, not the absence of one:

```csharp
Money.TryParse("1234.5 XXX", inv, out var x);   // true, even with RequireCurrency
```

## UTF-8 and spans

```csharp
Money.Parse("100.50 DKK"u8, inv);
Money.TryParse(utf8Bytes, inv, out var value);
Money.Parse(text.AsSpan(), inv);
```

All of these allocate nothing. UTF-8 input is transcoded once onto the stack so there is a single
implementation of the parsing rules; inputs longer than 256 bytes fall back to the heap.

## Round-tripping

For any `Money`, in any currency:

```csharp
Money.Parse(value.ToString("R", inv), inv) == value
```

Property-tested across all 166 currencies. See [formatting.md](formatting.md) for which other formats
round-trip.
