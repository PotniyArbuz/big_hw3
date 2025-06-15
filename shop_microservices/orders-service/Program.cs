using Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using MassTransit.RabbitMqTransport;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<OrdersDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddMassTransit(x =>
{
    x.SetKebabCaseEndpointNameFormatter();
    x.AddEntityFrameworkOutbox<OrdersDbContext>(o =>
    {
        o.QueryDelay = TimeSpan.FromSeconds(1);
        o.UsePostgres();
    });

    x.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(builder.Configuration.GetValue<string>("RabbitMQ:Host"), "/", h => { });
        cfg.ConfigureEndpoints(context);
    });
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.MapPost("/orders", async (CreateOrderRequest req, OrdersDbContext db, IPublishEndpoint publish) =>
{
    var order = new Order
    {
        Id = Guid.NewGuid(),
        UserId = req.UserId,
        Amount = req.Amount,
        Status = OrderStatus.Pending,
        CreatedAt = DateTime.UtcNow
    };

    db.Orders.Add(order);
    await db.SaveChangesAsync();

    await publish.Publish(new PaymentRequested(order.Id, order.UserId, order.Amount));

    return Results.Accepted($"/orders/{order.Id}", new { order.Id, order.Status });
});

app.MapGet("/orders", async (Guid userId, OrdersDbContext db) =>
    await db.Orders.Where(o => o.UserId == userId).ToListAsync());

app.MapGet("/orders/{id:guid}", async (Guid id, OrdersDbContext db) =>
    await db.Orders.FindAsync(id) is {} order
        ? Results.Ok(order)
        : Results.NotFound());

app.UseSwagger();
app.UseSwaggerUI();

await app.MigrateDbAsync();
app.Run();


enum OrderStatus
{
    Pending = 0,
    Paid    = 1,
    Failed  = 2
}

class Order
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public decimal Amount { get; set; }
    public OrderStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
}

record CreateOrderRequest(Guid UserId, decimal Amount);

class OrdersDbContext(DbContextOptions<OrdersDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
}


static class MigrationExt
{
    public static async Task MigrateDbAsync(this IHost host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        await db.Database.EnsureCreatedAsync();
    }
}
