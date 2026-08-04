# Riddersholm.Money

A money library for .NET 10, designed from scratch around C# 14 and the .NET 10 type system.

[![CI](https://github.com/Riddersholm1/Money/actions/workflows/ci.yml/badge.svg)](https://github.com/Riddersholm1/Money/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/Riddersholm.Money.svg)](https://www.nuget.org/packages/Riddersholm.Money)
[![Downloads](https://img.shields.io/nuget/dt/Riddersholm.Money)](https://www.nuget.org/packages/Riddersholm.Money)
[![Latest release](https://img.shields.io/github/v/release/Riddersholm1/Money?include_prereleases)](https://github.com/Riddersholm1/Money/releases)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

```csharp
var price = new Money(100m, Currency.DKK);

price + new Money(50m, Currency.DKK);   // 150.00 DKK
price * 3;                              // 300.00 DKK
price + new Money(50m, Currency.EUR);   // throws CurrencyMismatchException

new Money(10m, Currency.DKK).Allocate(3);
// 3.34 DKK, 3.33 DKK, 3.33 DKK — never loses a øre
```

```sh
dotnet add package Riddersholm.Money
```

Targets `net10.0`. No dependencies. Trimming- and NativeAOT-clean, and CI proves it by publishing a
native binary with zero IL warnings and running it.

---

## What makes it different

### Amounts are exact

`new Money(100.005m, Currency.DKK)` keeps `100.005`. Multiplication and division never round.

Whether VAT rounds per line or per invoice is a domain question with legal consequences, so the
library refuses to decide it for you. Round when you mean to — usually when persisting or displaying:

```csharp
var subtotal = unitPrice * quantity * taxRate;   // exact, no error accumulated
var payable  = subtotal.Round();                 // your decision, stated once
```

`IsCanonical` tells you whether an amount is actually representable in its currency.

### Splitting money never loses any

```csharp
new Money(10m, Currency.DKK).Allocate(3);        // 3.34, 3.33, 3.33
new Money(100m, Currency.DKK).Allocate([70, 30]); // 70.00, 30.00
```

Division cannot do this: `10 / 3` is `3.333…`, and three of those rounded come to `9.99`. Allocation
distributes the indivisible remainder instead, so the parts always sum to exactly the whole. It is
property-tested over hundreds of random amounts, counts, and ratios, including negatives.

### Minor units are modelled properly

Most libraries assume every currency has 100 minor units. ISO 4217 disagrees:

```csharp
Currency.JPY.DecimalDigits;         // 0
Currency.KWD.DecimalDigits;         // 3
Currency.MRU.MinorUnitsPerMajor;    // 5 — the khoum is a *fifth* of an ouguiya
Currency.XXX.HasMinorUnit;          // false

new Money(1.37m, Currency.MRU).Round();  // 1.4 MRU, not 1.37
```

Cash precision is tracked separately from accounting precision, because small coins get withdrawn:

```csharp
new Money(12.34m, Currency.CHF).Round();        // 12.34 — the ledger
new Money(12.34m, Currency.CHF).RoundToCash();  // 12.35 — the till
new Money(12.30m, Currency.DKK).RoundToCash();  // 12.50 — Denmark has no coin below 50 øre
```

### Formatting uses the currency's precision, not the culture's

The BCL always takes the digit count from the culture, so `decimal.ToString("C", enUS)` renders 1234
yen as `¥1,234.00` — two decimal places yen does not have. Money knows its own precision:

```csharp
new Money(1234m, Currency.JPY).ToString("C", enUS);     // ¥1,234
new Money(1.234m, Currency.KWD).ToString("C", enUS);    // KWD1.234
new Money(100.5m, Currency.DKK).ToString("C", daDK);    // 100,50 kr.
```

### Parsing never guesses a currency

`kr` is DKK, NOK, SEK **and** ISK. `$` covers a dozen currencies. So a symbol is only resolved against
a culture you supply, where exactly one answer is possible:

```csharp
Money.Parse("100.50 DKK", CultureInfo.InvariantCulture);  // unambiguous, always works
Money.Parse("100,50 kr.", new CultureInfo("da-DK"));      // 100.50 DKK
Money.TryParse("100 kr.", CultureInfo.InvariantCulture, out _);  // false — and rightly so
```

### Comparison does the right thing in both directions

```csharp
price > budget                       // throws if the currencies differ — that's a bug in your code
moneyList.OrderBy(m => m)            // works on mixed currencies: orders by currency, then amount
dkk100 == eur100                     // false — a correct answer, not an exception
```

`CompareTo` never throws, because it is what `List.Sort` calls and a comparer that throws corrupts the
sort rather than merely failing. The operators do throw, because `if (price > budget)` across two
currencies is a mistake worth catching where it happens.

### Unknown currencies survive

`Currency` packs the three letters of the ISO code into an integer, so the code *is* the value. A
currency loaded from a database round-trips byte for byte even if this library has never heard of it:

```csharp
var future = Currency.FromCode("QQQ");
future.Code;      // "QQQ"
future.IsKnown;   // false — no metadata, but the value is intact
```

`Round()` refuses to act on one, because rounding to a guessed precision is how money goes missing.
`Round(2)` works on anything.

---

## Also in the box

**JSON** — works with no configuration:

```csharp
JsonSerializer.Serialize(new Money(100.50m, Currency.DKK));
// {"amount":100.50,"currency":"DKK"}
```

Two alternative shapes via `options.AddMoney(...)`: a quoted amount for JavaScript consumers whose only
number type is a double, and a compact `"100.50 DKK"` string. Reading accepts all three whatever you
write, so changing the format never breaks stored documents. `MoneyJsonSerializerContext` gives the
reflection-free path for AOT.

**Entity Framework Core** — `dotnet add package Riddersholm.Money.EntityFrameworkCore`:

```csharp
modelBuilder.Entity<Product>().HasMoney(p => p.Price);
// Price_Amount decimal(19,4), Price_Currency char(3)

context.Products.Sum(p => p.Price.Amount);              // translated to SQL
context.Products.Where(p => p.Price.Currency == Currency.DKK);
```

The default `decimal(19,4)` covers every ISO currency exactly; EF's own default of `(18,2)` silently
truncates the three-decimal dinars. A single-column text mapping is available model-wide via
`ConfigureMoneyConventions()`.

**Your own currencies** — `dotnet add package Riddersholm.Money.Generators`:

```xml
<AdditionalFiles Include="my-currencies.json" RiddersholmCurrencies="true" />
```

```csharp
Currency.XBT.DecimalDigits;   // 8 — yours, on the built-in type
```

C# 14 static extension members put your currencies on `Currency` itself, with metadata registered from
a module initializer: no reflection, no runtime registry code, trim- and AOT-safe.

**Currency conversion** — deliberately explicit, never implicit:

```csharp
var rate = new ExchangeRate(Currency.DKK, Currency.EUR, 0.134m);
rate.Convert(price).Round();   // 13.40 EUR
```

Fetching rates stays an application concern; where the number came from and how stale it may be are
auditing questions, not value-object questions.

---

## Performance

Nothing in the arithmetic path allocates, and parsing allocates nothing at all.

| | Riddersholm.Money | NodaMoney 2.7.0 |
|---|---:|---:|
| Add | 4.6 ns / 0 B | 21.6 ns / 0 B |
| Multiply | 2.5 ns / 0 B | 30.6 ns / 0 B |
| Parse | 57 ns / **0 B** | 165 ns / 168 B |
| Format | 86 ns / 48 B | 163 ns / 392 B |
| Allocate across 64 | 903 ns | 2744 ns |
| Allocate into a span | **148 ns / 0 B** | — |

[docs/performance.md](docs/performance.md) has the full tables, the method, and the one row where this
library is *slower* — along with why that trade was made on purpose.

---

## Documentation

| | |
|---|---|
| [Architecture](docs/architecture.md) | How the pieces fit, and why `Currency` is four bytes |
| [Design decisions](docs/design-decisions.md) | Every contested choice, with the argument against it |
| [Formatting](docs/formatting.md) | All six format specifiers |
| [Parsing](docs/parsing.md) | What is accepted, and what is refused on purpose |
| [Allocation](docs/allocation.md) | The algorithms and their guarantees |
| [Currency data](docs/currency-data.md) | Which currencies ship, and where the data comes from |
| [Performance](docs/performance.md) | Measurements, and what is deliberately not optimised |

## Building

```sh
dotnet build                                   # requires the .NET 10 SDK
dotnet test
dotnet publish tests/Riddersholm.Money.AotTests -c Release -r linux-x64   # the AOT gate
dotnet run -c Release --project bench/Riddersholm.Money.Benchmarks
```

## Contributing

Bug reports and pull requests are welcome — see [CONTRIBUTING.md](CONTRIBUTING.md) for what a good
change looks like here, and [SECURITY.md](SECURITY.md) for reporting anything that could lose money or
exhaust memory from untrusted input.

Before proposing an API, it's worth reading [docs/design-decisions.md](docs/design-decisions.md):
several obvious-looking features are absent on purpose, and each entry records the argument *against*
the decision as well as for it.

## License

MIT — see [LICENSE](LICENSE).
