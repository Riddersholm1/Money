using Xunit;

// Deliberately not under this project's root namespace. Generated extension members live there, and
// before the generator emitted a project-wide static import they were invisible from here: this file
// did not compile. Consumers hit it first in top-level statements, which are in the global namespace.
#pragma warning disable IDE0130 // Namespace does not match folder structure — the mismatch is the test.
namespace Elsewhere.Entirely;
#pragma warning restore IDE0130

public sealed class OutsideTheRootNamespaceTests
{
    [Fact]
    public void Generated_currencies_are_visible_outside_the_root_namespace()
    {
        Assert.Equal(8, Riddersholm.Money.Currency.XBT.DecimalDigits);
        Assert.True(Riddersholm.Money.Currency.ETH.IsKnown);
    }

    [Fact]
    public void A_withdrawn_custom_currency_is_registered_as_withdrawn()
    {
        Riddersholm.Money.Currency legacy = Riddersholm.Money.Currency.FromCode("QLG");

        Assert.True(legacy.IsKnown);
        Assert.True(legacy.IsWithdrawn);
        Assert.False(Riddersholm.Money.Currency.XBT.IsWithdrawn);
    }
}
