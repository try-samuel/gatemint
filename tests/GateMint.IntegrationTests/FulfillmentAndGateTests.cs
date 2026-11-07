using System.Security.Cryptography;
using FluentAssertions;
using GateMint.Core;
using GateMint.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace GateMint.IntegrationTests;

[Collection("postgres")]
public sealed class FulfillmentAndGateTests(PostgresFixture fixture)
{
    private readonly FixedTimeProvider clock = new(new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task DuplicatePaymentNotifications_FulfillOnce()
    {
        var setup = await ReservedOrder("duplicate-payment");
        var verification = new PaymentVerification(setup.Reference, "provider-duplicate-1", true, true,
            setup.AmountKobo, "NGN", setup.OrderId.ToString());
        await using (var firstDb = fixture.CreateDb())
            (await new FulfillmentService(firstDb, clock, Qr()).ApplyVerifiedPaymentAsync(verification, default)).Should().Be(OrderStatus.Confirmed);
        await using (var secondDb = fixture.CreateDb())
            (await new FulfillmentService(secondDb, clock, Qr()).ApplyVerifiedPaymentAsync(verification, default)).Should().Be(OrderStatus.Confirmed);
        await using var verify = fixture.CreateDb();
        (await verify.Tickets.CountAsync(x => x.EventId == setup.EventId)).Should().Be(2);
        (await verify.Outbox.CountAsync(x => x.Type == "ticket.ready" && x.Payload.Contains(setup.OrderId.ToString()))).Should().Be(1);
        var tier = await verify.TicketTypes.SingleAsync(x => x.Id == setup.TierId);
        tier.ReservedCount.Should().Be(0); tier.SoldCount.Should().Be(2);
    }

    [Fact]
    public async Task LateSuccessfulPayment_GoesToRefundWithoutTickets()
    {
        var setup = await ReservedOrder("late-payment");
        clock.Value = clock.Value.AddMinutes(11);
        await using (var releaseDb = fixture.CreateDb()) await new CheckoutService(releaseDb, clock).ReleaseExpiredAsync(default);
        var verification = new PaymentVerification(setup.Reference, "provider-late-1", true, true,
            setup.AmountKobo, "NGN", setup.OrderId.ToString());
        await using (var fulfillDb = fixture.CreateDb())
            (await new FulfillmentService(fulfillDb, clock, Qr()).ApplyVerifiedPaymentAsync(verification, default)).Should().Be(OrderStatus.NeedsRefund);
        await using var verify = fixture.CreateDb();
        (await verify.Tickets.CountAsync(x => x.EventId == setup.EventId)).Should().Be(0);
        (await verify.RefundRequests.CountAsync(x => x.PaymentAttempt.OrderId == setup.OrderId)).Should().Be(1);
        (await verify.TicketTypes.SingleAsync(x => x.Id == setup.TierId)).ReservedCount.Should().Be(0);
    }

    [Fact]
    public async Task PaymentWithMismatchedAmount_IsRejectedWithoutChangingTheOrder()
    {
        var setup = await ReservedOrder("mismatched-payment", quantity: 1);
        var verification = new PaymentVerification(setup.Reference, "provider-mismatch-1", true, true,
            setup.AmountKobo + 1, "NGN", setup.OrderId.ToString());
        await using (var db = fixture.CreateDb())
        {
            var action = () => new FulfillmentService(db, clock, Qr()).ApplyVerifiedPaymentAsync(verification, default);
            await action.Should().ThrowAsync<DomainException>().WithMessage("*did not match*");
        }
        await using var verify = fixture.CreateDb();
        (await verify.Orders.SingleAsync(x => x.Id == setup.OrderId)).Status.Should().Be(OrderStatus.AwaitingPayment);
        (await verify.Tickets.CountAsync(x => x.EventId == setup.EventId)).Should().Be(0);
        (await verify.TicketTypes.SingleAsync(x => x.Id == setup.TierId)).ReservedCount.Should().Be(1);
    }

    [Fact]
    public async Task UnassignedStaff_CannotCheckInAValidTicket()
    {
        var pass = await IssuedTicket("unassigned-staff");
        await using var db = fixture.CreateDb();
        var action = () => new FulfillmentService(db, clock, Qr())
            .CheckInAsync("other-staff", pass.EventId, pass.Code, default);
        await action.Should().ThrowAsync<UnauthorizedAccessException>();
        (await db.CheckIns.CountAsync(x => x.TicketId == pass.TicketId)).Should().Be(0);
    }

    [Fact]
    public async Task InactiveTicket_IsRejectedAtTheGate()
    {
        var pass = await IssuedTicket("inactive-ticket");
        await using (var update = fixture.CreateDb())
        {
            (await update.Tickets.SingleAsync(x => x.Id == pass.TicketId)).Status = TicketStatus.Refunded;
            await update.SaveChangesAsync();
        }
        await using var db = fixture.CreateDb();
        (await new FulfillmentService(db, clock, Qr()).CheckInAsync("staff-user", pass.EventId, pass.Code, default))
            .Result.Should().Be("inactive");
    }

    [Fact]
    public async Task ConcurrentScans_AdmitOnce_AndRemainEventScoped()
    {
        var setup = await ReservedOrder("gate-payment", quantity: 1);
        var verification = new PaymentVerification(setup.Reference, "provider-gate-1", true, true,
            setup.AmountKobo, "NGN", setup.OrderId.ToString());
        await using (var fulfillDb = fixture.CreateDb())
            await new FulfillmentService(fulfillDb, clock, Qr()).ApplyVerifiedPaymentAsync(verification, default);
        Guid ticketId;
        await using (var db = fixture.CreateDb())
        {
            db.EventStaff.Add(new EventStaff { EventId = setup.EventId, UserId = "staff-user" });
            await db.SaveChangesAsync(); ticketId = await db.Tickets.Where(x => x.EventId == setup.EventId).Select(x => x.Id).SingleAsync();
        }
        Ticket ticket;
        await using (var db = fixture.CreateDb()) ticket = await db.Tickets.AsNoTracking().SingleAsync(x => x.Id == ticketId);
        var code = Qr().Create(ticket);
        using var barrier = new Barrier(2);
        var tasks = Enumerable.Range(0, 2).Select(_ => Task.Run(async () =>
        {
            barrier.SignalAndWait(); await using var db = fixture.CreateDb();
            return await new FulfillmentService(db, clock, Qr()).CheckInAsync("staff-user", setup.EventId, code, default);
        })).ToArray();
        var results = await Task.WhenAll(tasks);
        results.Count(x => x.Result == "admitted").Should().Be(1);
        results.Count(x => x.Result == "already_used").Should().Be(1);
        await using var verify = fixture.CreateDb();
        (await verify.CheckIns.CountAsync(x => x.TicketId == ticketId)).Should().Be(1);
        (await new FulfillmentService(verify, clock, Qr()).CheckInAsync("staff-user", Guid.NewGuid(), code, default)).Result.Should().Be("wrong_event");
    }

    private async Task<(Guid OrderId, Guid EventId, Guid TierId, string Reference, long AmountKobo)> ReservedOrder(string key, int quantity = 2)
    {
        clock.Value = new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);
        await using var db = fixture.CreateDb(); var suffix = Guid.NewGuid().ToString("N")[..8];
        var item = new Event { OrganizerId = $"organizer-{suffix}", Slug = $"fulfillment-{suffix}", Title = "Fulfillment Night",
            Description = "Test", Location = "Lagos", Category = "Test", TimeZone = "Africa/Lagos", StartsAt = clock.Value.AddDays(10),
            EndsAt = clock.Value.AddDays(10).AddHours(2), SalesStart = clock.Value.AddDays(-1), SalesEnd = clock.Value.AddDays(9),
            Status = EventStatus.Published, TicketTypes = [new TicketType { Name = "General", PriceKobo = 150000, Capacity = 10,
                SalesStart = clock.Value.AddDays(-1), SalesEnd = clock.Value.AddDays(9) }] };
        db.Events.Add(item); await db.SaveChangesAsync();
        var result = await new CheckoutService(db, clock).ReserveAsync($"buyer-{suffix}", new(item.TicketTypes[0].Id, quantity), key, default);
        var reference = $"gm_{Guid.NewGuid():N}";
        db.PaymentAttempts.Add(new PaymentAttempt { OrderId = result.OrderId, Gateway = "simulator", Reference = reference,
            AmountKobo = result.AmountKobo, Currency = result.Currency, Status = PaymentStatus.Pending, IsTest = true });
        await db.SaveChangesAsync();
        return (result.OrderId, item.Id, item.TicketTypes[0].Id, reference, result.AmountKobo);
    }

    private async Task<(Guid TicketId, Guid EventId, string Code)> IssuedTicket(string key)
    {
        var setup = await ReservedOrder(key, quantity: 1);
        var verification = new PaymentVerification(setup.Reference, $"provider-{key}", true, true,
            setup.AmountKobo, "NGN", setup.OrderId.ToString());
        await using (var fulfill = fixture.CreateDb())
            await new FulfillmentService(fulfill, clock, Qr()).ApplyVerifiedPaymentAsync(verification, default);
        await using var db = fixture.CreateDb();
        db.EventStaff.Add(new EventStaff { EventId = setup.EventId, UserId = "staff-user" });
        await db.SaveChangesAsync();
        var ticket = await db.Tickets.AsNoTracking().SingleAsync(x => x.EventId == setup.EventId);
        return (ticket.Id, setup.EventId, Qr().Create(ticket));
    }

    private static QrTicketService Qr() => new(new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["Tickets:SigningKey"] = Convert.ToBase64String(SHA256.HashData("integration-key"u8.ToArray())) }).Build());
}
