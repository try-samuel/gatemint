using FluentAssertions;
using GateMint.Core;
using GateMint.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace GateMint.IntegrationTests;

[Collection("postgres")]
public sealed class BookingConcurrencyTests(PostgresFixture fixture)
{
    private readonly FixedTimeProvider clock = new(new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task FiftyConcurrentBuyers_CannotReserveMoreThanTenTickets()
    {
        var tierId = await SeedTier(10);
        using var barrier = new Barrier(50);
        var tasks = Enumerable.Range(0, 50).Select(index => Task.Run(async () =>
        {
            barrier.SignalAndWait();
            await using var db = fixture.CreateDb();
            try { return await new CheckoutService(db, clock).ReserveAsync($"buyer-{index}", new(tierId, 1), $"key-{index}", default); }
            catch (DomainException) { return null; }
        })).ToArray();
        var results = await Task.WhenAll(tasks);
        results.Count(x => x is not null).Should().Be(10);
        await using var verify = fixture.CreateDb();
        var tier = await verify.TicketTypes.SingleAsync(x => x.Id == tierId);
        tier.ReservedCount.Should().Be(10);
        (tier.SoldCount + tier.ReservedCount).Should().BeLessThanOrEqualTo(tier.Capacity);
    }

    [Fact]
    public async Task ConcurrentIdenticalIdempotencyKeys_CreateOneOrder()
    {
        var tierId = await SeedTier(20);
        using var barrier = new Barrier(12);
        var tasks = Enumerable.Range(0, 12).Select(_ => Task.Run(async () =>
        {
            barrier.SignalAndWait();
            await using var db = fixture.CreateDb();
            return await new CheckoutService(db, clock).ReserveAsync("same-buyer", new(tierId, 2), "same-key", default);
        })).ToArray();
        var results = await Task.WhenAll(tasks);
        results.Select(x => x.OrderId).Distinct().Should().ContainSingle();
        await using var verify = fixture.CreateDb();
        (await verify.Orders.CountAsync(x => x.AttendeeId == "same-buyer" && x.IdempotencyKey == "same-key")).Should().Be(1);
        (await verify.TicketTypes.SingleAsync(x => x.Id == tierId)).ReservedCount.Should().Be(2);
    }

    [Fact]
    public async Task ReusedIdempotencyKey_WithDifferentBody_IsRejected()
    {
        var tierId = await SeedTier(20); await using var db = fixture.CreateDb();
        var service = new CheckoutService(db, clock);
        await service.ReserveAsync("fingerprint-buyer", new(tierId, 1), "fingerprint-key", default);
        var action = () => service.ReserveAsync("fingerprint-buyer", new(tierId, 2), "fingerprint-key", default);
        await action.Should().ThrowAsync<IdempotencyConflictException>();
    }

    private async Task<Guid> SeedTier(int capacity)
    {
        await using var db = fixture.CreateDb(); var suffix = Guid.NewGuid().ToString("N")[..8];
        var item = new Event { OrganizerId = $"organizer-{suffix}", Slug = $"event-{suffix}", Title = "Concurrency Night",
            Description = "Test", Location = "Lagos", Category = "Test", TimeZone = "Africa/Lagos",
            StartsAt = clock.Value.AddDays(10), EndsAt = clock.Value.AddDays(10).AddHours(2), SalesStart = clock.Value.AddDays(-1),
            SalesEnd = clock.Value.AddDays(9), Status = EventStatus.Published,
            TicketTypes = [new TicketType { Name = "General", PriceKobo = 100000, Capacity = capacity,
                SalesStart = clock.Value.AddDays(-1), SalesEnd = clock.Value.AddDays(9) }] };
        db.Events.Add(item); await db.SaveChangesAsync(); return item.TicketTypes[0].Id;
    }
}
