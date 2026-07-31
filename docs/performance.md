# Performance

All figures below were measured with BenchmarkDotNet on .NET 10 (x64, Linux) against **NodaMoney
2.7.0**, which is the closest comparable library and happens to make the opposite choice on the one
design decision where speed and robustness pull apart. Reproduce them with:

```sh
dotnet run -c Release --project bench/Riddersholm.Money.Benchmarks
```

The arithmetic table below is from a full BenchmarkDotNet job. The remaining tables are short-job runs,
which are indicative rather than publication-grade; re-run without `--job short` before quoting them.

## Arithmetic

| Operation | Riddersholm.Money | NodaMoney | bare `decimal` |
|---|---:|---:|---:|
| Create | 0.22 ns | 26.8 ns | — |
| `Currency.FromCode("DKK")` | 2.2 ns | — | — |
| Add | 4.7 ns | 22.8 ns | 5.8 ns |
| Subtract | 4.7 ns | 27.7 ns | — |
| Multiply | 2.1 ns | 31.1 ns | — |
| Divide | 26.7 ns | 53.5 ns | — |
| Round | 8.9 ns | — | 0.09 ns |
| Equality | 2.9 ns | 1.5 ns | — |
| Read `DecimalDigits` | 2.1 ns | — | — |
| Read `Symbol` | 3.9 ns | — | — |

**Nothing here allocates.**

On addition, `Money` and a bare `decimal` measure within about a nanosecond of each other, and the
ordering between them is not stable enough to mean anything — the honest reading is that **the currency
check costs nothing measurable**, not that adding money is faster than adding a number. An earlier
version of this document reported the difference as though it were a result; it was harness noise.

Two rows do deserve comment.

**Equality is slower than NodaMoney's, and that is the design working as intended.** NodaMoney packs
the currency into the unused bits of the `decimal`'s flags word, which gets `Money` down to 16 bytes and
turns equality into a single 128-bit comparison. Ours is 24 bytes and compares two fields. The packed
trick depends on `decimal`'s internal layout, needs reinterpretation to read back, and produces a
`decimal` that is only valid as long as nothing inspects it closely. The stated priority for this
library is correctness over micro-optimisation, so 1.7 ns is the price and this table is the receipt.

**Reading currency metadata does not allocate and does not run a static constructor.** Precision comes
from a `ReadOnlySpan<byte>` over data embedded in the assembly, not from a `CurrencyInfo` object. That
matters because rounding is on everyone's hot path — materialising metadata objects to read one byte
would allocate the whole table the first time anyone rounded an amount.

## Formatting and parsing

| Operation | Riddersholm.Money | NodaMoney |
|---|---:|---:|
| `ToString("G")` | 86 ns / 48 B | 163 ns / 392 B |
| `ToString("C", da-DK)` | 125 ns / 48 B | — |
| `TryFormat(Span<char>)` | 83 ns / **0 B** | — |
| `TryFormat(Span<byte>)` | 98 ns / **0 B** | — |
| `Parse(string)` | 57 ns / **0 B** | 165 ns / 168 B |
| `Parse(ReadOnlySpan<char>)` | 61 ns / **0 B** | — |
| `Parse(ReadOnlySpan<byte>)` | 61 ns / **0 B** | — |
| `Currency.Parse("DKK")` | 2.2 ns / **0 B** | — |

Parsing allocates nothing at all, and `ToString` allocates only the string it returns — the 48 bytes
are the result, not overhead. Every formatting path goes through `TryFormat` into a stack buffer, so
supplying your own buffer removes the last allocation.

## Allocation (splitting an amount)

| Recipients | `Allocate(n)` | `Allocate(Span<Money>)` | NodaMoney `Split` |
|---|---:|---:|---:|
| 3 | 63 ns / 96 B | **22 ns / 0 B** | 235 ns / 200 B |
| 12 | 185 ns / 312 B | **36 ns / 0 B** | 638 ns / 344 B |
| 64 | 903 ns / 1560 B | **148 ns / 0 B** | 2744 ns / 1176 B |

### A worked example of measuring before optimising

The first implementation did all its arithmetic in `decimal`, chosen for magnitude safety without any
thought about cost. Benchmarks said a 64-way split took **4122 ns** — slower than NodaMoney's 2838 ns.
Profiling the shape of the code made the reason obvious: one `decimal` division per recipient to
convert minor units back into an amount, at roughly 30 ns each.

The fix was to do the split in `long` arithmetic and construct the result decimal directly from an
integer and a scale, with no division at all. That path applies whenever the minor-unit divisor is a
power of ten — every currency except MRU and MGA — and whenever the amount fits in a `long` of minor
units, which for DKK means anything under about 9×10¹⁶. The `decimal` path is still there for
everything else, so behaviour is unchanged and the property tests that assert the sum is preserved pass
untouched.

| | before | after |
|---|---:|---:|
| `Allocate(3)` | 298 ns | 63 ns |
| `Allocate(64)` | 4122 ns | 903 ns |
| `Allocate(Span<Money>)`, 64 | 2347 ns | **148 ns** |

A 4.6× improvement on the array overload and 16× on the span one. None of it was guessable in advance —
the original code looked perfectly reasonable.

## JSON

| Operation | Time | Allocated |
|---|---:|---:|
| Serialize (reflection) | 142 ns | 96 B |
| Serialize (source-generated) | 139 ns | 96 B |
| Serialize to UTF-8 | 128 ns | 64 B |
| Serialize compact (`"100.50 DKK"`) | 180 ns | 48 B |
| Deserialize | 181 ns | 32 B |
| Deserialize (source-generated) | 174 ns | 32 B |
| Deserialize from UTF-8 | 176 ns | 32 B |

The source-generated path is marginally faster and, more importantly, is the one that works under
NativeAOT and full trimming.

## What is deliberately not optimised

- **`Money` is 24 bytes, not 16.** See the equality row above.
- **Division keeps full `decimal` precision.** `100 DKK / 3` carries 28 significant digits rather than
  being truncated to the currency's precision. Truncating would be faster and would silently lose
  money.
- **`Allocate` refuses non-canonical amounts** instead of rounding them for you. The check costs a
  multiply and a comparison, and it is the difference between a split that sums correctly and one that
  quietly does not.
- **Metadata lookup for unknown currencies allocates.** `Currency.FromCode("QQQ").Info` synthesises a
  fallback each time rather than caching, because caching arbitrary caller-supplied codes is an
  unbounded-growth hazard. Known currencies — the case that matters — are cached.

## Method

Every number here came from a run before the corresponding claim was written. Where a measurement
contradicted a design assumption, the design changed and the old number was kept in the table above.
