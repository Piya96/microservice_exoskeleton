using Microsoft.Data.Sqlite;

namespace Ordering.API;

/// <summary>
/// Two tables, one clear split: Orders is Ordering's own transactional
/// data. CatalogProjection is a deliberately narrow, denormalized local
/// copy of just the Catalog fields Ordering actually needs (Name, Price)
/// -- kept current by subscribing to Catalog's ProductPriceChangedIntegrationEvent
/// (see EventHandlers/ProductPriceChangedIntegrationEventHandler.cs) and
/// backfilled on a cache miss via a resilient synchronous call (see
/// Services/CatalogServiceClient.cs). This is the "replicate the data you
/// need instead of querying across the boundary" pattern applied for real,
/// not just described -- see the README for why order-placement still
/// falls back to a synchronous call on a cold cache rather than refusing
/// the order outright.
/// </summary>
public class OrderingDb(string connectionString)
{
    public void EnsureCreated()
    {
        using var connection = Open();
        connection.Execute("""
            CREATE TABLE IF NOT EXISTS Orders (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ProductId INTEGER NOT NULL,
                ProductName TEXT NOT NULL,
                Quantity INTEGER NOT NULL,
                UnitPrice REAL NOT NULL,
                Status TEXT NOT NULL,
                CreatedUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS CatalogProjection (
                ProductId INTEGER PRIMARY KEY,
                ProductName TEXT NOT NULL,
                Price REAL NOT NULL,
                UpdatedUtc TEXT NOT NULL
            );
            """);
    }

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection;
    }
}

public record Order(int Id, int ProductId, string ProductName, int Quantity, decimal UnitPrice, string Status, DateTime CreatedUtc)
{
    public decimal TotalPrice => UnitPrice * Quantity;
}

public record CatalogProjectionEntry(int ProductId, string ProductName, decimal Price);
