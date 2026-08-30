using BuildingBlocks.EventBus;
using Notifications.Worker;
using Ordering.API.Events;
using RabbitMQ.Client;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton<IConnectionFactory>(_ => new ConnectionFactory
{
    HostName = Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? "localhost",
    DispatchConsumersAsync = true,
});
builder.Services.AddSingleton<IPersistentConnection, DefaultRabbitMQPersistentConnection>();
builder.Services.AddSingleton<IEventBus>(sp => new EventBusRabbitMQ(
    sp.GetRequiredService<IPersistentConnection>(),
    sp.GetRequiredService<IServiceScopeFactory>(),
    queueName: "notifications_worker_queue"));
builder.Services.AddScoped<OrderPlacedIntegrationEventHandler>();
builder.Services.AddHostedService<SubscriberHostedService>();

var host = builder.Build();
host.Run();

/// <summary>
/// A worker's entire job here: connect, subscribe once at startup, then
/// let RabbitMQ.Client's own consumer callback thread do the rest --
/// there's no polling loop to write. IHostedService.StartAsync is exactly
/// the right hook for "do this once when the process comes up."
/// </summary>
public class SubscriberHostedService(IPersistentConnection connection, IEventBus eventBus) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        connection.TryConnect();
        eventBus.Subscribe<OrderPlacedIntegrationEvent, OrderPlacedIntegrationEventHandler>();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
