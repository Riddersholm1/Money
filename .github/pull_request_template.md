## What this changes

<!-- The behaviour that differs, and why it should. -->

## Why

<!-- The reasoning. The diff already says what changed; this is the part a reader needs in six months. -->

## Checklist

- [ ] A test **fails without this change** and passes with it
- [ ] `dotnet build` is clean — warnings are errors here
- [ ] `dotnet test` passes
- [ ] Public API changes are recorded in the relevant `PublicAPI.Unshipped.txt`
- [ ] Documentation updated if behaviour or guarantees changed
- [ ] `CHANGELOG.md` updated under `Unreleased`

## If this touches money semantics

- [ ] Rounding is still never implicit
- [ ] Allocation still preserves totals exactly
- [ ] No currency is inferred from an ambiguous symbol
- [ ] Benchmark numbers included, if the change is justified by performance
