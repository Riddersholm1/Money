# Changelog

All notable changes to this project are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Build

- Packages are now checked against the published 1.0.0 packages on every pack
  (`PackageValidationBaselineVersion`), so a breaking change to the public API fails CI instead of
  shipping. No change to the packages themselves.

## [1.0.0] — 2026-10-19

The first published version. From here on the public API is a semantic-versioning commitment: the
`PublicAPI.Shipped.txt` baselines record exactly what 1.0.0 contains, and removing or changing any of it
is a major version. If the release is published on a later day, update this heading's date.

The sections below fold in four rounds of adversarial review. Several behavioural changes are
deliberate: each replaces a quiet wrong answer with a loud refusal.

### Production-readiness review

Every item below returned a plausible, wrong answer before its fix, and each has a regression test in
`ProductionReadinessTests`, `ProductionReadinessGeneratorTests` or `PrecisionGuardTests`.

**Fixed**

- **JSON read `"1,5"` as fifteen.** A quoted amount was parsed with `NumberStyles.Number`, whose group
  separator turns a European one-and-a-half into 15 under the invariant culture; the compact
  `"1,5 DKK"` form, `Money` dictionary keys and the EF Core text column had the same flaw. Quoted
  amounts now accept only a sign and a decimal point, and the compact forms read with the new
  `Money.RoundTripStyles` — exactly what `ToString("R")` writes.
- **Duplicate JSON properties were resolved last-wins.** `{"amount":1,"amount":1000000}` read as a
  million, and other parsers read it as one — the ambiguity a forged payment instruction relies on.
  A repeated `amount` or `currency` is now a `JsonException`. Property names are also matched
  case-insensitively as documented, including escaped names; previously only `amount`/`Amount` were.
- **A customised culture could change how the real one formats.** The derived-format cache was keyed
  by culture *name*, which a modified clone of `da-DK` shares with `da-DK` itself, so whichever
  formatted first won: after any code formatted with the clone, every caller of the real culture got
  its separators (`1_234#50 kr.`). The cache is now keyed by the read-only `NumberFormatInfo` instance;
  a writable one is derived per call.
- **Fifth-unit and cash rounding could round the wrong way.** Fifth-unit rounding (MRU at the time)
  and cash rounding scaled the amount in `decimal` and then rounded, but the scaling itself rounds when
  an amount carries 28 significant digits: `Floor()` of `3.9999999999999999999999999999 MRU` returned
  `4.0`, above its input, and CHF cash rounding did the same. `Allocate` passed its whole-units check on
  that rounded product and allocated `4.0` from an amount that was not 4. Both paths now use exact 128-bit
  integer arithmetic (with a `BigInteger` fallback), which also removes the overflow they had near
  `decimal`'s limits.
- **A registered currency finer than 18 places overflowed when rounding** any amount above about
  8×10¹⁰. Power-of-ten rounding now takes its places from the minor-unit divisor itself.
- **`MoneyStyles.AllowLeadingWhite` and `AllowTrailingWhite` were ignored**; surrounding whitespace was
  always skipped. They are honoured now.
- **`CurrencyRegistry.Register` accepted the fallback metadata of an unknown code** (`Currency.Info` for
  a code nobody described), after which `IsKnown` reported `true` and `Round()` trusted a guessed
  precision. It is refused now.
- **The generator turned malformed currency data into wrong currencies.** Values were cast straight to
  `byte`/`short` (a cash step of 256 became 1, numeric code 70000 became 4464), a missing
  `decimalDigits` meant zero decimals, `2.7` digits became 2, integers above 2⁵³ were read through a
  `double`, and a divisor the digit count cannot express was only caught by a consumer's module
  initializer at startup. Each is now a build error, `RMG006`.
- **A line break in a currency name broke out of the generated string literal**, so the rest of the
  name compiled as code. Names and symbols are now fully escaped in both literals and doc comments.
- **Generated currencies were invisible outside the project's root namespace.** The extension members
  live in a class in that namespace, so `Currency.XBT` failed with `CS0117` in top-level statements —
  the first place a consumer of the NuGet package tries it. The generated file now carries a
  `global using static` for that one class. Found by installing the packed packages into a new app.

**Changed**

- **MRU and MGA follow ISO 4217's two decimal places.** They had been modelled in fifths — the khoum
  and the iraimbilanja — so valid amounts stepped by `0.2`, and `Round()` turned a valid `1234.56 MGA`
  from a bank into `1234.6`. ISO, and every bank, card scheme and ISO 20022 message, counts both in
  hundredths, so this library now does too. Non-power-of-ten minor units remain supported, exactly,
  for registered currencies.
- **Ten withdrawn currencies now ship** — `ANG` `BGN` `BYR` `CUC` `HRK` `MRO` `SLL` `STD` `VEF` `ZWL`,
  everything ISO withdrew from 2017 on — so ledgers holding historical amounts in them keep their
  precision. Their members are `[Obsolete]`, and the table holds 176 currencies, 166 of them in
  circulation.

**Added**

- `Currency.IsWithdrawn` and `CurrencyInfo.IsWithdrawn`, so new money can be kept out of withdrawn
  currencies. A numeric code ISO has reused resolves to the active currency (`532` is `XCG`, not `ANG`),
  and `CurrencyNumericValueConverter` refuses to store a currency whose number now identifies another,
  which would otherwise read back as a different currency.
- `Money.FromMinorUnits`, `Money.ToMinorUnits` and `Money.TryToMinorUnits` — integer minor units at the
  ISO 4217 exponent, the form payment processors and ISO 8583 exchange (`1050` for `10.50 DKK`). They
  never round; an amount finer than the currency is refused.
- `Money.RoundTripStyles`, for reading back what `ToString("R")` wrote.
- `UseMoneyPrecisionGuard()` / `MoneyPrecisionInterceptor` in the EF Core package. `decimal(19,4)`
  columns make SQL Server and PostgreSQL round `13.40123` to `13.4012` silently; with the guard,
  `SaveChanges` throws instead, naming the entity, property and amount.
- Withdrawn currencies are kept rather than deleted. The data file can mark a currency `withdrawn`
  (with `replacedBy`); its `Currency.XYZ` member becomes `[Obsolete]` and its metadata stays, so
  stored historical amounts still round. Deleting it would have been a breaking change in a routine
  data update. The runtime gives a reused numeric code (ISO reuses them) to the active currency.
- `eng/iso-4217.schema.json`, which the data file already referenced but which did not exist.
- `tools/Riddersholm.Money.DataSync --check`, and a weekly `currency-data` workflow that opens a pull
  request when ISO 4217 or CLDR changes. See
  [docs/maintaining-currency-data.md](docs/maintaining-currency-data.md).

**Build and pipeline**

- `.github/workflows/pipeline.yml` now follows the Resultify/EventBus layout exactly — build, test and
  pack on push, pull request and release; publish via Trusted Publishing on a published release — and
  keeps this repository's extra gates: Windows and macOS test runs, the NativeAOT publish-and-run gate,
  the dependency audit, and the package-content checks.
- xunit.v3 4.0.2 on Microsoft.Testing.Platform 2.x, with TRX reports and code coverage (uploaded to
  Codecov). xunit.v3 3.x and the coverage extension had been compiled against different platform
  versions, which is why the pipeline previously ran without coverage.
- Microsoft.SourceLink.GitHub 10.0.402. 10.0.301 pulled in a vulnerable `Microsoft.Build.Tasks.Git`
  (GHSA-23fw-v26w-5fgq), so the dependency-audit job failed on `main`.
- Dependabot ignores major bumps of `Microsoft.CodeAnalysis.Analyzers` as well as `.CSharp`; a grouped
  update had proposed Roslyn 5.9, which would stop the generator loading in older compilers.
- Package authorship matches the other Riddersholm packages, and the LICENSE file is packed.
- ISO 4217 data re-verified against the register and CLDR 48: no currency changes.

### Build and tooling

- **The build was broken.** `IsExternalInit` had been moved out of `System.Runtime.CompilerServices`
  into the project's own namespace — the shape a "namespace must match folder structure" rule
  suggests. The compiler resolves that type by its exact fully-qualified name and by no other means,
  so every `init` accessor and record in the netstandard2.0 generator failed with `CS0518`. Moved
  back, with the constraint written down and a file-scoped `IDE0130` suppression so it cannot be
  helpfully relocated again.
- **`Directory.Build.props` no longer sets a repository-wide `TargetFramework`.** It had been added
  two lines under the comment explaining why it must not be: the generator has to target
  `netstandard2.0`, and a repo-wide default is silently wrong for exactly that project — wrong in the
  worst way, since it would still compile and only fail when Roslyn tried to load it.
- **`Sum`, `Min`, `Max` and `Average` are classic extension methods again.** They had moved into a
  C# 14 `extension` block, which `Microsoft.CodeAnalysis.PublicApiAnalyzers` only half-models: it
  reports a new extension member as untracked (RS0016) but stays silent when a tracked one disappears
  (RS0017). A public surface whose *removals* the API tracker cannot see is not one this library can
  promise stability on. The `Aggregate`-based rewrites are also reverted to plain loops — `Sum` is the
  most-called aggregate here and `Aggregate` allocates a delegate per call.
- **`.editorconfig` and `.gitattributes` no longer disagree about line endings** (LF on both sides),
  `*.png binary` is restored so the package icon cannot be corrupted by EOL normalisation, and
  `.gitignore` regained `generated/`, `TestResults/` and `*.trx` — the first of which the csproj
  documents a build command for.
- **Unused parameters on private members are now a build error**, which is how
  `Money.Allocation.ToBigInteger`'s dead `scale` argument had gone unnoticed in the signature of an
  exactness-critical helper. The parameter is gone.
- **Benchmark operands are instance fields again, not constants.** `Add_Decimal` and `Round_Decimal`
  over two `const` values are constant-foldable, and those rows are the bare-`decimal` baseline that
  `docs/performance.md` uses to claim the currency check costs nothing measurable.
- **`IDE0051` (unused private member) is enabled in the library again**, scoped off only for the EF
  Core test fixtures whose private constructors EF calls by reflection. It had been silenced
  repository-wide with a justification describing EF entities this library does not contain.
- **The generator's invariant-culture guard was unreachable.** `SourceBuilderExtensions` offered
  `Line(FormattableString)`, which routes through `FormattableString.Invariant`, beside a plain
  `Line(string)` that does not. An interpolated string literal converts better to `string`, so all 23
  interpolated call sites bound to the culture-sensitive overload and the protection the class exists
  for did not exist. Nothing had gone wrong yet — every interpolated value is a non-negative integer,
  and .NET writes those with ASCII digits under every culture — but the first emitted `decimal` would
  have produced `-1234,5` on a Danish build agent, which is not valid C#. The members are named rather
  than overloaded now (`Line`, `LineInvariant`, `TextInvariant`), so the compiler cannot pick the wrong
  one. Generated output is byte-identical.
- **`Pow10` was implemented twice in the core assembly**, in `CurrencyInfo` and again in
  `Money.Rounding`, for the same purpose. Both callers now share one helper, so a currency validated at
  registration and rounded at runtime cannot disagree about which divisors a digit count expresses.

### Publishing

- **Releases now require a published GitHub Release, not just a tag.** Pushing a tag no longer
  publishes anything, and `workflow_dispatch` is gone from the release path. A tag that is not a
  version number fails the run rather than shipping `1.0.0` by default.
- **NuGet authentication moved to Trusted Publishing (OIDC).** There is no `NUGET_API_KEY` secret any
  more: GitHub mints a token per run, NuGet exchanges it for a key that expires in an hour, and the
  policy is bound to the repository's immutable GitHub ID. Setup is documented in
  [CONTRIBUTING.md](CONTRIBUTING.md#releasing).

### Fixed

- **`RoundToCash()` could return an amount that could not be paid.** Cash rounding used the
  currency's cash *digit count*, which only equals its increment when the minor unit is a power of ten.
  For a currency divided into fifths (MRU, as it was then modelled), `1.37` rounded to cash came back as
  `1.37` — not a whole number of fifths. Cash rounding now snaps to the coarser of the cash increment and
  the currency's own unit, and the sync tool refuses to emit a file where the two disagree.
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
- `BankingInvariantTests`, asserting across every shipped currency that rounding, cash rounding and
  allocation always produce payable amounts — the suite that caught the cash-rounding defect.
- `AllocationOracleTests`, checking allocation and rounding against an independent implementation
  written in exact `BigInteger` arithmetic — the test that caught the tie-breaking defect.
- A test pinning that `RoundToCash` floors a hand-registered currency's cash increment at that
  currency's own minor unit. With the shipped data correct, nothing else exercises that floor, so a
  registered fifth-unit currency with a too-fine cash step does.

### Riddersholm.Money — what the package contains

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
- Rounding to the currency's *increment* rather than a digit count, so a registered currency divided
  into fifths snaps to multiples of 0.2. Separate cash rounding for currencies whose small coins were withdrawn — CHF to 0.05, DKK to
  0.50, HUF to 5.
- `Currency`, a four-byte identity packing the ISO code itself, so any well-formed currency round-trips
  exactly — including codes this build has never heard of.
- 166 ISO 4217 currencies in circulation plus 10 recently withdrawn ones, generated at compile time
  from reviewable data. Metals, fund codes and units of account are deliberately excluded; `XXX` and
  `XTS` are deliberately included.
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

[Unreleased]: https://github.com/Riddersholm1/Money/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/Riddersholm1/Money/releases/tag/v1.0.0
