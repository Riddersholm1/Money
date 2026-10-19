using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Riddersholm.Money.EntityFrameworkCore.Tests;

/// <summary>
/// <see cref="MoneyPrecisionInterceptor"/>: an amount its column would round is refused at save time.
/// </summary>
/// <remarks>
/// SQLite stores decimals as text, so these tests could never observe the rounding itself — SQL Server
/// and PostgreSQL are where <c>decimal(19,4)</c> turns 13.40123 into 13.4012 without a word. What can be
/// observed everywhere is that the guard refuses before anything is sent.
/// </remarks>
public sealed class PrecisionGuardTests
{
    [Fact]
    public void An_amount_finer_than_its_column_is_refused_when_added()
    {
        (TwoColumnContext context, SqliteConnection connection) = Create<TwoColumnContext>(o => new TwoColumnContext(o));

        using (connection)
        using (context)
        {
            context.Products.Add(new Product { Name = "fx", Price = new Money(13.40123m, Currency.EUR) });

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => context.SaveChanges());

            Assert.Contains("Product.Price", error.Message, StringComparison.Ordinal);
            Assert.Contains("13.40123 EUR", error.Message, StringComparison.Ordinal);
            Assert.Contains("5 decimal places", error.Message, StringComparison.Ordinal);

            context.ChangeTracker.Clear();
            Assert.Equal(0, context.Products.Count());
        }
    }

    [Fact]
    public void An_amount_that_fits_is_saved_and_trailing_zeros_do_not_count()
    {
        (TwoColumnContext context, SqliteConnection connection) = Create<TwoColumnContext>(o => new TwoColumnContext(o));

        using (connection)
        using (context)
        {
            context.Products.AddRange(
                new Product { Name = "a", Price = new Money(13.4012m, Currency.EUR) },
                new Product { Name = "b", Price = new Money(13.401200000m, Currency.EUR) },
                new Product { Name = "c", Price = new Money(13.40123m, Currency.EUR).Round() });

            Assert.Equal(3, context.SaveChanges());
        }
    }

    [Fact]
    public void A_modified_amount_is_checked_too()
    {
        (TwoColumnContext context, SqliteConnection connection) = Create<TwoColumnContext>(o => new TwoColumnContext(o));

        using (connection)
        using (context)
        {
            context.Products.Add(new Product { Name = "a", Price = new Money(10m, Currency.DKK) });
            context.SaveChanges();

            Product product = context.Products.Single();
            product.Price = new Money(10m, Currency.DKK) / 3;

            Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
        }
    }

    [Fact]
    public async Task The_asynchronous_save_is_checked_too()
    {
        (TwoColumnContext context, SqliteConnection connection) = Create<TwoColumnContext>(o => new TwoColumnContext(o));

        await using (connection)
        await using (context)
        {
            context.Products.Add(new Product { Name = "a", Price = new Money(1m / 3m, Currency.DKK) });

            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync(TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public void An_optional_amount_is_checked_when_present_and_skipped_when_absent()
    {
        (TwoColumnContext context, SqliteConnection connection) = Create<TwoColumnContext>(o => new TwoColumnContext(o));

        using (connection)
        using (context)
        {
            context.Products.Add(new Product { Name = "none", Price = new Money(1m, Currency.DKK), Discount = null });
            Assert.Equal(1, context.SaveChanges());

            context.Products.Add(new Product { Name = "fine", Price = new Money(1m, Currency.DKK), Discount = new Money(0.123456m, Currency.DKK) });

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
            Assert.Contains("Product.Discount", error.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void An_amount_with_more_integer_digits_than_the_column_is_refused()
    {
        // decimal(19,4) leaves fifteen integer digits; SQL Server would fail with an arithmetic
        // overflow, SQLite would store it, and the guard says which property and why.
        (TwoColumnContext context, SqliteConnection connection) = Create<TwoColumnContext>(o => new TwoColumnContext(o));

        using (connection)
        using (context)
        {
            context.Products.Add(new Product { Name = "big", Price = new Money(1_000_000_000_000_000m, Currency.DKK) });

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
            Assert.Contains("decimal(19,4)", error.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_wider_mapping_accepts_what_it_declares()
    {
        (HighPrecisionContext context, SqliteConnection connection) = Create<HighPrecisionContext>(o => new HighPrecisionContext(o));

        using (connection)
        using (context)
        {
            context.Products.Add(new Product { Name = "sat", Price = new Money(0.12345678m, Currency.FromCode("XBT")) });
            Assert.Equal(1, context.SaveChanges());

            context.Products.Add(new Product { Name = "too fine", Price = new Money(0.123456789m, Currency.FromCode("XBT")) });
            Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
        }
    }

    [Fact]
    public void Money_nested_inside_another_complex_type_is_checked()
    {
        (InvoiceContext context, SqliteConnection connection) = Create<InvoiceContext>(o => new InvoiceContext(o));

        using (connection)
        using (context)
        {
            context.Invoices.Add(new Invoice { Totals = new InvoiceTotals { Net = new Money(100m, Currency.DKK), Vat = new Money(25m, Currency.DKK) } });
            Assert.Equal(1, context.SaveChanges());

            context.Invoices.Add(new Invoice { Totals = new InvoiceTotals { Net = new Money(100m, Currency.DKK), Vat = new Money(25.000001m, Currency.DKK) } });

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
            Assert.Contains("Invoice.Totals.Vat", error.Message, StringComparison.Ordinal);
        }
    }

    private static (TContext Context, SqliteConnection Connection) Create<TContext>(Func<DbContextOptions<TContext>, TContext> factory)
        where TContext : DbContext
    {
        SqliteConnection connection = new("Filename=:memory:");
        connection.Open();

        DbContextOptions<TContext> options = new DbContextOptionsBuilder<TContext>()
            .UseSqlite(connection)
            .UseMoneyPrecisionGuard()
            .Options;

        TContext context = factory(options);
        context.Database.EnsureCreated();

        return (context, connection);
    }
}

public sealed class Invoice
{
    public int Id { get; set; }

    public InvoiceTotals Totals { get; set; } = new();
}

public sealed class InvoiceTotals
{
    public Money Net { get; set; }

    public Money Vat { get; set; }
}

/// <summary>Money two levels deep: entity, then an ordinary complex type, then Money.</summary>
public sealed class InvoiceContext(DbContextOptions<InvoiceContext> options) : DbContext(options)
{
    public DbSet<Invoice> Invoices => Set<Invoice>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<Invoice>().ComplexProperty(i => i.Totals, totals =>
        {
            totals.ComplexProperty(t => t.Net, money => money.ConfigureMoney());
            totals.ComplexProperty(t => t.Vat, money => money.ConfigureMoney());
        });
    }
}
