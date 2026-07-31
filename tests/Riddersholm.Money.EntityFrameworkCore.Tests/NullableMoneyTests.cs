using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Riddersholm.Money.EntityFrameworkCore.Tests;

/// <summary>
/// An optional amount — <c>Money?</c> — is one of the most common shapes in a real model, and had no
/// support or coverage at all before the audit.
/// </summary>
public sealed class NullableMoneyTests
{
    [Fact]
    public void An_absent_amount_round_trips_as_null()
    {
        (TwoColumnContext context, Microsoft.Data.Sqlite.SqliteConnection connection) =
            TestDatabase.Create<TwoColumnContext>(options => new TwoColumnContext(options));

        using (connection)
        using (context)
        {
            context.Products.Add(new Product
            {
                Name = "no discount",
                Price = new Money(100m, Currency.DKK),
                Discount = null,
            });
            context.SaveChanges();
            context.ChangeTracker.Clear();

            Assert.Null(context.Products.Single().Discount);
        }
    }

    [Fact]
    public void A_present_amount_round_trips_intact()
    {
        (TwoColumnContext context, Microsoft.Data.Sqlite.SqliteConnection connection) =
            TestDatabase.Create<TwoColumnContext>(options => new TwoColumnContext(options));

        using (connection)
        using (context)
        {
            context.Products.Add(new Product
            {
                Name = "discounted",
                Price = new Money(100m, Currency.DKK),
                Discount = new Money(12.50m, Currency.DKK),
            });
            context.SaveChanges();
            context.ChangeTracker.Clear();

            Assert.Equal(new Money(12.50m, Currency.DKK), context.Products.Single().Discount);
        }
    }

    [Fact]
    public void Absent_is_distinguishable_from_a_default_money()
    {
        // The reason a nullable overload is needed at all: without it, "no discount" would persist as
        // zero in an unspecified currency and become indistinguishable from a real 0 XXX amount.
        (TwoColumnContext context, Microsoft.Data.Sqlite.SqliteConnection connection) =
            TestDatabase.Create<TwoColumnContext>(options => new TwoColumnContext(options));

        using (connection)
        using (context)
        {
            context.Products.AddRange(
                new Product { Name = "absent", Price = new Money(1m, Currency.DKK), Discount = null },
                new Product { Name = "zero", Price = new Money(1m, Currency.DKK), Discount = default(Money) });
            context.SaveChanges();
            context.ChangeTracker.Clear();

            Dictionary<string, Product> loaded = context.Products.ToDictionary(p => p.Name);

            Assert.Null(loaded["absent"].Discount);
            Assert.Equal(default(Money), loaded["zero"].Discount);
        }
    }

    [Fact]
    public void Null_amounts_can_be_filtered_in_sql()
    {
        (TwoColumnContext context, Microsoft.Data.Sqlite.SqliteConnection connection) =
            TestDatabase.Create<TwoColumnContext>(options => new TwoColumnContext(options));

        using (connection)
        using (context)
        {
            context.Products.AddRange(
                new Product { Name = "a", Price = new Money(1m, Currency.DKK), Discount = null },
                new Product { Name = "b", Price = new Money(1m, Currency.DKK), Discount = new Money(5m, Currency.DKK) });
            context.SaveChanges();
            context.ChangeTracker.Clear();

            List<string> discounted = [.. context.Products.Where(p => p.Discount != null).Select(p => p.Name)];

            Assert.Equal("b", Assert.Single(discounted));
        }
    }

    [Fact]
    public void An_optional_amount_can_be_set_and_cleared()
    {
        (TwoColumnContext context, Microsoft.Data.Sqlite.SqliteConnection connection) =
            TestDatabase.Create<TwoColumnContext>(options => new TwoColumnContext(options));

        using (connection)
        using (context)
        {
            context.Products.Add(new Product { Name = "x", Price = new Money(1m, Currency.DKK), Discount = null });
            context.SaveChanges();
            context.ChangeTracker.Clear();

            Product loaded = context.Products.Single();
            loaded.Discount = new Money(3m, Currency.EUR);
            context.SaveChanges();
            context.ChangeTracker.Clear();

            Assert.Equal(new Money(3m, Currency.EUR), context.Products.Single().Discount);

            Product again = context.Products.Single();
            again.Discount = null;
            context.SaveChanges();
            context.ChangeTracker.Clear();

            Assert.Null(context.Products.Single().Discount);
        }
    }

    [Fact]
    public void The_schema_makes_both_optional_columns_nullable()
    {
        (TwoColumnContext context, Microsoft.Data.Sqlite.SqliteConnection connection) =
            TestDatabase.Create<TwoColumnContext>(options => new TwoColumnContext(options));

        using (connection)
        using (context)
        {
            string schema = context.Database.GenerateCreateScript();

            Assert.Contains("Discount_Amount", schema, StringComparison.Ordinal);
            Assert.Contains("Discount_Currency", schema, StringComparison.Ordinal);
            // The required Price columns must stay NOT NULL.
            Assert.Contains("Price_Currency", schema, StringComparison.Ordinal);
        }
    }
}
