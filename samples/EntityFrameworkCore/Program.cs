using Kevlar;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

await using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();
var shield = Shield.When<IOException>().Retry(2, backoff: Backoff.None);
var options = new DbContextOptionsBuilder<OrdersContext>()
    .UseSqlite(connection)
    .UseKevlarExecutionStrategy(shield)
    .Options;
await using var database = new OrdersContext(options);
await database.Database.EnsureCreatedAsync();

var orderId = Guid.NewGuid();
database.Orders.Add(new Order { Id = orderId });
var attempts = 0;
var strategy = database.Database.CreateExecutionStrategy();
await strategy.ExecuteInTransactionAsync(database,
    async (context, token) =>
    {
        if (++attempts == 1)
        {
            throw new IOException("Simulated transient failure before writing");
        }

        return await context.SaveChangesAsync(acceptAllChangesOnSuccess: false, cancellationToken: token);
    },
    (context, token) => context.Orders.AsNoTracking().AnyAsync(order => order.Id == orderId, token));
database.ChangeTracker.AcceptAllChanges();

var count = await database.Orders.CountAsync();
if (count != 1 || attempts != 2 || !strategy.RetriesOnFailure)
{
    throw new InvalidOperationException("Expected one committed order after two attempts.");
}

Console.WriteLine($"EF Core sample passed: {attempts} attempts, {count} committed order.");

internal sealed class OrdersContext(DbContextOptions<OrdersContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
}

internal sealed class Order
{
    public Guid Id { get; set; }
}
