using BuildingBlocks.EventBus;
using Ordering.API.Events;

namespace Notifications.Worker;

/// <summary>
/// This is the entire payoff of the event-bus design: this class exists,
/// subscribes at startup, and Ordering.API needed zero code changes to
/// support it. There was no Notifications.Worker when Ordering.API's
/// order-placement endpoint was written -- adding it later is the
/// open/closed-principle claim from the field guide's Section 04, made
/// concrete rather than just asserted.
/// </summary>
public class OrderPlacedIntegrationEventHandler(ILogger<OrderPlacedIntegrationEventHandler> logger)
    : IIntegrationEventHandler<OrderPlacedIntegrationEvent>
{
    public Task Handle(OrderPlacedIntegrationEvent @event)
    {
        // Stands in for an actual email/SMS/push send -- the point being
        // demonstrated is the fan-out and decoupling, not a notification
        // provider integration.
        logger.LogInformation(
            "Order confirmation -> Order #{OrderId}: {Quantity} x {ProductName} @ {UnitPrice:C} = {TotalPrice:C}",
            @event.OrderId, @event.Quantity, @event.ProductName, @event.UnitPrice, @event.TotalPrice);
        return Task.CompletedTask;
    }
}
