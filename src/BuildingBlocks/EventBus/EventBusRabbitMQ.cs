using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

namespace BuildingBlocks.EventBus;

/// <summary>
/// A direct exchange, one durable queue per subscribing service, bound
/// with the event's type name as the routing key -- e.g. Notifications.Worker
/// declares "notifications_queue" bound to routing key
/// "OrderPlacedIntegrationEvent". Publishing and subscribing never
/// reference each other's queues directly, only the shared exchange, which
/// is what lets a second, third, or Nth subscriber be added later with
/// zero changes to Ordering.API. See verification/rabbitmq_topology_check.py
/// for where this exact exchange/queue/routing-key shape was proven
/// against a real broker before being trusted here -- there's no .NET SDK
/// in the sandbox this repo was built in to compile and run this class
/// directly.
/// </summary>
public sealed class EventBusRabbitMQ : IEventBus, IDisposable
{
    public const string ExchangeName = "integration_event_bus";

    private readonly IPersistentConnection _connection;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly InMemoryEventBusSubscriptionsManager _subscriptionsManager = new();
    private readonly string _queueName;
    private readonly Dictionary<string, Type> _eventTypes = new();
    private IModel? _consumerChannel;

    public EventBusRabbitMQ(IPersistentConnection connection, IServiceScopeFactory scopeFactory, string queueName)
    {
        _connection = connection;
        _scopeFactory = scopeFactory;
        _queueName = queueName;
    }

    public void Publish(IntegrationEvent @event)
    {
        if (!_connection.IsConnected) _connection.TryConnect();

        using var channel = _connection.CreateModel();
        var eventName = @event.GetType().Name;
        channel.ExchangeDeclare(ExchangeName, type: "direct", durable: true);

        var body = JsonSerializer.SerializeToUtf8Bytes(@event, @event.GetType());

        // Publishing is the one place a transient broker blip is worth a
        // few retries in-process, rather than surfacing straight to the
        // HTTP caller -- three attempts, short exponential backoff. This is
        // deliberately NOT a substitute for the Outbox pattern: if the
        // process crashes between committing the domain change and this
        // Publish call succeeding, the event is still lost. See the
        // README's "What I'd do differently" for why Outbox was left out
        // of this skeleton rather than half-implemented.
        var retryPolicy = Policy
            .Handle<BrokerUnreachableException>()
            .Or<SocketException>()
            .WaitAndRetry(3, attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)));

        retryPolicy.Execute(() =>
        {
            var properties = channel.CreateBasicProperties();
            properties.DeliveryMode = 2; // persistent
            properties.MessageId = @event.Id.ToString();
            channel.BasicPublish(ExchangeName, routingKey: eventName, mandatory: false, basicProperties: properties, body: body);
        });
    }

    public void Subscribe<TEvent, THandler>()
        where TEvent : IntegrationEvent
        where THandler : IIntegrationEventHandler<TEvent>
    {
        var eventName = _subscriptionsManager.GetEventName<TEvent>();
        _eventTypes[eventName] = typeof(TEvent);

        EnsureConsumerChannel();
        _consumerChannel!.QueueBind(_queueName, ExchangeName, routingKey: eventName);

        _subscriptionsManager.AddSubscription<TEvent, THandler>();
        StartBasicConsume();
    }

    private void EnsureConsumerChannel()
    {
        if (_consumerChannel is { IsOpen: true }) return;

        if (!_connection.IsConnected) _connection.TryConnect();

        _consumerChannel = _connection.CreateModel();
        _consumerChannel.ExchangeDeclare(ExchangeName, type: "direct", durable: true);
        _consumerChannel.QueueDeclare(_queueName, durable: true, exclusive: false, autoDelete: false);
        _consumerChannel.CallbackException += (_, _) =>
        {
            _consumerChannel?.Dispose();
            _consumerChannel = null;
            EnsureConsumerChannel();
        };
    }

    private bool _consuming;

    private void StartBasicConsume()
    {
        if (_consuming || _consumerChannel is null) return;
        _consuming = true;

        var consumer = new EventingBasicConsumer(_consumerChannel);
        consumer.Received += async (_, ea) =>
        {
            var eventName = ea.RoutingKey;
            var message = Encoding.UTF8.GetString(ea.Body.Span);

            try
            {
                await ProcessEvent(eventName, message);
                _consumerChannel!.BasicAck(ea.DeliveryTag, multiple: false);
            }
            catch (Exception)
            {
                // A handler exception dead-letters nothing in this
                // skeleton -- there's no DLQ topology here the way
                // Repo 3 (event-pipeline-skeleton) builds one deliberately
                // for that concern. Requeueing blindly on every failure
                // risks a poison-message loop; nacking without requeue
                // silently drops the message. Neither is right for a real
                // system -- see the README for why a production version of
                // this event bus needs Repo 3's retry/DLQ topology layered
                // on top, not reinvented here.
                _consumerChannel!.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
            }
        };

        _consumerChannel!.BasicConsume(_queueName, autoAck: false, consumer);
    }

    private async Task ProcessEvent(string eventName, string message)
    {
        if (!_subscriptionsManager.HasSubscriptionsForEvent(eventName)) return;
        if (!_eventTypes.TryGetValue(eventName, out var eventType)) return;

        var integrationEvent = JsonSerializer.Deserialize(message, eventType)
            ?? throw new InvalidOperationException($"Could not deserialize '{eventName}' payload");

        using var scope = _scopeFactory.CreateScope();
        foreach (var handlerType in _subscriptionsManager.GetHandlersForEvent(eventName))
        {
            var handler = scope.ServiceProvider.GetService(handlerType);
            if (handler is null) continue;

            var handlerInterface = typeof(IIntegrationEventHandler<>).MakeGenericType(eventType);
            var handleMethod = handlerInterface.GetMethod(nameof(IIntegrationEventHandler<IntegrationEvent>.Handle))!;
            await (Task)handleMethod.Invoke(handler, [integrationEvent])!;
        }
    }

    public void Dispose() => _consumerChannel?.Dispose();
}
