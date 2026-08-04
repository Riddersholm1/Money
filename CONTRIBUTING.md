# Contributing

Thank you for considering a contribution. This is a money library, so the bar for correctness is
higher than for most code: a subtle rounding bug here becomes a rounding bug in someone's ledger.

## Getting set up

You need the **.NET 10 SDK** (see [`global.json`](global.json) for the pinned feature band).

```sh
dotnet build                                                              # zero warnings expected
dotnet test
dotnet publish tests/Riddersholm.Money.AotTests -c Release -r linux-x64   # the AOT gate
dotnet run -c Release --project bench/Riddersholm.Money.Benchmarks
```

The build treats warnings as errors. That is deliberate and not negotiable — several real defects in
this codebase were first surfaced by an analyzer.

## What a good change looks like

**Every behavioural change needs a test that fails before it.** Not a test that passes afterwards — one
that demonstrably fails first. Several bugs have survived in this repository precisely because a test
asserted the wrong behaviour, and writing the test first is the only reliable defence.

**Correctness beats performance, and both beat cleverness.** If a change makes something faster at the
cost of a guarantee, it will be declined. If it is faster and equally correct, bring the BenchmarkDotNet
numbers — see [`docs/performance.md`](docs/performance.md), where every published figure was measured
before the claim around it was written.

**Explain the "why" in comments, not the "what".** The code says what it does. Comments should say why
it does it that way, and especially why an obvious-looking alternative is wrong.

## Things that will be pushed back on

- Silently rounding money. Anywhere. Rounding is always the caller's explicit decision.
- Inferring a currency from an ambiguous symbol. `kr` is DKK, NOK, SEK *and* ISK.
- Public API that cannot be justified against
  [`docs/design-decisions.md`](docs/design-decisions.md). If your change contradicts a decision
  recorded there, that is fine — but argue the decision, do not route around it.
- Adding a dependency to the core package. It has none, and that is a feature.

## Public API changes

The public surface is tracked by `Microsoft.CodeAnalysis.PublicApiAnalyzers`. Adding or changing public
API fails the build until you record it in the relevant `PublicAPI.Unshipped.txt`. This is intentional:
it makes every API change visible in review rather than accidental.

## Currency data

Do not hand-edit `eng/iso-4217.json`. Regenerate it:

```sh
dotnet run --project tools/Riddersholm.Money.DataSync
```

The filter that decides which currencies ship is documented in
[`docs/currency-data.md`](docs/currency-data.md). Changing it is a design decision, not a data refresh.

## Commit messages

Explain the reasoning, not just the change. A reader six months from now needs to know why, and the
diff already tells them what.

## Releasing

Publishing to NuGet happens one way, and only one way:

1. Tag the commit — `git tag v1.2.3 && git push origin v1.2.3`.
2. **Publish a GitHub Release for that tag.** This is what triggers
   [`release.yml`](.github/workflows/release.yml); the tag alone does nothing.
3. Approve the `nuget` environment if reviewers are configured on it.

The workflow re-runs the whole gate — build, tests, the NativeAOT publish-and-execute — before it
packs anything, so a release cannot ship something CI would have rejected. The version comes from the
tag with the leading `v` stripped, and a tag that is not a version number fails the run rather than
silently publishing `1.0.0` from `Directory.Build.props`.

There is **no `NUGET_API_KEY` secret**, deliberately. The workflow authenticates with
[Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing): GitHub mints an
OIDC token for the run, NuGet exchanges it for an API key that expires in an hour, and the policy is
bound to this repository's immutable GitHub ID. A long-lived push credential in repository secrets is
readable by every workflow and every action they call, and stays valid until someone remembers to
rotate it — which is the wrong shape of risk for a package that ends up inside banking systems.

One-time setup on nuget.org, under **Account → Trusted Publishing**:

| Field | Value |
|---|---|
| Repository owner | `Riddersholm1` |
| Repository | `Money` |
| Workflow file | `release.yml` |
| Environment | `nuget` |

Then set the repository variable `NUGET_USER` to the nuget.org account name. Note that a new policy is
only *temporarily* active for seven days and becomes permanent after the first successful publish, so
create it shortly before releasing rather than months ahead.

## Reporting a security issue

Please do not open a public issue — see [SECURITY.md](SECURITY.md).
