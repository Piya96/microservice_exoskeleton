using BuildingBlocks.EventBus;

namespace Ordering.API.Events;

/// <summary>
/// "Thin vs. fat" integration events is a real, named trade-off the source
/// material calls out -- carry just an id and make subscribers query back
/// for details, or carry enough that they don't have to. This one is
/// deliberately fat: ProductName and UnitPrice are included so
/// Notifications.Worker can compose a confirmation message without a
/// synchronous callback to either Ordering or Catalog, which would just
/// reintroduce the sync-dependency problem this whole design avoids.
/// </summary>
public record OrderPlacedIntegrationEvent(
    int OrderId, int ProductId, string ProductName, int Quantity, decimal UnitPrice, decimal TotalPrice)
    : IntegrationEvent;
