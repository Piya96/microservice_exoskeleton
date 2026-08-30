using BuildingBlocks.EventBus;

namespace Catalog.API.Events;

/// <summary>
/// Defined here, in Catalog.API's own application layer, not in a shared
/// events library -- see BuildingBlocks/EventBus/IntegrationEvent.cs for
/// why. Any current or future subscriber (Ordering.API's projection cache,
/// a future Basket service, a search-index updater) depends only on this
/// shape and the event name on the wire, never on Catalog's internal
/// Product entity.
/// </summary>
public record ProductPriceChangedIntegrationEvent(int ProductId, string ProductName, decimal OldPrice, decimal NewPrice)
    : IntegrationEvent;
