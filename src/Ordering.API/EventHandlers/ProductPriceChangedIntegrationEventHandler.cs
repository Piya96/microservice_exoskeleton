using BuildingBlocks.EventBus;
using Catalog.API.Events;

namespace Ordering.API.EventHandlers;

/// <summary>
/// This is the "replicate the data instead of querying across the
/// boundary" half of Ordering's catalog projection actually running: every
/// time Catalog changes a price, Ordering's local copy updates itself
/// asynchronously, with zero request from Ordering and zero knowledge on
/// Catalog's side that Ordering is even listening. The only reason
/// CatalogServiceClient's synchronous fallback (see Services/) ever fires
/// is a product this handler hasn't seen an event for yet -- a brand-new
/// product, or Ordering having been offline when the event fired. Note the
/// reference to Catalog.API.Events.ProductPriceChangedIntegrationEvent
/// directly: in a real multi-repo setup each service would carry its own
/// copy of the wire contract it depends on (a thin "shared kernel" of DTOs
/// is fine; the full Catalog domain model is not) -- referencing Catalog's
/// project directly here is a monorepo-demo convenience, called out
/// explicitly in the README, not a pattern to copy as-is.
/// </summary>
public class ProductPriceChangedIntegrationEventHandler(OrderingDb db)
    : IIntegrationEventHandler<ProductPriceChangedIntegrationEvent>
{
    public Task Handle(ProductPriceChangedIntegrationEvent @event)
    {
        using var connection = db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO CatalogProjection (ProductId, ProductName, Price, UpdatedUtc)
            VALUES (@productId, @name, @price, @updatedUtc)
            ON CONFLICT(ProductId) DO UPDATE SET
                ProductName = excluded.ProductName,
                Price = excluded.Price,
                UpdatedUtc = excluded.UpdatedUtc;
            """;
        cmd.Parameters.AddWithValue("@productId", @event.ProductId);
        cmd.Parameters.AddWithValue("@name", @event.ProductName);
        cmd.Parameters.AddWithValue("@price", @event.NewPrice);
        cmd.Parameters.AddWithValue("@updatedUtc", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();

        Console.WriteLine($"[projection] {@event.ProductName} (#{@event.ProductId}) "
                           + $"{@event.OldPrice:C} -> {@event.NewPrice:C}, cached locally");
        return Task.CompletedTask;
    }
}
