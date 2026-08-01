using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Riddersholm.Money.EntityFrameworkCore.Tests;

/// <summary>
/// Round trips against a real SQLite database. The in-memory provider bypasses the relational stack
/// entirely, so it would happily accept a mapping no database could actually store.
/// </summary>
public sealed class MoneyPersistenceTests
{
    [Fact]
    public void Two_column_mapping_round_trips_an_amount()
    {
        (TwoColumnContext context, Microsoft.Data.Sqlite.SqliteConnection connection) =
            TestDatabase.Create<TwoColumnContext>(options => new TwoColumnContext(options));

        using (connection)
        using (context)
        {
            context.Products.Add(new Product { Name = "Widget", Price = new Money(1234.56m, Currency.DKK) });
            context.SaveChanges();
            context.ChangeTracker.Clear();

            Product loaded = context.Products.Single();

            Assert.Equal(new Money(1234.56m, Currency.DKK), loaded.Price);
            Assert.Equal(Currency.DKK, loaded.Price.Currency);
        }
    }

    [Fact]
    public void Two_column_mapping_creates_separate_amount_and_currency_columns()
    {
        (TwoColumnContext context, Microsoft.Data.Sqlite.SqliteConnection connection) =
            TestDatabase.Create<TwoColumnContext>(options => new TwoColumnContext(options));

        using (connection)
        using (context)
        {
            string schema = context.Database.GenerateCreateScript();

            Assert.Contains("Price_Amount", schema, StringComparison.Ordinal);
            Assert.Contains("Price_Currency", schema, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Two_column_mapping_lets_the_database_aggregate_and_order()
    {
        // The reason to prefer two columns: the amount stays a real numeric column.
        (TwoColumnContext context, Microsoft.Data.Sqlite.SqliteConnection connection) =
            TestDatabase.Create<TwoColumnContext>(options => new TwoColumnContext(options));

        using (connection)
        using (context)
        {
            context.Products.AddRange(
                new Product { Name = "a", Price = new Money(10m, Currency.DKK) },
                new Product { Name = "b", Price = new Money(30m, Currency.DKK) },
                new Product { Name = "c", Price = new Money(20m, Currency.DKK) });
            context.SaveChanges();
            context.ChangeTracker.Clear();

            // Translated to SQL, not evaluated in memory.
            decimal total = context.Products.Sum(p => p.Price.Amount);
            List<string> ordered = [.. context.Products.OrderByDescending(p => p.Price.Amount).Select(p => p.Name)];
            List<string> expensive = [.. context.Products.Where(p => p.Price.Amount > 15m).Select(p => p.Name)];

            Assert.Equal(60m, total);
            Assert.Equal(["b", "c", "a"], ordered);
            Assert.Equal(2, expensive.Count);
        }
    }

    [Fact]
    public void Two_column_mapping_can_filter_by_currency()
    {
        (TwoColumnContext context, Microsoft.Data.Sqlite.SqliteConnection connection) =
            TestDatabase.Create<TwoColumnContext>(options => new TwoColumnContext(options));

        using (connection)
        using (context)
        {
            context.Products.AddRange(
                new Product { Name = "kroner", Price = new Money(10m, Currency.DKK) },
                new Product { Name = "euros", Price = new Money(10m, Currency.EUR) });
            context.SaveChanges();
            context.ChangeTracker.Clear();

            List<Product> kroner = [.. context.Products.Where(p => p.Price.Currency == Currency.DKK)];

            Assert.Equal("kroner", Assert.Single(kroner).Name);
        }
    }

    [Fact]
    public void Single_column_mapping_round_trips_by_convention()
    {
        (SingleColumnContext context, Microsoft.Data.Sqlite.SqliteConnection connection) =
            TestDatabase.Create<SingleColumnContext>(options => new SingleColumnContext(options));

        using (connection)
        using (context)
        {
            context.Ledgers.Add(new Ledger
            {
                Balance = new Money(-99.95m, Currency.EUR),
                Reporting = Currency.USD,
            });
            context.SaveChanges();
            context.ChangeTracker.Clear();

            Ledger loaded = context.Ledgers.Single();

            Assert.Equal(new Money(-99.95m, Currency.EUR), loaded.Balance);
            Assert.Equal(Currency.USD, loaded.Reporting);
        }
    }

    [Fact]
    public void Single_column_mapping_stores_a_readable_value()
    {
        (SingleColumnContext context, Microsoft.Data.Sqlite.SqliteConnection connection) =
            TestDatabase.Create<SingleColumnContext>(options => new SingleColumnContext(options));

        using (connection)
        using (context)
        {
            context.Ledgers.Add(new Ledger { Balance = new Money(1234.5m, Currency.DKK), Reporting = Currency.DKK });
            context.SaveChanges();

            using Microsoft.Data.Sqlite.SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT Balance, Reporting FROM Ledgers";

            using Microsoft.Data.Sqlite.SqliteDataReader reader = command.ExecuteReader();
            Assert.True(reader.Read());

            Assert.Equal("1234.50 DKK", reader.GetString(0));
            Assert.Equal("DKK", reader.GetString(1));
        }
    }

    [Fact]
    public void Reformatting_an_amount_is_not_treated_as_a_change()
    {
        // 100 DKK and 100.00 DKK are the same money. Comparing the *converted* strings — EF's default
        // for a converted type — would report a modification and write the row back for nothing.
        (SingleColumnContext context, Microsoft.Data.Sqlite.SqliteConnection connection) =
            TestDatabase.Create<SingleColumnContext>(options => new SingleColumnContext(options));

        using (connection)
        using (context)
        {
            context.Ledgers.Add(new Ledger { Balance = new Money(100m, Currency.DKK), Reporting = Currency.DKK });
            context.SaveChanges();
            context.ChangeTracker.Clear();

            Ledger loaded = context.Ledgers.Single();
            loaded.Balance = new Money(100.00m, Currency.DKK);

            context.ChangeTracker.DetectChanges();

            Assert.Equal(EntityState.Unchanged, context.Entry(loaded).State);
        }
    }

    [Fact]
    public void Every_currency_survives_a_round_trip()
    {
        (TwoColumnContext context, Microsoft.Data.Sqlite.SqliteConnection connection) =
            TestDatabase.Create<TwoColumnContext>(options => new TwoColumnContext(options));

        using (connection)
        using (context)
        {
            foreach (Currency currency in Currency.Known)
            {
                context.Products.Add(new Product { Name = currency.Code, Price = new Money(1234.5m, currency) });
            }

            context.SaveChanges();
            context.ChangeTracker.Clear();

            Dictionary<string, Product> loaded = context.Products.ToDictionary(p => p.Name);

            foreach (Currency currency in Currency.Known)
            {
                Assert.Equal(new Money(1234.5m, currency), loaded[currency.Code].Price);
            }
        }
    }

    [Fact]
    public void Unknown_currencies_survive_a_round_trip()
    {
        (TwoColumnContext context, Microsoft.Data.Sqlite.SqliteConnection connection) =
            TestDatabase.Create<TwoColumnContext>(options => new TwoColumnContext(options));

        using (connection)
        using (context)
        {
            context.Products.Add(new Product { Name = "mystery", Price = new Money(10m, Currency.FromCode("QQQ")) });
            context.SaveChanges();
            context.ChangeTracker.Clear();

            Product loaded = context.Products.Single();

            Assert.Equal("QQQ", loaded.Price.Currency.Code);
            Assert.False(loaded.Price.Currency.IsKnown);
        }
    }

    [Fact]
    public void Default_money_round_trips_as_the_iso_no_currency_code()
    {
        (TwoColumnContext context, Microsoft.Data.Sqlite.SqliteConnection connection) =
            TestDatabase.Create<TwoColumnContext>(options => new TwoColumnContext(options));

        using (connection)
        using (context)
        {
            context.Products.Add(new Product { Name = "unset", Price = default });
            context.SaveChanges();
            context.ChangeTracker.Clear();

            Product loaded = context.Products.Single();

            Assert.Equal(default, loaded.Price);
            Assert.Equal("XXX", loaded.Price.Currency.Code);
        }
    }

    [Fact]
    public void The_default_precision_covers_every_iso_currency()
    {
        // EF's own default for decimal is (18,2), which truncates the three-decimal dinars and the
        // four-decimal units outright.
        (TwoColumnContext context, Microsoft.Data.Sqlite.SqliteConnection connection) =
            TestDatabase.Create<TwoColumnContext>(options => new TwoColumnContext(options));

        using (connection)
        using (context)
        {
            context.Products.AddRange(
                new Product { Name = "dinar", Price = new Money(1.234m, Currency.KWD) },
                new Product { Name = "unit", Price = new Money(1.2345m, Currency.CLF) });
            context.SaveChanges();
            context.ChangeTracker.Clear();

            Dictionary<string, Product> loaded = context.Products.ToDictionary(p => p.Name);

            Assert.Equal(1.234m, loaded["dinar"].Price.Amount);
            Assert.Equal(1.2345m, loaded["unit"].Price.Amount);
        }
    }

    [Fact]
    public void Precision_can_be_raised_for_currencies_that_need_it()
    {
        (HighPrecisionContext context, Microsoft.Data.Sqlite.SqliteConnection connection) =
            TestDatabase.Create<HighPrecisionContext>(options => new HighPrecisionContext(options));

        using (connection)
        using (context)
        {
            CurrencyRegistry.Register(new CurrencyInfo("XBT", 0, "Bitcoin", "₿", 8, 100_000_000L, 8, 1));

            context.Products.Add(new Product { Name = "satoshis", Price = new Money(0.00000001m, Currency.FromCode("XBT")) });
            context.SaveChanges();
            context.ChangeTracker.Clear();

            Assert.Equal(0.00000001m, context.Products.Single().Price.Amount);
        }
    }

    [Fact]
    public void Migrations_can_be_generated_for_the_two_column_mapping()
    {
        (TwoColumnContext context, Microsoft.Data.Sqlite.SqliteConnection connection) =
            TestDatabase.Create<TwoColumnContext>(options => new TwoColumnContext(options));

        using (connection)
        using (context)
        {
            string script = context.Database.GenerateCreateScript();

            Assert.Contains("CREATE TABLE \"Products\"", script, StringComparison.Ordinal);
            Assert.Contains("Price_Currency", script, StringComparison.Ordinal);
        }
    }
}
