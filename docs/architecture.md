# Architecture

## The shape of the thing

```
Riddersholm.Money                     net10.0, no dependencies
├── Currency          4 bytes         identity: three ISO letters packed into a uint
├── CurrencyInfo      class           metadata: name, symbol, precision, cash rounding
├── Money             24 bytes        a decimal amount and a Currency
├── ExchangeRate      24 bytes        base, quote, rate
└── Serialization/                    System.Text.Json converters (in-box, no dependency)

Riddersholm.Money.Generators          netstandard2.0 analyzer
└── CurrencyGenerator                 ISO table for this library; custom currencies for yours

Riddersholm.Money.EntityFrameworkCore net10.0
└── complex-type mapping, value converters and comparers, precision helpers
```

Three packages, and the boundaries are drawn by dependencies rather than by topic. JSON lives in the
core package because `System.Text.Json` is in the `net10.0` shared framework — a separate package would
add one to install and version in lockstep while isolating nothing. EF Core is separate because it is a
real third-party dependency that most consumers do not want.

## Currency is an identity, not a record of facts

The obvious design gives `Currency` a code, a numeric code, a symbol, a name, and a precision. That
makes the value type 40-odd bytes across several string fields, turns equality into a string
comparison, and makes every `Money` expensive to copy — for information most code never reads.

So `Currency` holds one `uint`. Each of the three letters occupies five bits holding `1..26`:

```
  bits 0-4    bits 5-9    bits 10-14
   'D'=4        'K'=11       'K'=11
```

Equality and hashing are one integer comparison. Nothing allocates. And because zero is unreachable
from any real code, it is given to `XXX` — ISO's own "no currency" — which makes `default(Currency)`,
`Currency.None`, and `Currency.XXX` a single value rather than three things that mostly agree.

Metadata lives in `CurrencyInfo`, reached through `currency.Info`. For convenience `Currency` forwards
the common properties, so `Currency.DKK.Symbol` still reads naturally; the metadata simply isn't
stored in the struct. This mirrors how .NET separates a culture *name* from `CultureInfo`.

### Why this beats an index into a table

An index would be two bytes instead of four. It would also mean that a currency code the library does
not recognise cannot be represented at all — an unrecognised code would have to throw on the way in or
collapse to a sentinel, and either way data that was perfectly good in the database is gone.

Because the code *is* the value here, an unfamiliar currency round-trips exactly:

```csharp
var future = Currency.FromCode("QQQ");   // ISO adds this next year; this build predates it
future.Code;                             // "QQQ"
future.IsKnown;                          // false — no metadata, but nothing was lost
```

The same property is what makes custom currencies need no coordination: a consumer's `XBT` occupies
its own value automatically, with no shared index space to allocate from and no runtime registry to
consult before the value is usable.

## Two tiers of metadata, for a reason

Reading a currency's precision is on the hot path — `Round()` needs it, and everyone rounds. So the
generator emits two different things:

**Scalar metadata** becomes `ReadOnlySpan<byte>` built from constant collection expressions, which the
compiler lays out as a blob in the assembly. Reading `DecimalDigits` is an array index into static
data: no allocation, no static constructor, ~2 ns.

**Descriptive metadata** — names, symbols — becomes a `switch` returning string literals, and
`CurrencyInfo` objects are materialised lazily and cached on first request. A program that only moves
money around never allocates a single one.

The lookup is split in two to make this work: a sparse `switch` maps the packed code to a dense
*ordinal*, and everything else is indexed by that ordinal.

## Money is 24 bytes, on purpose

`decimal` (16) + `Currency` (4) + padding = 24.

NodaMoney gets to 16 by hiding the currency in the unused bits of the `decimal`'s flags word. It works,
and it is measurably faster for equality — see [performance.md](performance.md), which records the
1.7 ns this costs us. It also depends on `decimal`'s internal layout, needs reinterpretation to read
back, and yields a `decimal` that is only valid so long as nothing looks at it closely.

The stated priority is correctness over micro-optimisation. Twenty-four bytes still fits in registers
on the paths that matter, still never allocates, and does not depend on a runtime implementation
detail.

## What the source generator is actually for

Generating 166 ISO currencies barely justifies a Roslyn component. The list changes about once a year;
a checked-in `.g.cs` would serve, and would be easier to debug.

The generator earns its place by giving a *consumer's* currencies the same treatment. C# 14 static
extension members let another assembly add `Currency.XBT` to a type it does not own:

```csharp
public static class CryptoCurrencies
{
    extension(Currency)
    {
        public static Currency XBT => Values.XBT;
    }

    [ModuleInitializer]
    internal static void Register() => CurrencyRegistry.Register(/* ... */);
}
```

Registration runs from a module initializer: deterministic, reflection-free, and safe under trimming
and NativeAOT. No `Type.GetType`, no assembly scanning, nothing that a trimmer has to be told about.

The library then dogfoods its own generator on `eng/iso-4217.json`, so the path consumers take is the
path that is exercised on every build.

## Data flows one way

```
ISO 4217 register + Unicode CLDR
        │  tools/Riddersholm.Money.DataSync   (run by hand, when ISO amends)
        ▼
eng/iso-4217.json                             (committed — reviewable as a data diff)
        │  Riddersholm.Money.Generators       (every build)
        ▼
Currency constants + CurrencyTable            (in-memory; no checked-in generated code)
```

Committing the JSON rather than fetching it during the build makes builds offline and reproducible, and
turns an ISO amendment into a reviewable data diff — `"decimalDigits": 2 → 0` is visible in a way that
a regenerated blob of C# is not.

## Where the exceptions live

| | |
|---|---|
| `CurrencyMismatchException` | combining amounts in different currencies |
| `UnknownCurrencyException` | needing a precision the library does not have |
| `FormatException` | unparseable text (the BCL type, so existing `catch` blocks work) |

Equality is deliberately not in that list: `100 DKK == 100 EUR` is `false`, because "are these the same
amount of money?" has a correct answer.

## Threading

Every public type is an immutable value or an immutable class. The only mutable state in the library is
`CurrencyRegistry`, which replaces a `FrozenDictionary` atomically rather than mutating one, so lookups
are lock-free and never observe a half-built table.
