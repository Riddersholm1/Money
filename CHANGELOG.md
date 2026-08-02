# Changelog

All notable changes to this project are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

A correctness pass aimed at use inside banking software, where a silently wrong answer is worse than a
crash. Two of the fixes below change behaviour deliberately: each replaces a quiet wrong answer with a
loud refusal.

### Fixed

- **`RoundToCash()` returned an amount that could not be paid, for MRU.** Cash rounding used the
  currency's cash *digit count*, which only equals its increment when the minor unit is a power of ten.
  The Mauritanian khoum is one fifth of an ouguiya, so `1.37 MRU` rounded to cash came back as
  `1.37 MRU` — not a whole number of khoums. Cash rounding now snaps to the coarser of the cash
  increment and the currency's own unit, the ISO data records MRU's real cash increment of `0.20`, and
  the sync tool refuses to emit a file where the two disagree.
- **Ratio allocation broke ties in the wrong direction.** The largest-remainder method promises that
  equal shortfalls go to the earlier position, but the shortfalls were compared as `decimal`s computed
  through a division that rounds to 28 significant digits, so a genuine tie could be ordered
  arbitrarily. Splitting 757,197 JPY across nineteen weights gave the spare yen to recipient 17 instead
  of recipient 2. The total was always exact and no part was ever off by more than one unit, so only
  the identity of the recipient was wrong — which is enough to stop a second implementation of the same
  rule from reconciling. Shortfalls are now computed in `Int128`, falling back to `BigInteger` for
  large or fractional weights. As a side effect the exact version is 2-3× faster.
- **`default(ExchangeRate)` silently zeroed every amount it converted.** The constructor rejects a rate
  of zero, but the struct's default carries one, and `Convert` multiplied by it. `Convert`,
  `ConvertBack` and `Invert` now throw `InvalidOperationException`; `ExchangeRate.IsSpecified` reports
  the state. A same-currency rate other than `1` is also rejected now.
- **`CurrencyNumericValueConverter` wrote `0` for a currency with no ISO numeric code**, storing a row
  that identified nothing and only failing on a later read. It now throws at the point of the mistake.

### Changed

- **JSON `null` is no longer read as a zero amount.** `Money` and `Currency` deserialised `null` to
  `default` — zero in `XXX`. An absent amount is not zero, and the two must not be the same value, so
  both now throw `JsonException`. Declare the property as `Money?` or `Currency?` when absence is
  legitimate; those still read `null` as `null`.

### Added

- `MoneyStyles.RequireKnownCurrency`, `Currency.FromKnownCode` and `Currency.TryFromKnownCode`, for
  validating input at a trust boundary. The default stays permissive so that codes newer than the
  installed build still round-trip.
- `ExchangeRate.IsSpecified`.
- A `NuGet.config` pinning a single package source with package source mapping. Without it, a machine
  with more than one remote feed — the normal state at a bank — fails restore with `NU1507` on every
  project, because central package management will not guess between unmapped sources and the
  repository treats warnings as errors. This is why the solution reported central-package-management
  errors when opened in Visual Studio while CI stayed green.
- Vulnerability-audit findings (`NU1900`-`NU1904`) now warn locally and fail in a dedicated CI job,
  rather than breaking every developer's build the day an advisory is published against a transitive
  dependency.
- `BankingInvariantTests`, asserting across all 166 currencies that rounding, cash rounding and
  allocation always produce payable amounts — the suite that caught the MRU defect.
- `AllocationOracleTests`, checking allocation and rounding against an independent implementation
  written in exact `BigInteger` arithmetic — the test that caught the tie-breaking defect.

## [1.0.0-rc.1] — 2026-08-01

First public release candidate. The API is complete and the implementation is what 1.0.0 is intended
to be; the `rc` marks that no one outside the project has used it yet. It will be promoted to 1.0.0
unchanged if nothing surfaces.

### Riddersholm.Money

- `Money`, an immutable `readonly record struct` of a `decimal` amount and a `Currency`, 24 bytes and
  allocation-free.
- **Exact amounts.** Construction, multiplication and division never round; rounding is always an
  explicit call, because whether VAT rounds per line or per invoice is a domain decision with legal
  consequences.
- Complete arithmetic with generic-math interfaces. `INumber<Money>` is deliberately not implemented —
  there is no meaningful `Money.One` and `Money * Money` is dimensionally nonsense. `Money * double`
  exists only as a compile-time error explaining why binary floating point has no place here.
- Relational operators throw across currencies; `IComparable.CompareTo` does not, because it is what
  `List.Sort` calls and a comparer that throws corrupts the sort rather than merely failing.
- **Sum-preserving allocation.** `Allocate` splits an amount so the parts always add back to exactly
  the whole, by count or by weight, with span overloads that allocate nothing.
- Rounding to the currency's *increment* rather than a digit count, so MRU and MGA snap to multiples of
  0.2. Separate cash rounding for currencies whose small coins were withdrawn — CHF to 0.05, DKK to
  0.50, HUF to 5.
- `Currency`, a four-byte identity packing the ISO code itself, so any well-formed currency round-trips
  exactly — including codes this build has never heard of.
- 166 ISO 4217 currencies, generated at compile time from reviewable data. Metals, fund codes and
  units of account are deliberately excluded; `XXX` and `XTS` are deliberately included.
- Formatting through `ISpanFormattable` and `IUtf8SpanFormattable`, using the *currency's* precision
  rather than the culture's — the BCL renders 1234 yen as `¥1,234.00`, which is two decimal places yen
  does not have.
- Parsing through `IParsable`, `ISpanParsable` and `IUtf8SpanParsable`, allocation-free. ISO codes are
  always accepted; symbols only against an explicit culture, because `kr` is DKK, NOK, SEK *and* ISK.
- `System.Text.Json` support with no configuration, three write formats, and a source-generated context
  for NativeAOT.
- `ExchangeRate` for conversion that is possible but never accidental.
- Verified NativeAOT- and trim-clean by a published, executed binary in CI.

### Riddersholm.Money.Generators

- Incremental source generator emitting the ISO table for this library, and — via C# 14 static
  extension members — a consumer's own currencies as members of `Currency`, registered from a module
  initializer with no reflection.

### Riddersholm.Money.EntityFrameworkCore

- Two-column complex-type mapping, so `SUM`, `ORDER BY` and range predicates translate to SQL, with
  `decimal(19,4)` by default rather than EF's `(18,2)`, which truncates the three-decimal dinars.
- Nullable `Money?` support, a single-column text mapping by convention, and value comparers that treat
  `100 DKK` and `100.00 DKK` as the same money rather than as a modification.

[Unreleased]: https://github.com/Riddersholm1/Money/compare/v1.0.0-rc.1...HEAD
[1.0.0-rc.1]: https://github.com/Riddersholm1/Money/releases/tag/v1.0.0-rc.1
