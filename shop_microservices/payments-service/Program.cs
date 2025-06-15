using Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using MassTransit.RabbitMqTransport;
using MassTransit.EntityFrameworkCoreIntegration;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<PaymentsDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddMassTransit(x =>
{
    x.SetKebabCaseEndpointNameFormatter();

    x.AddEntityFrameworkOutbox<PaymentsDbContext>(o =>
    {
        o.UsePostgres();
    });

    //x.AddEntityFrameworkInbox<PaymentsDbContext>();

    x.AddConsumer<PaymentRequestedConsumer>(cfg =>
    {
        //cfg.UseEntityFrameworkInbox<PaymentsDbContext>();
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

app.MapPost("/accounts", async (Guid userId, PaymentsDbContext db) =>
{
    if (await db.Accounts.AnyAsync(a => a.UserId == userId))
        return Results.Conflict("Account already exists");

    var acc = new Account { UserId = userId, Balance = 0m };
    db.Accounts.Add(acc);
    await db.SaveChangesAsync();
    return Results.Created($"/accounts/{userId}", acc);
});

app.MapPost("/accounts/deposit", async (DepositRequest req, PaymentsDbContext db) =>
{
    var acc = await db.Accounts.SingleOrDefaultAsync(a => a.UserId == req.UserId);
    if (acc is null) return Results.NotFound();

    acc.Balance += req.Amount;
    await db.SaveChangesAsync();
    return Results.Ok(acc.Balance);
});

app.MapGet("/accounts/{userId:guid}", async (Guid userId, PaymentsDbContext db) =>
{
    var acc = await db.Accounts.SingleOrDefaultAsync(a => a.UserId == userId);
    return acc is null ? Results.NotFound() : Results.Ok(acc.Balance);
});

app.UseSwagger();
app.UseSwaggerUI();

await app.MigrateDbAsync();
app.Run();


class Account
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public decimal Balance { get; set; }
    public uint RowVersion { get; set; }
}

record DepositRequest(Guid UserId, decimal Amount);


class PaymentsDbContext(DbContextOptions<PaymentsDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
}


class PaymentRequestedConsumer(ILogger<PaymentRequestedConsumer> logger, PaymentsDbContext db)
    : IConsumer<PaymentRequested>
{
    public async Task Consume(ConsumeContext<PaymentRequested> context)
    {
        var evt = context.Message;
        var account = await db.Accounts.SingleOrDefaultAsync(a => a.UserId == evt.UserId);

        if (account is null)
        {
            logger.LogWarning("Account not found for {User}", evt.UserId);
            await context.Publish(new PaymentProcessed(evt.OrderId, evt.UserId, false, "No account"));
            return;
        }

        if (account.Balance < evt.Amount)
        {
            await context.Publish(new PaymentProcessed(evt.OrderId, evt.UserId, false, "Insufficient funds"));
            return;
        }

        account.Balance -= evt.Amount;
        await db.SaveChangesAsync();

        await context.Publish(new PaymentProcessed(evt.OrderId, evt.UserId, true));
    }
}


static class MigrationExt
{
    public static async Task MigrateDbAsync(this IHost host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        await db.Database.EnsureCreatedAsync();
    }
}
