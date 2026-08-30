using BuildingBlocks.EventBus;
using Xunit;

namespace EventBus.Tests;

// Pure bookkeeping logic, zero RabbitMQ.Client dependency -- this is the
// one piece of the event bus that's actually unit-testable without a
// broker. (Reviewed but not run: no .NET SDK in this sandbox -- see
// verification/rabbitmq_topology_check.py for what *was* run, against a
// real broker, to check the wire-level pub/sub shape this class assumes.)
public record TestEvent : IntegrationEvent;
public record OtherEvent : IntegrationEvent;

public class TestEventHandler : IIntegrationEventHandler<TestEvent>
{
    public Task Handle(TestEvent @event) => Task.CompletedTask;
}

public class AnotherTestEventHandler : IIntegrationEventHandler<TestEvent>
{
    public Task Handle(TestEvent @event) => Task.CompletedTask;
}

public class InMemoryEventBusSubscriptionsManagerTests
{
    [Fact]
    public void New_manager_is_empty()
    {
        var sut = new InMemoryEventBusSubscriptionsManager();
        Assert.True(sut.IsEmpty);
    }

    [Fact]
    public void After_subscribing_it_is_no_longer_empty_and_knows_the_event_name()
    {
        var sut = new InMemoryEventBusSubscriptionsManager();
        sut.AddSubscription<TestEvent, TestEventHandler>();

        Assert.False(sut.IsEmpty);
        Assert.True(sut.HasSubscriptionsForEvent(nameof(TestEvent)));
        Assert.Contains(nameof(TestEvent), sut.EventNames);
    }

    [Fact]
    public void Multiple_handlers_can_subscribe_to_the_same_event()
    {
        var sut = new InMemoryEventBusSubscriptionsManager();
        sut.AddSubscription<TestEvent, TestEventHandler>();
        sut.AddSubscription<TestEvent, AnotherTestEventHandler>();

        var handlers = sut.GetHandlersForEvent(nameof(TestEvent)).ToList();
        Assert.Equal(2, handlers.Count);
        Assert.Contains(typeof(TestEventHandler), handlers);
        Assert.Contains(typeof(AnotherTestEventHandler), handlers);
    }

    [Fact]
    public void Subscribing_the_same_handler_twice_to_the_same_event_throws()
    {
        var sut = new InMemoryEventBusSubscriptionsManager();
        sut.AddSubscription<TestEvent, TestEventHandler>();

        Assert.Throws<ArgumentException>(() => sut.AddSubscription<TestEvent, TestEventHandler>());
    }

    [Fact]
    public void Unrelated_events_do_not_share_subscriptions()
    {
        var sut = new InMemoryEventBusSubscriptionsManager();
        sut.AddSubscription<TestEvent, TestEventHandler>();

        Assert.False(sut.HasSubscriptionsForEvent(nameof(OtherEvent)));
        Assert.Empty(sut.GetHandlersForEvent(nameof(OtherEvent)));
    }

    [Fact]
    public void Removing_the_last_handler_for_an_event_fires_OnEventRemoved_and_drops_the_event()
    {
        var sut = new InMemoryEventBusSubscriptionsManager();
        sut.AddSubscription<TestEvent, TestEventHandler>();

        string? removedEventName = null;
        sut.OnEventRemoved += (_, name) => removedEventName = name;

        sut.RemoveSubscription(nameof(TestEvent), typeof(TestEventHandler));

        Assert.Equal(nameof(TestEvent), removedEventName);
        Assert.False(sut.HasSubscriptionsForEvent(nameof(TestEvent)));
        Assert.True(sut.IsEmpty);
    }

    [Fact]
    public void Removing_one_of_several_handlers_keeps_the_event_registered()
    {
        var sut = new InMemoryEventBusSubscriptionsManager();
        sut.AddSubscription<TestEvent, TestEventHandler>();
        sut.AddSubscription<TestEvent, AnotherTestEventHandler>();

        sut.RemoveSubscription(nameof(TestEvent), typeof(TestEventHandler));

        Assert.True(sut.HasSubscriptionsForEvent(nameof(TestEvent)));
        Assert.DoesNotContain(typeof(TestEventHandler), sut.GetHandlersForEvent(nameof(TestEvent)));
    }
}
