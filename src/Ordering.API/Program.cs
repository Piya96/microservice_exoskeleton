using BuildingBlocks.EventBus;
using Catalog.API.Events;
using Ordering.API;
using Ordering.API.EventHandlers;
using Ordering.API.Events;
using Ordering.API.Services;
using Polly;
using Polly.CircuitBreaker;
using Polly.Extensions.Http;
using RabbitMQ.Client;

var builder = WebApplication.CreateBuilder(args);

var connectionString = Environment.GetEnvironmentVariable("ORDERING_DB_PATH") is { } path
    ? $"Data Source={path}"
    : "Data Source=ordering.db";
builder.Services.AddSingleton(new OrderingDb(connectionString));

// Exact policy shape from the field guide's Resilient HTTP section: 6
// retries, exponential backoff, then a breaker that opens after 5
// consecutive faults and stays open 30s. This is not a cosmetic choice --
// see CatalogServiceClient's doc comment for why this specific call is the
// one place in the skeleton that needed it.
builder.Services.AddHttpClient<CatalogServiceClient>(client =>
    {
        client.BaseAddress = new Uri(Environment.GetEnvironmentVariable("CATALOG_API_URL") ?? "http://localhost:5081");
        client.Timeout = TimeSpan.FromSeconds(5);
    })
    .AddPolicyHandler(GetRetryPolicy())
    .AddPolicyHandler(GetCircuitBreakerPolicy());

builder.Services.AddSingleton<IConnectionFactory>(_ => new ConnectionFactory
{
    HostName = Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? "localhost",
    DispatchConsumersAsync = true,
});
builder.Services.AddSingleton<IPersistentConnection, DefaultRabbitMQPersistentConnection>();
builder.Services.AddSingleton<IEventBus>(sp => new EventBusRabbitMQ(
    sp.GetRequiredService<IPersistentConnection>(),
    sp.GetRequiredService<IServiceScopeFactory>(),
    queueName: "ordering_api_queue"));
builder.Services.AddScoped<ProductPriceChangedIntegrationEventHandler>();

var app = builder.Build();

app.Services.GetRequiredService<OrderingDb>().EnsureCreated();

var connection = app.Services.GetRequiredService<IPersistentConnection>();
connection.TryConnect();
app.Services.GetRequiredService<IEventBus>()
    .Subscribe<ProductPriceChangedIntegrationEvent, ProductPriceChangedIntegrationEventHandler>();

// Routes are root-relative ("/", "/{id}"), not "/orders/..." -- the
// "orders" resource name is the API Gateway's upstream naming choice (see
// ocelot.json), not something this service needs to know about itself.
// That's deliberate: it's the same "gateway shapes the API for the client,
// downstream services don't have to agree on the naming" point Section 02
// makes, applied literally instead of just described.
app.MapPost("/", async (PlaceOrderRequest request, OrderingDb db, CatalogServiceClient catalog, IEventBus eventBus) =>
{
    var (productName, unitPrice, error) = await ResolveProductAsync(request.ProductId, db, catalog);
    if (error is not null) return error;

    int orderId;
    using (var conn = db.Open())
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Orders (ProductId, ProductName, Quantity, UnitPrice, Status, CreatedUtc)
            VALUES (@productId, @productName, @quantity, @unitPrice, 'Placed', @createdUtc);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("@productId", request.ProductId);
        cmd.Parameters.AddWithValue("@productName", productName);
        cmd.Parameters.AddWithValue("@quantity", request.Quantity);
        cmd.Parameters.AddWithValue("@unitPrice", unitPrice);
        cmd.Parameters.AddWithValue("@createdUtc", DateTime.UtcNow.ToString("O"));
        orderId = Convert.ToInt32(cmd.ExecuteScalar());
    }

    // Published AFTER the local commit succeeds, never before -- if the
    // publish itself then fails, this skeleton accepts the small window of
    // inconsistency (order exists, event never went out) rather than
    // implement the Outbox pattern the field guide names for closing that
    // gap. See the README's "What I'd do differently" for the honest
    // reason it's not built here.
    eventBus.Publish(new OrderPlacedIntegrationEvent(
        orderId, request.ProductId, productName, request.Quantity, unitPrice, unitPrice * request.Quantity));

    return Results.Created($"/{orderId}", new { OrderId = orderId, ProductName = productName, unitPrice, request.Quantity });
});

app.MapGet("/{id:int}", (int id, OrderingDb db) =>
{
    using var conn = db.Open();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT Id, ProductId, ProductName, Quantity, UnitPrice, Status, CreatedUtc FROM Orders WHERE Id = @id";
    cmd.Parameters.AddWithValue("@id", id);
    using var reader = cmd.ExecuteReader();
    if (!reader.Read()) return Results.NotFound();

    var order = new Order(reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2), reader.GetInt32(3),
        (decimal)reader.GetDouble(4), reader.GetString(5), DateTime.Parse(reader.GetString(6)));
    return Results.Ok(order);
});

app.MapGet("/", (OrderingDb db) =>
{
    using var conn = db.Open();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT Id, ProductId, ProductName, Quantity, UnitPrice, Status, CreatedUtc FROM Orders ORDER BY Id";
    using var reader = cmd.ExecuteReader();
    var orders = new List<Order>();
    while (reader.Read())
    {
        orders.Add(new Order(reader.GetInt32(0), reader.GetInt32(1), reader.GetString(2), reader.GetInt32(3),
            (decimal)reader.GetDouble(4), reader.GetString(5), DateTime.Parse(reader.GetString(6))));
    }
    return Results.Ok(orders);
});

app.Run();

// Cache-aside: check the locally replicated projection first (kept warm by
// ProductPriceChangedIntegrationEventHandler); only reach across the
// service boundary synchronously -- resiliently, via CatalogServiceClient
// -- on a genuine miss, and backfill the projection so the next order for
// the same product doesn't pay that cost again.
static async Task<(string ProductName, decimal UnitPrice, IResult? Error)> ResolveProductAsync(
    int productId, OrderingDb db, CatalogServiceClient catalog)
{
    using (var conn = db.Open())
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT ProductName, Price FROM CatalogProjection WHERE ProductId = @id";
        cmd.Parameters.AddWithValue("@id", productId);
        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            return (reader.GetString(0), (decimal)reader.GetDouble(1), null);
        }
    }

    try
    {
        var product = await catalog.GetProductAsync(productId);
        if (product is null)
        {
            return (string.Empty, 0m, Results.NotFound(new { error = $"Product {productId} does not exist in Catalog" }));
        }

        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO CatalogProjection (ProductId, ProductName, Price, UpdatedUtc)
            VALUES (@id, @name, @price, @updatedUtc)
            ON CONFLICT(ProductId) DO UPDATE SET ProductName = excluded.ProductName, Price = excluded.Price, UpdatedUtc = excluded.UpdatedUtc;
            """;
        cmd.Parameters.AddWithValue("@id", productId);
        cmd.Parameters.AddWithValue("@name", product.Name);
        cmd.Parameters.AddWithValue("@price", product.Price);
        cmd.Parameters.AddWithValue("@updatedUtc", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();

        return (product.Name, product.Price, null);
    }
    catch (BrokenCircuitException)
    {
        // The typed-exception-to-honest-user-message pattern straight out
        // of the field guide's eShopOnContainers example (BasketController
        // catching BrokenCircuitException) -- a 503 telling the truth,
        // not a generic 500.
        return (string.Empty, 0m, Results.Problem(
            title: "Catalog service is temporarily unavailable",
            detail: "The circuit breaker is open after repeated Catalog failures. Try again shortly.",
            statusCode: StatusCodes.Status503ServiceUnavailable));
    }
}

static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy() =>
    HttpPolicyExtensions.HandleTransientHttpError()
        .OrResult(msg => msg.StatusCode == System.Net.HttpStatusCode.RequestTimeout)
        .WaitAndRetryAsync(6, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));

static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy() =>
    HttpPolicyExtensions.HandleTransientHttpError()
        .CircuitBreakerAsync(handledEventsAllowedBeforeBreaking: 5, durationOfBreak: TimeSpan.FromSeconds(30));

record PlaceOrderRequest(int ProductId, int Quantity);
