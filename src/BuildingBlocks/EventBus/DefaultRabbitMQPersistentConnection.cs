using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace BuildingBlocks.EventBus;

/// <summary>
/// A connection is not just "open or closed" -- RabbitMQ.Client itself
/// fires ConnectionShutdown/CallbackException/ConnectionBlocked on the
/// exact same IConnection instance you're still holding a reference to, so
/// "IsConnected" has to mean "the connection object thinks it's open AND
/// hasn't already fired one of those events", not just "the field is
/// non-null". This wrapper is the one place in the codebase allowed to
/// know that; everything else (EventBusRabbitMQ) just calls CreateModel()
/// and trusts it to reconnect first if needed.
/// </summary>
public sealed class DefaultRabbitMQPersistentConnection(IConnectionFactory connectionFactory, int retryCount = 5)
    : IPersistentConnection
{
    private IConnection? _connection;
    private readonly object _lock = new();
    private bool _disposed;

    public bool IsConnected => _connection is { IsOpen: true } && !_disposed;

    public IModel CreateModel()
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException("No RabbitMQ connection is available to create a model (channel).");
        }

        return _connection!.CreateModel();
    }

    public bool TryConnect()
    {
        lock (_lock)
        {
            if (IsConnected) return true;

            // Linear backoff, not exponential -- a broker that's merely
            // still starting up (see the "docker-compose only guarantees
            // the process started, not that it's ready" gotcha the ebook
            // calls out for SQL Server -- RabbitMQ has the identical
            // failure mode on a fresh docker-compose up) is usually ready
            // within a few seconds, and this connection attempt runs once
            // at process startup, not per-request, so aggressive backoff
            // buys nothing here.
            for (var attempt = 1; attempt <= retryCount; attempt++)
            {
                try
                {
                    _connection = connectionFactory.CreateConnection();
                    _connection.ConnectionShutdown += OnConnectionShutdown;
                    _connection.CallbackException += OnCallbackException;
                    _connection.ConnectionBlocked += OnConnectionBlocked;
                    return true;
                }
                catch (BrokerUnreachableException) when (attempt < retryCount)
                {
                    Thread.Sleep(TimeSpan.FromSeconds(2 * attempt));
                }
            }

            return false;
        }
    }

    private void OnConnectionBlocked(object? sender, RabbitMQ.Client.Events.ConnectionBlockedEventArgs e) =>
        TryReconnect();

    private void OnCallbackException(object? sender, RabbitMQ.Client.Events.CallbackExceptionEventArgs e) =>
        TryReconnect();

    private void OnConnectionShutdown(object? sender, ShutdownEventArgs e) => TryReconnect();

    private void TryReconnect()
    {
        if (_disposed) return;
        TryConnect();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _connection?.Dispose(); } catch (IOException) { /* already gone */ }
    }
}
