using System.Security.Cryptography;
using GateMint.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace GateMint.IntegrationTests;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("gatemint_tests").WithUsername("postgres")
        .WithPassword(Convert.ToHexString(RandomNumberGenerator.GetBytes(24))).Build();
    public string ConnectionString => container.GetConnectionString();
    public async Task InitializeAsync()
    {
        await container.StartAsync();
        await using var db = CreateDb();
        await db.Database.MigrateAsync();
    }
    public async Task DisposeAsync() => await container.DisposeAsync();
    public ApplicationDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(ConnectionString).Options;
        return new ApplicationDbContext(options);
    }
}

[CollectionDefinition("postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;

public sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
{
    public DateTimeOffset Value { get; set; } = value;
    public override DateTimeOffset GetUtcNow() => Value;
}
