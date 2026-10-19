using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GateMint.Core;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GateMint.Infrastructure;

public sealed class CheckoutService(ApplicationDbContext db, TimeProvider clock) : ICheckoutService
{
    public async Task<CheckoutResult> ReserveAsync(string attendeeId, CheckoutRequest request, string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (request.Quantity is < 1 or > 6) throw new DomainException("Quantity must be between 1 and 6.");
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 100)
            throw new DomainException("A valid Idempotency-Key is required.");

        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { request.TicketTypeId, request.Quantity }))));
        var existing = await db.Orders.AsNoTracking()
            .SingleOrDefaultAsync(x => x.AttendeeId == attendeeId && x.IdempotencyKey == idempotencyKey, cancellationToken);
        if (existing is not null) return Replay(existing, fingerprint);

        for (var attempt = 0; attempt < 4; attempt++)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            try
            {
                var tier = await db.TicketTypes.FromSqlInterpolated(
                        $"SELECT * FROM \"TicketTypes\" WHERE \"Id\" = {request.TicketTypeId} FOR UPDATE")
                    .Include(x => x.Event).SingleOrDefaultAsync(cancellationToken)
                    ?? throw new DomainException("Ticket type was not found.");
                var now = clock.GetUtcNow();
                if (tier.Event.Status != EventStatus.Published || now < tier.Event.SalesStart || now > tier.Event.SalesEnd ||
                    now < tier.SalesStart || now > tier.SalesEnd)
                    throw new DomainException("Tickets are not currently on sale.");
                if (tier.SoldCount + tier.ReservedCount + request.Quantity > tier.Capacity)
                    throw new DomainException("Not enough tickets remain.");

                tier.ReservedCount += request.Quantity;
                var expiresAt = now.AddMinutes(10);
                var order = new Order
                {
                    AttendeeId = attendeeId, EventId = tier.EventId, AmountKobo = checked(tier.PriceKobo * request.Quantity),
                    Currency = tier.Currency, ExpiresAt = expiresAt, IdempotencyKey = idempotencyKey,
                    RequestFingerprint = fingerprint,
                    Items = [new OrderItem { TicketTypeId = tier.Id, TicketTypeName = tier.Name,
                        Quantity = request.Quantity, UnitPriceKobo = tier.PriceKobo,
                        TotalKobo = checked(tier.PriceKobo * request.Quantity), Currency = tier.Currency }],
                    Reservation = new Reservation { TicketTypeId = tier.Id, Quantity = request.Quantity, ExpiresAt = expiresAt }
                };
                db.Orders.Add(order);
                db.AuditEntries.Add(new AuditEntry { ActorId = attendeeId, Action = "order.reserved",
                    EntityType = nameof(Order), EntityId = order.Id.ToString() });
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return new CheckoutResult(order.Id, order.Status, order.AmountKobo, order.Currency, order.ExpiresAt, false);
            }
            catch (Exception ex) when (IsRetryable(ex) && attempt < 3)
            {
                await transaction.RollbackAsync(cancellationToken);
                db.ChangeTracker.Clear();
            }
            catch (DbUpdateException)
            {
                await transaction.RollbackAsync(cancellationToken);
                db.ChangeTracker.Clear();
                existing = await db.Orders.AsNoTracking().SingleOrDefaultAsync(
                    x => x.AttendeeId == attendeeId && x.IdempotencyKey == idempotencyKey, cancellationToken);
                if (existing is not null) return Replay(existing, fingerprint);
                throw;
            }
        }
        throw new DomainException("The reservation could not be completed. Please retry.");
    }

    public async Task CancelAsync(string attendeeId, Guid orderId, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var order = await db.Orders.Include(x => x.Reservation).SingleOrDefaultAsync(
            x => x.Id == orderId && x.AttendeeId == attendeeId, cancellationToken)
            ?? throw new DomainException("Order was not found.");
        if (order.Status != OrderStatus.AwaitingPayment || order.Reservation.State != ReservationState.Held)
            throw new DomainException("This order can no longer be cancelled.");
        var tier = await LockTier(order.Reservation.TicketTypeId, cancellationToken);
        tier.ReservedCount -= order.Reservation.Quantity;
        order.Reservation.State = ReservationState.Released;
        order.Reservation.ResolvedAt = clock.GetUtcNow();
        order.Status = OrderStatus.Cancelled;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<int> ReleaseExpiredAsync(CancellationToken cancellationToken)
    {
        var ids = await db.Reservations.Where(x => x.State == ReservationState.Held && x.ExpiresAt <= clock.GetUtcNow())
            .OrderBy(x => x.TicketTypeId).Select(x => x.OrderId).Take(100).ToListAsync(cancellationToken);
        var released = 0;
        foreach (var id in ids)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            var order = await db.Orders.Include(x => x.Reservation).SingleAsync(x => x.Id == id, cancellationToken);
            if (order.Reservation.State == ReservationState.Held && order.ExpiresAt <= clock.GetUtcNow())
            {
                var tier = await LockTier(order.Reservation.TicketTypeId, cancellationToken);
                tier.ReservedCount -= order.Reservation.Quantity;
                order.Reservation.State = ReservationState.Released;
                order.Reservation.ResolvedAt = clock.GetUtcNow();
                order.Status = OrderStatus.Expired;
                await db.SaveChangesAsync(cancellationToken);
                released++;
            }
            await transaction.CommitAsync(cancellationToken);
        }
        return released;
    }

    private async Task<TicketType> LockTier(Guid id, CancellationToken cancellationToken) =>
        await db.TicketTypes.FromSqlInterpolated($"SELECT * FROM \"TicketTypes\" WHERE \"Id\" = {id} FOR UPDATE")
            .SingleAsync(cancellationToken);
    private static CheckoutResult Replay(Order order, string fingerprint) => order.RequestFingerprint == fingerprint
        ? new(order.Id, order.Status, order.AmountKobo, order.Currency, order.ExpiresAt, true)
        : throw new IdempotencyConflictException();
    private static bool IsRetryable(Exception ex) => ex is PostgresException { SqlState: "40001" or "40P01" }
        || ex.InnerException is PostgresException { SqlState: "40001" or "40P01" };
}

public class DomainException(string message) : Exception(message);
public sealed class IdempotencyConflictException() : DomainException("This idempotency key was already used with different parameters.");
