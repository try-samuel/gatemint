using System.Data;
using System.Text.Json;
using GateMint.Core;
using Microsoft.EntityFrameworkCore;

namespace GateMint.Infrastructure;

public sealed class FulfillmentService(ApplicationDbContext db, TimeProvider clock, IQrTicketService qr) : IFulfillmentService
{
    public async Task<OrderStatus> ApplyVerifiedPaymentAsync(PaymentVerification verification, CancellationToken cancellationToken)
    {
        if (!verification.Success) throw new DomainException("The provider has not verified this payment.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var attempt = await db.PaymentAttempts.FromSqlInterpolated(
                $"SELECT * FROM \"PaymentAttempts\" WHERE \"Reference\" = {verification.Reference} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken) ?? throw new DomainException("Payment reference was not found.");
        var order = await db.Orders.Include(x => x.Reservation).Include(x => x.Items).ThenInclude(x => x.Tickets)
            .SingleAsync(x => x.Id == attempt.OrderId, cancellationToken);

        if (order.Status is OrderStatus.Confirmed or OrderStatus.NeedsRefund or OrderStatus.Refunded)
        {
            await transaction.CommitAsync(cancellationToken);
            return order.Status;
        }
        if (!verification.IsTest || attempt.AmountKobo != verification.AmountKobo ||
            !string.Equals(attempt.Currency, verification.Currency, StringComparison.OrdinalIgnoreCase) ||
            verification.OrderAssociation != order.Id.ToString())
            throw new DomainException("Payment verification did not match the order.");

        attempt.ProviderTransactionId = verification.ProviderTransactionId;
        attempt.LastCheckedAt = clock.GetUtcNow();
        var tier = await db.TicketTypes.FromSqlInterpolated(
            $"SELECT * FROM \"TicketTypes\" WHERE \"Id\" = {order.Reservation.TicketTypeId} FOR UPDATE").SingleAsync(cancellationToken);
        if (order.Reservation.State != ReservationState.Held || order.ExpiresAt <= clock.GetUtcNow())
        {
            if (order.Reservation.State == ReservationState.Held)
            {
                tier.ReservedCount -= order.Reservation.Quantity;
                order.Reservation.State = ReservationState.Released;
                order.Reservation.ResolvedAt = clock.GetUtcNow();
            }
            attempt.Status = PaymentStatus.NeedsRefund;
            order.Status = OrderStatus.NeedsRefund;
            if (!await db.RefundRequests.AnyAsync(x => x.PaymentAttemptId == attempt.Id, cancellationToken))
                db.RefundRequests.Add(new RefundRequest { PaymentAttemptId = attempt.Id });
        }
        else
        {
            tier.ReservedCount -= order.Reservation.Quantity;
            tier.SoldCount += order.Reservation.Quantity;
            order.Reservation.State = ReservationState.Consumed;
            order.Reservation.ResolvedAt = clock.GetUtcNow();
            attempt.Status = PaymentStatus.Succeeded;
            order.Status = OrderStatus.Confirmed;
            foreach (var item in order.Items)
                for (var i = item.Tickets.Count; i < item.Quantity; i++)
                    db.Tickets.Add(new Ticket { OrderItemId = item.Id, EventId = order.EventId, IssuedAt = clock.GetUtcNow() });
            db.Outbox.Add(new OutboxMessage { Type = "ticket.ready",
                Payload = JsonSerializer.Serialize(new { OrderId = order.Id, order.AttendeeId }), NextAttemptAt = clock.GetUtcNow() });
        }
        db.AuditEntries.Add(new AuditEntry { ActorId = "payment-system", Action = $"order.{order.Status.ToString().ToLowerInvariant()}",
            EntityType = nameof(Order), EntityId = order.Id.ToString() });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return order.Status;
    }

    public async Task<CheckInResult> CheckInAsync(string staffUserId, Guid eventId, string qrData,
        CancellationToken cancellationToken)
    {
        var parsed = qr.Verify(qrData);
        if (!parsed.Valid) return new("invalid", null, "This code is invalid or has been altered.");
        if (parsed.EventId != eventId) return new("wrong_event", null, "This ticket belongs to another event.");
        if (!await db.EventStaff.AnyAsync(x => x.EventId == eventId && x.UserId == staffUserId, cancellationToken))
            throw new UnauthorizedAccessException("You are not assigned to this event.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var ticket = await db.Tickets.FromSqlInterpolated(
                $"SELECT * FROM \"Tickets\" WHERE \"Id\" = {parsed.TicketId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (ticket is null || ticket.EventId != eventId || ticket.QrNonce != parsed.Nonce)
            return new("invalid", null, "This ticket could not be found.");
        if (ticket.Status != TicketStatus.Active)
            return new("inactive", null, "This ticket is no longer active.");
        var existing = await db.CheckIns.SingleOrDefaultAsync(x => x.TicketId == ticket.Id, cancellationToken);
        if (existing is not null)
            return new("already_used", existing.CheckedInAt, "This ticket was already admitted.");
        var now = clock.GetUtcNow();
        db.CheckIns.Add(new CheckIn { TicketId = ticket.Id, EventId = eventId, StaffUserId = staffUserId, CheckedInAt = now });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new("admitted", now, "Entry approved.");
    }
}
