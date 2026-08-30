using Microsoft.Data.Sqlite;

namespace Catalog.API;

/// <summary>
/// Plain ADO.NET over SQLite, on purpose -- not every bounded-context
/// service needs a full ORM. Contrast with the companion matching-engine
/// repo (a separate portfolio piece), which uses EF Core + SQL Server for
/// a service with a genuinely complex, evolving relational model.
/// Catalog's whole schema is one table; EF Core here would be ceremony
/// with no payoff. "Database per service" is the actual architectural
/// commitment -- the tooling underneath it is a separate, per-service
/// choice.
/// </summary>
public class CatalogDb(string connectionString)
{
    public void EnsureCreatedAndSeeded()
    {
        using var connection = Open();
        connection.Execute("""
            CREATE TABLE IF NOT EXISTS Products (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Price REAL NOT NULL,
                Stock INTEGER NOT NULL
            );
            """);

        var count = connection.ExecuteScalar("SELECT COUNT(*) FROM Products");
        if (Convert.ToInt64(count) > 0) return;

        connection.Execute("""
            INSERT INTO Products (Name, Price, Stock) VALUES
                ('Torque Wrench 1/2in', 42.50, 120),
                ('OBD-II Scanner Pro', 89.99, 60),
                ('Coolant Sensor Kit', 15.75, 300),
                ('Diagnostic Cable USB-C', 22.00, 200);
            """);
    }

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection;
    }
}

public record Product(int Id, string Name, decimal Price, int Stock);

public static class SqliteExtensions
{
    public static void Execute(this SqliteConnection connection, string sql, object? parameters = null)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        AddParameters(cmd, parameters);
        cmd.ExecuteNonQuery();
    }

    public static object? ExecuteScalar(this SqliteConnection connection, string sql, object? parameters = null)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        AddParameters(cmd, parameters);
        return cmd.ExecuteScalar();
    }

    private static void AddParameters(SqliteCommand cmd, object? parameters)
    {
        if (parameters is null) return;
        foreach (var prop in parameters.GetType().GetProperties())
        {
            cmd.Parameters.AddWithValue("@" + prop.Name, prop.GetValue(parameters) ?? DBNull.Value);
        }
    }
}
