# Currency data

`eng/iso-4217.json` is the single source of truth for every currency the library knows about. It is
**generated and committed**, then consumed at build time by `Riddersholm.Money.Generators`.

Committing the data rather than fetching it during the build means:

- builds are offline and reproducible — no network dependency, no drift between machines;
- an ISO amendment shows up in review as a **data diff** (`"decimalDigits": 2 → 0`) instead of a
  regenerated blob of C#, so a wrong number is visible rather than buried.

Regenerate with:

```sh
dotnet run --project tools/Riddersholm.Money.DataSync
```

## Sources

| Data | Source |
|---|---|
| Alphabetic code, numeric code, minor unit, withdrawal date | ISO 4217 register (lists one & three) via [`datasets/currency-codes`](https://github.com/datasets/currency-codes) |
| Decimal digits, cash digits, cash rounding | [CLDR `supplemental/currencyData.json`](https://github.com/unicode-org/cldr-json) |
| English display name, symbol | CLDR `cldr-numbers-full/main/en/currencies.json` |

The ISO maintenance agency (SIX Group) publishes the authoritative XML, but it is not reachable from
every build environment; the `datasets/currency-codes` mirror carries the same fields.

## Which currencies are included

**166 currencies**: active circulating money, plus `XTS` and `XXX`.

The inclusion rule is data-driven rather than a hand-maintained list:

1. **Active only.** Rows with a `WithdrawalDate` are historic and are dropped.
2. **Numeric minor unit.** ISO writes `-` in the minor-unit column for everything that is not money.
   That single field cleanly excludes precious metals (`XAU` `XAG` `XPT` `XPD`), the SDR (`XDR`), the
   Sucre (`XSU`), the ADB unit (`XUA`), and the European bond-market units (`XBA`–`XBD`).
3. **Minus `XAD`.** The Arab Accounting Dinar is a unit of account that *does* carry a numeric minor
   unit, so rule 2 alone would let it through.
4. **Plus `XTS` and `XXX`.** Both are marked `-` by ISO and would be dropped, but they are structurally
   required — see below.

This correctly keeps the five `X`-prefixed codes that really are circulating money: `XAF` (CFA franc
BEAC), `XOF` (CFA franc BCEAO), `XPF` (CFP franc), `XCD` (East Caribbean dollar), and `XCG` (Caribbean
guilder).

### Why `XXX` and `XTS` are kept

`XXX` is ISO's own code for "no currency", and it is what `default(Currency)` decodes to. Because the
packed representation reserves `0` for `XXX`, `Currency.None`, `Currency.XXX`, and `default(Currency)`
are all the same value — one representation of "no currency" instead of two, and `default(Money)`
round-trips through JSON and the database as a real ISO code rather than an empty string.

`XTS` is reserved by ISO for testing, which makes it the right currency for test doubles and fixtures
that must never be mistaken for real money.

### Judgement calls

`CLF` (Chilean Unidad de Fomento) and `UYW` (Uruguayan Unidad Previsional) are the only 4-decimal
entries. They are inflation-indexed units rather than banknotes, but ISO scopes them to a country and
Chilean contracts are routinely denominated in UF, so they are included.

## Minor units are not always powers of ten

Two currencies have a minor unit of **one fifth**, not one hundredth:

| Currency | ISO digits | Minor units per major | Consequence |
|---|---|---|---|
| `MRU` Mauritanian ouguiya | 2 | **5** (khoums) | valid amounts step by `0.2` |
| `MGA` Malagasy ariary | 2 | **5** (iraimbilanja) | valid amounts step by `0.2` |

ISO records `MinorUnit = 2` for both, which is true about the *digit count* but wrong about the
*increment*. `MinorUnitsPerMajor` therefore comes from an explicit override in the sync tool, and
`Money.Round()` snaps to the increment rather than simply truncating to N decimals. Rounding
`1.37 MRU` gives `1.40 MRU`, not `1.37 MRU`.

`XTS` and `XXX` have **no** minor unit. Their `MinorUnitsPerMajor` is `0`, `HasMinorUnit` is `false`,
and `Round()` is a no-op that preserves full decimal precision — you cannot round to an increment that
does not exist. Rounding to an explicit digit count still works.

## Cash rounding is separate from accounting precision

Several currencies are accounted to two decimals but cannot be *paid* that precisely, because the small
coins were withdrawn. CLDR models this with `cashDigits` and a `cashRounding` step counted in last-place
units:

| Currency | Accounting | Cash | Meaning |
|---|---|---|---|
| `CHF` | 2 digits | step 5 | cash rounds to `0.05` |
| `DKK` | 2 digits | step 50 | cash rounds to `0.50` |
| `HUF` | 2 digits | 0 digits, step 5 | cash rounds to `5` forint |
| `NOK`, `SEK`, `CZK` | 2 digits | 0 digits | cash rounds to whole units |

`Money.Round()` uses the accounting precision; `Money.RoundToCash()` uses the cash precision. Ledgers
want the former, tills want the latter, and conflating them produces off-by-a-few-øre errors that are
tedious to find.

### Cash can be coarser than the minor unit, never finer

CLDR describes cash precision on the assumption that the minor unit is a power of ten. For MRU and MGA
it is not — the khoum and the iraimbilanja are one **fifth** of the major unit — and the assumption
breaks: CLDR gives MRU two cash digits, which is an increment of `0.01`, while the smallest amount that
exists is `0.2`.

Taken literally that made `RoundToCash()` return amounts nobody can hold: `1.37 MRU` rounded to cash
came back as `1.37 MRU`, which `IsCanonical` correctly reports as false. The sync tool now raises any
cash increment finer than the currency's own unit up to that unit, so MRU is recorded with a step of
`20` at two digits — an increment of `0.20` — and refuses to emit a file where the two disagree.
`Money.RoundToCash()` applies the same floor at runtime, so a hand-registered currency cannot
reintroduce the problem, and `BankingInvariantTests` asserts across all 166 currencies that a
cash-rounded amount is always payable.

## Symbols

`CurrencyInfo.Symbol` is the CLDR **narrow** symbol for `en` (`kr`, `$`, `¥`), falling back to the wide
symbol and then to the code itself — 67 of the 166 currencies have no distinct symbol and use their
code, which is the honest answer.

Symbols are **not** unique: `kr` is DKK, NOK, SEK, *and* ISK; `$` covers a dozen currencies. That is why
parsing never infers a currency from a symbol without an explicit culture — see
[parsing.md](parsing.md).

For localised output, `ToString("C", provider)` prefers the provider's own `NumberFormatInfo.CurrencySymbol`
when the provider's region uses that currency, so `da-DK` renders `kr.` as Danes expect, and falls back
to `CurrencyInfo.Symbol` otherwise.
