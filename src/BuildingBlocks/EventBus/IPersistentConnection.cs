using RabbitMQ.Client;

namespace BuildingBlocks.EventBus;

public interface IPersistentConnection : IDisposable
{
    bool IsConnected { get; }
    bool TryConnect();
    IModel CreateModel();
}
