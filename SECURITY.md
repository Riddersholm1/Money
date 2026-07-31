# Security policy

## Supported versions

| Version | Supported |
|---|---|
| 1.0.x | ✅ |

## Reporting a vulnerability

Please report security issues privately through
[GitHub's private vulnerability reporting](https://github.com/Riddersholm1/Money/security/advisories/new)
rather than by opening a public issue.

You can expect an acknowledgement within a few days and an assessment shortly after. If the report is
accepted, you will be credited in the advisory unless you would rather not be.

## What counts as a vulnerability here

This is a value-object library with no network, file, or process access, so the realistic surface is
narrower than for most packages. Reports in these areas are especially welcome:

- **Parser robustness.** Input that causes unbounded allocation, excessive CPU time, or a crash rather
  than a clean `false` from `TryParse`.
- **Arithmetic that loses or invents money.** Any input where `Allocate` fails to preserve a total,
  or where rounding produces an amount the currency cannot represent.
- **Unbounded caches.** The library memoises derived number formats and currency metadata; a way to
  grow either without limit from untrusted input is a denial-of-service issue.
- **Deserialisation.** Any JSON or database value that produces a `Money` violating its invariants
  rather than a clean error.

## What does not count

- `OverflowException` from arithmetic that genuinely exceeds `decimal`'s range. That is the documented
  and correct behaviour — the alternative would be to invent money.
- `CurrencyMismatchException` from combining currencies. That is the library working.
- Exhausting memory by passing a deliberately enormous input to a method that is documented to allocate
  proportionally to its input.
