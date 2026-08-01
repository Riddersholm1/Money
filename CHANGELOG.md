# Changelog

All notable changes to this project are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

Nothing yet.

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
