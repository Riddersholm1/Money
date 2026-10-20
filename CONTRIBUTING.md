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

Publishing to NuGet happens one way, and only one way — the same as for Riddersholm.Resultify and
Riddersholm.EventBus:

1. Set `VersionPrefix`/`VersionSuffix` in [`Directory.Build.props`](Directory.Build.props) to the version
   you are releasing, give its `CHANGELOG.md` heading today's date, and merge that to `main`.
2. Tag the merge commit — `git tag v1.2.3 && git push origin v1.2.3`.
3. **Publish a GitHub Release for that tag.** This is what triggers the publish job in
   [`pipeline.yml`](.github/workflows/pipeline.yml); the tag alone does nothing. Tick *Set as a
   pre-release* for an `-rc`/`-beta` version.
4. Once a stable release is on nuget.org, raise `PackageValidationBaselineVersion` in
   [`src/Directory.Build.props`](src/Directory.Build.props) to it (see below).

The release run repeats the whole gate — build, tests on Linux, Windows and macOS, the NativeAOT
publish-and-execute, the dependency audit — before anything is pushed, so a release cannot ship
something CI would have rejected. The version comes from the tag with the leading `v` stripped; a tag
that is not a version number fails the run, and one that disagrees with `Directory.Build.props` gets a
warning. All three packages are pushed with `--skip-duplicate`, so re-running a half-finished release
is safe.

There is **no `NUGET_API_KEY` secret**, deliberately. The workflow authenticates with
[Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing): GitHub mints an
OIDC token for the run, NuGet exchanges it for an API key that expires in an hour, and the policy is
bound to this repository's immutable GitHub ID. A long-lived push credential in repository secrets is
readable by every workflow and every action they call, and stays valid until someone remembers to
rotate it — which is the wrong shape of risk for a package that ends up inside banking systems.

One-time setup on nuget.org, under **your profile → Trusted Publishing → Create**, mirroring the
existing `Resultify-GitHubActions` and `EventBus-GitHubActions` policies:

| Field | Value |
|---|---|
| Policy name | `Money-GitHubActions` |
| Repository owner | `Riddersholm1` |
| Repository | `Money` |
| Workflow file | `pipeline.yml` |
| Environment | *(leave empty)* |

Then add a repository **variable** (Settings → Secrets and variables → Actions → *Variables*) named
**`NUGET_USER`** holding your nuget.org *profile name* — not the email address. The publish job checks
for it before it tries to log in, so a missing value fails with a plain message rather than an OIDC
error.

Note the activation rule: a new policy is only **temporarily active for seven days** and becomes
permanent after the first successful publish. Create it shortly before releasing, not months ahead.

### Breaking-change check

Every `dotnet pack` downloads the release named by `PackageValidationBaselineVersion` in
[`src/Directory.Build.props`](src/Directory.Build.props) (today `1.0.0`) and compares the new package
with it, so removing or changing a published member fails CI with `CP0002` and similar errors, naming
the member. This checks the compiled packages against what is actually on nuget.org; the
`PublicAPI.Shipped.txt` files check the source against what you declared.

A major release is the one place to accept a break. Pack once with
`-p:ApiCompatGenerateSuppressionFile=true`, which writes the accepted differences to a
`CompatibilitySuppressions.xml` file next to the project, and commit that file with the change. After
the major release ships, raise the baseline to it and delete the file.

### Optional repository settings

- **`CODECOV_TOKEN`** secret — enables the coverage upload. The step never fails the build without it.
- **Settings → Actions → General → "Allow GitHub Actions to create and approve pull requests"** — lets
  the weekly [currency-data workflow](.github/workflows/currency-data.yml) open its pull request. Without
  it, the workflow reports the same change in an issue.

### Keeping currency data current

See [docs/maintaining-currency-data.md](docs/maintaining-currency-data.md).

## Reporting a security issue

Please do not open a public issue — see [SECURITY.md](SECURITY.md).
