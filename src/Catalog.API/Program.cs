using BuildingBlocks.EventBus;
using Catalog.API;
using Catalog.API.Events;
using RabbitMQ.Client;

var builder = WebApplication.CreateBuilder(args);

var connectionString = Environment.GetEnvironmentVariable("CATALOG_DB_PATH") is { } path
    ? $"Data Source={path}"
    : "Data Source=catalog.db";
builder.Services.AddSingleton(new CatalogDb(connectionString));

builder.Services.AddSingleton<IConnectionFactory>(_ => new ConnectionFactory
{
    HostName = Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? "localhost",
    DispatchConsumersAsync = true,
});
builder.Services.AddSingleton<IPersistentConnection, DefaultRabbitMQPersistentConnection>();
builder.Services.AddSingleton<IEventBus>(sp => new EventBusRabbitMQ(
    sp.GetRequiredService<IPersistentConnection>(),
    sp.GetRequiredService<IServiceScopeFactory>(),
    queueName: "catalog_api_queue")); // Catalog doesn't currently subscribe to anything, but every
                                       // service gets its own queue name up front -- adding a
                                       // subscription later (e.g. a StockReserved event from a
                                       // future Ordering flow) is then a one-line Subscribe<>() call,
                                       // not a topology change.

var app = builder.Build();

var db = app.Services.GetRequiredService<CatalogDb>();
db.EnsureCreatedAndSeeded();

app.Services.GetRequiredService<IPersistentConnection>().TryConnect();

app.MapGet("/products", () =>
{
    using var connection = db.Open();
    using var cmd = connection.CreateCommand();
    cmd.CommandText = "SELECT Id, Name, Price, Stock FROM Products ORDER BY Id";
    using var reader = cmd.ExecuteReader();
    var products = new List<Product>();
    while (reader.Read())
    {
        products.Add(new Product(reader.GetInt32(0), reader.GetString(1), (decimal)reader.GetDouble(2), reader.GetInt32(3)));
    }
    return Results.Ok(products);
});

app.MapGet("/products/{id:int}", (int id) =>
{
    using var connection = db.Open();
    using var cmd = connection.CreateCommand();
    cmd.CommandText = "SELECT Id, Name, Price, Stock FROM Products WHERE Id = @id";
    cmd.Parameters.AddWithValue("@id", id);
    using var reader = cmd.ExecuteReader();
    if (!reader.Read()) return Results.NotFound();

    var product = new Product(reader.GetInt32(0), reader.GetString(1), (decimal)reader.GetDouble(2), reader.GetInt32(3));
    return Results.Ok(product);
});

app.MapPut("/products/{id:int}/price", (int id, UpdatePriceRequest request, IEventBus eventBus) =>
{
    using var connection = db.Open();

    using var readCmd = connection.CreateCommand();
    readCmd.CommandText = "SELECT Name, Price, Stock FROM Products WHERE Id = @id";
    readCmd.Parameters.AddWithValue("@id", id);
    using var reader = readCmd.ExecuteReader();
    if (!reader.Read()) return Results.NotFound();

    var productName = reader.GetString(0);
    var oldPrice = (decimal)reader.GetDouble(1);
    var stock = reader.GetInt32(2);
    reader.Close();

    using var updateCmd = connection.CreateCommand();
    updateCmd.CommandText = "UPDATE Products SET Price = @price WHERE Id = @id";
    updateCmd.Parameters.AddWithValue("@price", request.NewPrice);
    updateCmd.Parameters.AddWithValue("@id", id);
    updateCmd.ExecuteNonQuery();

    // The whole point of this event: Catalog doesn't know or care who's
    // listening. Today, nobody in this skeleton is (see the queue comment
    // above) -- that's fine, and is the open/closed-principle payoff the
    // ebook names directly: a subscriber can be added later with zero
    // change here.
    eventBus.Publish(new ProductPriceChangedIntegrationEvent(id, productName, oldPrice, request.NewPrice));

    return Results.Ok(new Product(id, productName, request.NewPrice, stock));
});

app.Run();

record UpdatePriceRequest(decimal NewPrice);
