namespace BuildingBlocks.EventBus;

/// <summary>
/// Tracks "which .NET handler type runs for which event type name" inside
/// one process. Deliberately separate from EventBusRabbitMQ, which only
/// knows about exchanges, queues, and routing keys — this class is pure
/// bookkeeping with zero RabbitMQ.Client dependency, which is exactly what
/// makes it unit-testable without a broker (see
/// tests/EventBus.Tests/InMemoryEventBusSubscriptionsManagerTests.cs). The
/// event *name*, not the .NET type itself, is the routing key on the wire
/// (see EventBusRabbitMQ.Publish) -- that's what lets a Python or Node
/// subscriber consume the same event without knowing about a C# type.
/// </summary>
public class InMemoryEventBusSubscriptionsManager
{
    private readonly Dictionary<string, List<Type>> _handlersByEventName = new();
    private readonly Dictionary<Type, string> _eventNamesByType = new();

    public event EventHandler<string>? OnEventRemoved;

    public bool IsEmpty => _handlersByEventName.Count == 0;

    public IEnumerable<string> EventNames => _handlersByEventName.Keys;

    public void AddSubscription<TEvent, THandler>()
        where TEvent : IntegrationEvent
        where THandler : IIntegrationEventHandler<TEvent>
    {
        var eventName = GetEventName<TEvent>();
        _eventNamesByType[typeof(TEvent)] = eventName;

        if (!_handlersByEventName.TryGetValue(eventName, out var handlerTypes))
        {
            handlerTypes = [];
            _handlersByEventName[eventName] = handlerTypes;
        }

        if (handlerTypes.Contains(typeof(THandler)))
        {
            throw new ArgumentException(
                $"Handler type {typeof(THandler).Name} already registered for '{eventName}'", nameof(THandler));
        }

        handlerTypes.Add(typeof(THandler));
    }

    public bool HasSubscriptionsForEvent(string eventName) => _handlersByEventName.ContainsKey(eventName);

    public IEnumerable<Type> GetHandlersForEvent(string eventName) =>
        _handlersByEventName.TryGetValue(eventName, out var handlers) ? handlers : [];

    public string GetEventName<TEvent>() where TEvent : IntegrationEvent =>
        _eventNamesByType.TryGetValue(typeof(TEvent), out var name) ? name : typeof(TEvent).Name;

    public void RemoveSubscription(string eventName, Type handlerType)
    {
        if (!_handlersByEventName.TryGetValue(eventName, out var handlers)) return;

        handlers.Remove(handlerType);
        if (handlers.Count != 0) return;

        _handlersByEventName.Remove(eventName);
        OnEventRemoved?.Invoke(this, eventName);
    }
}
