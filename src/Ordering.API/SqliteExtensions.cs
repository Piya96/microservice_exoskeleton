using Microsoft.Data.Sqlite;

namespace Ordering.API;

/// <summary>
/// The same small ADO.NET helper as Catalog.API's -- duplicated, not
/// shared. That's deliberate: these two services already don't share a
/// domain model or a database (Section 01's Bounded Context point), and
/// pulling six lines of plumbing into a shared library isn't worth the
/// coupling of both services now depending on one more common package's
/// release cadence. A NuGet-worthy amount of shared *infrastructure* code
/// (the actual example: BuildingBlocks/EventBus) is a different call than
/// six lines of glue.
/// </summary>
public static class SqliteExtensions
{
    public static void Execute(this SqliteConnection connection, string sql)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
