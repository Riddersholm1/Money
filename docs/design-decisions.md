# Design decisions

Each entry states the decision, the case against it, and why it was made anyway.

---

## Amounts are exact; nothing rounds implicitly

`new Money(100.005m, Currency.DKK)` keeps `100.005`. Multiplication and division never round.

**Against:** every `Money` should arguably be a real amount of money. Allowing 100.005 DKK — an amount
no one can pay — weakens the type's central invariant, and means persistence needs an explicit
canonicalisation step that is easy to forget.

**For:** the alternative loses data. `unitPrice * quantity * taxRate` rounds three times instead of
once, and the accumulated error is real. Worse, the library would be choosing the rounding mode, when
"per line or per invoice" and "banker's or away-from-zero" are questions with legal answers that differ
by jurisdiction. Exact is lossless: you can always go from exact to canonical, never back.

The invariant is preserved where it matters instead. `Allocate` refuses non-canonical amounts outright,
because no set of payable parts can sum to 10.005 DKK, and the error message says to `Round()` first.

---

## Relational operators throw; `CompareTo` does not

`price > budget` throws `CurrencyMismatchException` across currencies. `IComparable<Money>.CompareTo`
orders by currency then amount and never throws.

**Against:** two comparison mechanisms that disagree is surprising, and surprise is a cost.

**For:** they answer different questions. `if (price > budget)` across two currencies is a bug in the
caller's code and should say so at the point it happens. `CompareTo` is what `List.Sort`, `OrderBy`, and
`SortedSet` call, and a comparer that throws does not merely fail — it corrupts the sort, surfacing
later as `InvalidOperationException: IComparer.Compare() method returns inconsistent results`. Sorting
a mixed-currency list is a reasonable thing to want.

`TryCompareTo` exists for callers who want neither. Equality never throws in either direction:
`100 DKK == 100 EUR` is `false`.

---

## `Money * double` exists only to fail

```csharp
[Obsolete("...", error: true)]
public static Money operator *(Money left, double right);
```

**Against:** it is public API surface that can never be called, and it shows up in IntelliSense.

**For:** without it, `price * 1.1` fails with "no such operator", which tells the caller nothing.
With it, the compiler explains that `double` cannot represent `1.1` and that a rate which is
imperceptibly wrong produces an invoice that is visibly wrong. A confusing error becomes a lesson. The
overload covers `float` too, since `float` widens to `double`.

---

## `INumber<Money>` is not implemented

Implemented: `IAdditionOperators`, `ISubtractionOperators`, `IUnaryNegationOperators`,
`IUnaryPlusOperators`, `IMultiplyOperators<Money, decimal, Money>`,
`IDivisionOperators<Money, decimal, Money>`, `IDivisionOperators<Money, Money, decimal>`,
`IComparisonOperators`, `IAdditiveIdentity`.

**Against:** generic-math code could then treat `Money` like any other number.

**For:** that is precisely the problem. `INumber<T>` demands a multiplicative identity, and there is no
meaningful `Money.One`. `Money * Money` is dimensionally nonsense — DKK² is not a thing. Implementing
the interface would let generic algorithms compile and then produce garbage. The operators that *do*
make sense are implemented individually.

---

## `default(Money)` is the additive identity

`default(Money)` is zero in `Currency.None`, and adding it to any amount returns the other operand.

**Against:** it is a special case, and special cases are where bugs hide. `0 XXX + 100 DKK = 100 DKK`
breaks the otherwise absolute rule that currencies must match.

**For:** a struct can always be `default`, so pretending otherwise is not an option — the only choice
is what it means. Without the identity rule, `moneys.Sum()` and `moneys.Aggregate((a, b) => a + b)`
throw on their own seed, and every caller writes a currency-specific fold instead.

The exception is narrow and principled: only the *zero* of `Currency.None` behaves this way. A non-zero
amount in XXX still refuses to mix, because five of nothing is not five kroner.

---

## `Round()` refuses unknown currencies, but `Info` does not

Reading metadata for an unrecognised currency returns a documented fallback with `IsKnown == false`.
Calling `Round()` on one throws `UnknownCurrencyException`.

**Against:** inconsistent. Either the fallback is trustworthy or it is not.

**For:** the distinction is between reading and acting. Loading a row whose currency this build has
never seen must not crash, so metadata degrades gracefully. But *changing an amount* based on a guessed
precision silently alters money whenever the guess is wrong, and "probably two decimals" is not a good
enough reason to modify someone's ledger. `Round(2)` states the precision explicitly and works on
anything.

---

## Parsing accepts ISO codes always, symbols only with a culture

`Parse("100.50 DKK")` works anywhere. `Parse("100,50 kr.")` works only when given a culture that
identifies `kr.`

**Against:** users write `"$100"` and expect it to work.

**For:** `kr` is DKK, NOK, SEK, **and** ISK. `$` is USD, CAD, AUD, MXN, SGD, HKD, NZD and more. `£` is
GBP, EGP, SYP. There is no correct answer without knowing who wrote the text, so any library that
resolves symbols unaided is guessing — and guessing wrong is worse than failing, because failing is
visible. With an explicit culture there is exactly one candidate and no guess is involved.

---

## Parsing accepts unrecognised currency codes

`Parse("100 QQQ")` succeeds and yields an unknown currency rather than failing.

**Against:** a parser that accepts `"100 AND"` as 100 of currency "AND" is very permissive, and
permissive parsers let bad data in.

**For:** it is required by round-tripping. `Money.Parse(money.ToString("R"))` has to work for a
currency loaded from a database that this build does not recognise, and that is the same property that
makes the packed representation worth having. Callers who need strictness check `IsKnown`.

---

## `ToString("C")` uses the currency's precision, not the culture's

**Against:** it diverges from `decimal.ToString("C")`, and consistency with the BCL has value.

**For:** the BCL behaviour is wrong for money. `1234m.ToString("C", enUS)` is `$1,234.00` whatever
currency was meant, so 1234 yen gains two decimal places it does not have and 1.234 dinars loses one it
does. Money knows its own precision. Separators, grouping, and all sixteen negative-currency patterns
still come from the provider — only the digit count and symbol come from the currency.

---

## Formatting never hides precision

`new Money(100.005m, Currency.DKK).ToString()` is `100.005 DKK`, not `100.00 DKK`.

**Against:** output should look like money, and money has two decimals.

**For:** an amount padded down to the currency's precision would be a lie about the value, and a
non-canonical amount would become invisible in exactly the logs where you need to see it. The digit
count is the *larger* of the currency's precision and the amount's own, so canonical amounts still look
right (`100 DKK` renders `100.00 DKK`) while unusual ones stay visible.

Trailing zeros are ignored when choosing the count, so equal amounts always format identically —
`100m` and `100.00m` are the same money and must not render differently.

---

## `Currency.Known`, not `Currency.All`

**Against:** `All` is the obvious name.

**For:** `ALL` is the Albanian lek. A property differing from a generated currency only by case is a
trap in case-insensitive languages and a CLS-compliance failure besides — the compiler flagged it, and
the data won. `Known` also pairs with the existing `IsKnown`.

---

## Get-only properties, not `init`

**Against:** `price with { Amount = 200m }` would be convenient.

**For:** it also permits `price with { Currency = Currency.EUR }`, which turns 100 kroner into 100
euros with no exchange rate — exactly the class of bug this library exists to prevent. Get-only members
make that line fail to compile while keeping everything else `record struct` provides. `WithCurrency`
exists for the narrow case of correcting mis-stored data, and says in its own documentation that it is
almost always the wrong tool.

---

## JSON lives in the core package

**Against:** the original plan had a separate `Riddersholm.Money.Json`, and small focused packages are
generally good.

**For:** `System.Text.Json` is *in* the `net10.0` shared framework. A separate package would add nothing
to isolate — no dependency avoided, no assembly saved — while adding a second NuGet to discover,
install, and keep version-aligned. Splitting packages is worth it when it buys dependency isolation;
here it buys none. EF Core is separate for exactly that reason: it is a real dependency.

---

## Currency conversion is in scope; exchange rates are not

`ExchangeRate` ships. Fetching rates does not.

**Against:** an incomplete story — users still need somewhere to get rates from.

**For:** where a rate comes from, how stale it may be, and which one applied to a given transaction are
audit questions with regulatory weight. They belong to the application, not to a value-object library.
What the library owes you is a way to convert that is impossible to do by accident and that records the
rate used.

---

## Precision is capped at 28, not ISO's 4

**Against:** this is an ISO 4217 library, and ISO tops out at four decimal places.

**For:** a satoshi is 1e-8 of a bitcoin and a wei is 1e-18 of an ether, and the generator explicitly
supports consumer-defined currencies. Capping the runtime type at the ISO limit would make those
unrepresentable for no benefit. ISO's own limit is enforced where it belongs — in the data pipeline
that produces `eng/iso-4217.json`. This was found by writing a sample that used Bitcoin and watching it
fail.

---

## `XTS` and `XXX` ship; metals and fund codes do not

**Against:** an ISO 4217 library that omits ISO 4217 codes is incomplete.

**For:** gold is not money, and neither is the IMF's unit of account. Including them would put
`Currency.XAU` in the same list as `Currency.DKK` and imply they are the same kind of thing. `XXX` is
kept because `default(Currency)` has to mean something and ISO already has a code for "no currency";
`XTS` is kept because a test-doubles currency that can never be mistaken for real money is genuinely
useful. See [currency-data.md](currency-data.md) for the exact filter.
