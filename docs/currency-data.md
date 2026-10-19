# Currency data

`eng/iso-4217.json` is the single source of truth for every currency the library knows about. It is
**generated and committed**, then consumed at build time by `Riddersholm.Money.Generators`.

Committing the data rather than fetching it during the build means:

- builds are offline and reproducible — no network dependency, no drift between machines;
- an ISO amendment shows up in review as a **data diff** (`"decimalDigits": 2 → 0`) instead of a
  regenerated blob of C#, so a wrong number is visible rather than buried.

Regenerate with:

```sh
dotnet run --project tools/Riddersholm.Money.DataSync              # rewrite the file
dotnet run --project tools/Riddersholm.Money.DataSync -- --check   # report only; exit 2 on change
```

A weekly workflow runs the check and opens a pull request when ISO or CLDR changes — see
[maintaining-currency-data.md](maintaining-currency-data.md) for the routine. The file's format is
described by [`eng/iso-4217.schema.json`](../eng/iso-4217.schema.json), which also applies to the files
consumers point the generator at.

## Sources

| Data | Source |
|---|---|
| Alphabetic code, numeric code, minor unit, withdrawal date | ISO 4217 register (lists one & three) via [`datasets/currency-codes`](https://github.com/datasets/currency-codes) |
| Decimal digits, cash digits, cash rounding | [CLDR `supplemental/currencyData.json`](https://github.com/unicode-org/cldr-json) |
| English display name, symbol | CLDR `cldr-numbers-full/main/en/currencies.json` |

The ISO maintenance agency (SIX Group) publishes the authoritative XML, but it is not reachable from
every build environment; the `datasets/currency-codes` mirror carries the same fields.

## Which currencies are included

**176 currencies**: 166 in circulation (active money plus `XTS` and `XXX`), and 10 that ISO has
withdrawn since 2017, kept for historical amounts.

The inclusion rule is data-driven rather than a hand-maintained list:

1. **Active, plus recent history.** Rows with a `WithdrawalDate` are historic and are dropped —
   except the currencies withdrawn from 2017 on (`ANG` `BGN` `BYR` `CUC` `HRK` `MRO` `SLL` `STD`
   `VEF` `ZWL`), which ledgers still hold. They are marked `"withdrawn"`; see rule 5.
2. **Numeric minor unit.** ISO writes `-` in the minor-unit column for everything that is not money.
   That single field cleanly excludes precious metals (`XAU` `XAG` `XPT` `XPD`), the SDR (`XDR`), the
   Sucre (`XSU`), the ADB unit (`XUA`), and the European bond-market units (`XBA`–`XBD`).
3. **Minus `XAD`.** The Arab Accounting Dinar is a unit of account that *does* carry a numeric minor
   unit, so rule 2 alone would let it through.
4. **Plus `XTS` and `XXX`.** Both are marked `-` by ISO and would be dropped, but they are structurally
   required — see below.
5. **Withdrawn currencies stay.** A currency that has shipped is never removed when ISO withdraws it.
   It is marked `"withdrawn": "YYYY-MM"` (and `"replacedBy"` where the register makes the successor
   unambiguous), its `Currency.XYZ` member becomes `[Obsolete]`, and its metadata stays, so ledgers
   full of historical amounts still round correctly. Deleting it would remove a public member — a
   breaking change in what should be a routine data update. `Currency.IsWithdrawn` reports it, so an
   application can keep withdrawn currencies out of new payments and currency pickers. When ISO gives
   a withdrawn currency's numeric code to its successor (`ANG` and `XCG` are both `532`), a numeric
   lookup returns the active one.

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

## Minor units follow ISO 4217

`DecimalDigits` is ISO's minor unit, and `MinorUnitsPerMajor` is ten to that power for every shipped
currency: `100` for DKK, `1000` for KWD, `1` for JPY.

That includes `MRU` (Mauritanian ouguiya) and `MGA` (Malagasy ariary). Both are *physically* divided
into fifths — the khoum and the iraimbilanja — and an earlier version modelled them that way, so that
valid amounts stepped by `0.2`. That was the wrong call for a finance library: ISO 4217 records two
decimal places for both, and every bank, card scheme and ISO 20022 message counts them in hundredths.
Modelling fifths made `Round()` turn a perfectly valid `1234.56 MGA` from a bank into `1234.6`. They now
follow ISO like every other currency.

Non-power-of-ten minor units are still supported for currencies you register yourself — five minor
units per major makes amounts step by `0.2` — and rounding and allocation handle them exactly.

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

CLDR describes cash precision independently of ISO's minor unit, and the two can disagree. A cash
increment finer than the currency's own unit would round to an amount nobody can hold, so the sync
tool raises any such increment to the minor unit and refuses to emit a file where the two disagree.
`Money.RoundToCash()` applies the same floor at runtime, so a currency registered by hand cannot
reintroduce the problem, and `BankingInvariantTests` asserts across every shipped currency that a
cash-rounded amount is always payable. (MRU, when it was modelled in fifths, is where this was found.)

## Symbols

`CurrencyInfo.Symbol` is the CLDR **narrow** symbol for `en` (`kr`, `$`, `¥`), falling back to the wide
symbol and then to the code itself — 67 of the 166 currencies in circulation have no distinct symbol
and use their code, which is the honest answer.

Symbols are **not** unique: `kr` is DKK, NOK, SEK, *and* ISK; `$` covers a dozen currencies. That is why
parsing never infers a currency from a symbol without an explicit culture — see
[parsing.md](parsing.md).

For localised output, `ToString("C", provider)` prefers the provider's own `NumberFormatInfo.CurrencySymbol`
when the provider's region uses that currency, so `da-DK` renders `kr.` as Danes expect, and falls back
to `CurrencyInfo.Symbol` otherwise.
