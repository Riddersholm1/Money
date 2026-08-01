using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Riddersholm.Money.EntityFrameworkCore.Tests;

public sealed class Product
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public Money Price { get; set; }

    /// <summary>An optional amount — the case a money library has to get right to be usable.</summary>
    public Money? Discount { get; set; }
}

public sealed class Ledger
{
    public int Id { get; set; }

    public Money Balance { get; set; }

    public Currency Reporting { get; set; }
}

/// <summary>Maps money across two columns, so SQL can aggregate and order it.</summary>
public sealed class TwoColumnContext(DbContextOptions<TwoColumnContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<Product>().HasMoney(p => p.Price);
        modelBuilder.Entity<Product>().HasMoney(p => p.Discount);
    }
}

/// <summary>Maps money into a single text column, configured model-wide by convention.</summary>
public sealed class SingleColumnContext(DbContextOptions<SingleColumnContext> options) : DbContext(options)
{
    public DbSet<Ledger> Ledgers => Set<Ledger>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        configurationBuilder.ConfigureMoneyConventions();
    }
}

/// <summary>Stores amounts at eight decimals, as a currency such as Bitcoin requires.</summary>
public sealed class HighPrecisionContext(DbContextOptions<HighPrecisionContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<Product>().HasMoney(p => p.Price, precision: 28, scale: 8);
        modelBuilder.Entity<Product>().HasMoney(p => p.Discount, precision: 28, scale: 8);
    }
}

/// <summary>Creates a throwaway SQLite database that lives as long as its connection.</summary>
internal static class TestDatabase
{
    public static (TContext Context, SqliteConnection Connection) Create<TContext>(
        Func<DbContextOptions<TContext>, TContext> factory)
        where TContext : DbContext
    {
        SqliteConnection connection = new("Filename=:memory:");
        connection.Open();

        DbContextOptions<TContext> options = new DbContextOptionsBuilder<TContext>()
            .UseSqlite(connection)
            .Options;

        TContext context = factory(options);
        context.Database.EnsureCreated();

        return (context, connection);
    }
}
