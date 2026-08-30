namespace BuildingBlocks.EventBus;

/// <summary>
/// The pub/sub middleman: publishers and subscribers only ever reference
/// this interface, never each other. Ordering publishing
/// OrderPlacedIntegrationEvent has no idea Notifications.Worker exists, or
/// how many other services (if any) also subscribed — that's the whole
/// point. A service resolves this via DI and gets whichever
/// implementation is registered (see EventBusRabbitMQ), so swapping the
/// transport later doesn't touch a single publisher or handler.
/// </summary>
public interface IEventBus
{
    void Publish(IntegrationEvent @event);

    void Subscribe<TEvent, THandler>()
        where TEvent : IntegrationEvent
        where THandler : IIntegrationEventHandler<TEvent>;
}
