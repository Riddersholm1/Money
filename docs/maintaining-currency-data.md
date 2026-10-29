# Keeping currency data current

Currencies change a few times a year: a country adopts the euro (Bulgaria, 2026), a currency is
redenominated or replaced (ZWL → ZWG in 2024, ANG → XCG in 2025), coins are withdrawn and cash
rounding changes. This page is the whole routine for keeping the package in step.

## 1. Where changes come from

| Source | What it tells you | How to follow it |
|---|---|---|
| **ISO 4217 Maintenance Agency (SIX)** — [six-group.com → Data standards → Currency codes](https://www.six-group.com/en/products-services/financial-information/data-standards.html) | The authoritative register. Every change is published as a numbered *amendment* PDF, usually months before it takes effect. | Check the amendments list when the weekly workflow (below) reports a change, and quarterly regardless. |
| **[datasets/currency-codes](https://github.com/datasets/currency-codes)** | A machine-readable mirror of the ISO lists. This is what the sync tool reads. | *Watch → Custom → Releases* on GitHub. |
| **[Unicode CLDR](https://cldr.unicode.org/index/downloads)** | Display names, symbols, and cash rounding (coins withdrawn, e.g. 1 and 2 øre). | Releases twice a year, around April and October. |
| **Early warning** | Euro adoptions (EU Council decisions, ECB convergence reports), central-bank redenomination announcements. | News; these give a year or more of notice. |

You do not have to poll any of these by hand: the **Currency data** workflow
([`currency-data.yml`](../.github/workflows/currency-data.yml)) compares the shipped data with ISO and
CLDR every Monday, and when anything differs it opens a pull request from `automation/currency-data`
containing the regenerated `eng/iso-4217.json`, a one-line-per-change summary, and whether the build
and tests pass against it. Dependabot does the same for package dependencies.

## 2. When the pull request arrives

1. **Confirm each change against the ISO amendment** on six-group.com. The summary uses these markers:

   | Marker | Meaning |
   |---|---|
   | `+ XYZ added` | ISO introduced a currency. |
   | `- XYZ withdrawn 2026-01, replaced by EUR` | ISO withdrew it. It is **kept**, its `Currency.XYZ` becomes `[Obsolete]`, and its metadata stays so stored amounts still round. |
   | `~ XYZ decimalDigits: 2 -> 0` | A field changed. |
   | `! XYZ removed` | Never expected — a currency disappeared outright. Stop and investigate. |

2. **Choose the version bump** (semantic versioning; the rule is "what happens to existing callers"):

   | Change | Bump | Why |
   |---|---|---|
   | Name or symbol | patch | Display only. |
   | Currency added | minor | New member `Currency.XYZ`. |
   | Currency withdrawn | minor | The member stays; new code gets a `CS0618` warning, which breaks only builds that treat warnings as errors — call it out in the CHANGELOG. |
   | Cash rounding changed | minor | `RoundToCash()` results change for that currency. |
   | `decimalDigits` or `minorUnitsPerMajor` changed | **major** | `Round()`, `IsCanonical`, `Allocate` and minor-unit conversion change for stored amounts. |

3. **Fix the pinned counts** if a currency was added. The suite pins the number on purpose, so an
   accidental drop cannot pass. Search for the old count (today `176` in total, `166` in circulation):
   `tests/Riddersholm.Money.Tests/CurrencyDataTests.cs`, `ThreadSafetyTests.cs`,
   `tests/Riddersholm.Money.Generators.Tests/IsoCurrencyTableTests.cs`,
   `tests/Riddersholm.Money.AotTests/Program.cs`, and the counts quoted in `docs/`.
4. **Add a CHANGELOG entry** under `Unreleased` naming each currency and the ISO amendment number.
5. **Run CI on the branch** (*Actions → CI → Run workflow*, branch `automation/currency-data`) — a
   pull request opened by the workflow's own token does not start CI by itself — then merge and
   [release](../CONTRIBUTING.md#releasing).

## 3. Running it by hand

```sh
dotnet run --project tools/Riddersholm.Money.DataSync -- --check   # report only; exit 2 = changes
dotnet run --project tools/Riddersholm.Money.DataSync              # rewrite eng/iso-4217.json
dotnet test                                                        # then review the data diff
```

The data file is committed, so builds never touch the network, and every change shows up in review as
a data diff (`"decimalDigits": 2 → 0`) rather than as regenerated C#.

## 4. Judgement calls the tool cannot make

These live as short lists at the top of `tools/Riddersholm.Money.DataSync/Program.cs`:

- **New `X`-codes.** ISO's minor-unit column filters out metals and fund units automatically, but a new
  *unit of account* with a numeric minor unit (as `XAD` was) has to be added to `deny` by hand.
- **Minor units always follow ISO.** MRU and MGA are physically divided into fifths but ISO counts them
  in hundredths, and so does this library, because every counterparty does. Do not override a currency's
  minor unit away from ISO's.
- **Historic currencies.** The `historic` list seeds currencies withdrawn before they ever shipped
  (everything since 2017). Extend it only to support older ledgers, with the minor unit from ISO's
  list three — the mirror does not carry it for withdrawn rows.
- **Withdrawn currencies** are never deleted. If ISO ever reuses a withdrawn *alphabetic* code, both
  cannot coexist — that needs a deliberate decision and a major version.

## 5. Asking Claude to do it

Everything above is mechanical enough to hand over. A prompt that works:

> The currency-data workflow opened a pull request. Check every change against the ISO 4217 amendment,
> fix the pinned counts and docs, add the CHANGELOG entry, choose the version per
> docs/maintaining-currency-data.md, and get CI green.
