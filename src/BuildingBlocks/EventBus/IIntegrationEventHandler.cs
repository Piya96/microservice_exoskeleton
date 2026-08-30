namespace BuildingBlocks.EventBus;

public interface IIntegrationEventHandler<in TEvent> : IIntegrationEventHandlerBase
    where TEvent : IntegrationEvent
{
    Task Handle(TEvent @event);
}

// Non-generic marker so the subscriptions manager can hold handlers of
// different closed generic types in one collection without reflection
// gymnastics at every call site.
public interface IIntegrationEventHandlerBase;
