namespace GateMint.Core;

public enum EventStatus { Draft, Published, Unpublished }
public enum OrderStatus { AwaitingPayment, Confirmed, Expired, Cancelled, NeedsRefund, Refunded }
public enum ReservationState { Held, Consumed, Released }
public enum PaymentStatus { Initializing, Pending, Succeeded, Failed, NeedsRefund, RefundPending, Refunded }
public enum TicketStatus { Active, Refunded, Cancelled }
public enum WorkStatus { Pending, Processing, Completed, Failed }

public abstract class Entity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Event : Entity
{
    public required string OrganizerId { get; set; }
    public required string Slug { get; set; }
    public required string Title { get; set; }
    public required string Description { get; set; }
    public required string Location { get; set; }
    public required string Category { get; set; }
    public required string TimeZone { get; set; }
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
    public DateTimeOffset SalesStart { get; set; }
    public DateTimeOffset SalesEnd { get; set; }
    public EventStatus Status { get; set; } = EventStatus.Draft;
    public List<TicketType> TicketTypes { get; set; } = [];
    public List<EventStaff> Staff { get; set; } = [];
}

public sealed class EventStaff : Entity
{
    public Guid EventId { get; set; }
    public Event Event { get; set; } = null!;
    public required string UserId { get; set; }
}

public sealed class TicketType : Entity
{
    public Guid EventId { get; set; }
    public Event Event { get; set; } = null!;
    public required string Name { get; set; }
    public long PriceKobo { get; set; }
    public string Currency { get; set; } = "NGN";
    public int Capacity { get; set; }
    public int ReservedCount { get; set; }
    public int SoldCount { get; set; }
    public DateTimeOffset SalesStart { get; set; }
    public DateTimeOffset SalesEnd { get; set; }
    public uint Version { get; set; }
}

public sealed class Order : Entity
{
    public required string AttendeeId { get; set; }
    public Guid EventId { get; set; }
    public Event Event { get; set; } = null!;
    public OrderStatus Status { get; set; } = OrderStatus.AwaitingPayment;
    public long AmountKobo { get; set; }
    public string Currency { get; set; } = "NGN";
    public DateTimeOffset ExpiresAt { get; set; }
    public required string IdempotencyKey { get; set; }
    public required string RequestFingerprint { get; set; }
    public List<OrderItem> Items { get; set; } = [];
    public Reservation Reservation { get; set; } = null!;
    public List<PaymentAttempt> PaymentAttempts { get; set; } = [];
}

public sealed class OrderItem : Entity
{
    public Guid OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public Guid TicketTypeId { get; set; }
    public TicketType TicketType { get; set; } = null!;
    public required string TicketTypeName { get; set; }
    public int Quantity { get; set; }
    public long UnitPriceKobo { get; set; }
    public long TotalKobo { get; set; }
    public string Currency { get; set; } = "NGN";
    public List<Ticket> Tickets { get; set; } = [];
}

public sealed class Reservation : Entity
{
    public Guid OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public Guid TicketTypeId { get; set; }
    public int Quantity { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public ReservationState State { get; set; } = ReservationState.Held;
    public DateTimeOffset? ResolvedAt { get; set; }
}

public sealed class PaymentAttempt : Entity
{
    public Guid OrderId { get; set; }
    public Order Order { get; set; } = null!;
    public required string Gateway { get; set; }
    public required string Reference { get; set; }
    public string? ProviderTransactionId { get; set; }
    public long AmountKobo { get; set; }
    public string Currency { get; set; } = "NGN";
    public PaymentStatus Status { get; set; } = PaymentStatus.Initializing;
    public bool IsTest { get; set; }
    public DateTimeOffset? LastCheckedAt { get; set; }
}

public sealed class Ticket : Entity
{
    public Guid OrderItemId { get; set; }
    public OrderItem OrderItem { get; set; } = null!;
    public Guid EventId { get; set; }
    public Guid QrNonce { get; set; } = Guid.NewGuid();
    public TicketStatus Status { get; set; } = TicketStatus.Active;
    public DateTimeOffset IssuedAt { get; set; }
    public CheckIn? CheckIn { get; set; }
}

public sealed class CheckIn : Entity
{
    public Guid TicketId { get; set; }
    public Ticket Ticket { get; set; } = null!;
    public Guid EventId { get; set; }
    public required string StaffUserId { get; set; }
    public DateTimeOffset CheckedInAt { get; set; }
}

public sealed class WebhookInbox : Entity
{
    public required string Provider { get; set; }
    public required string DeduplicationKey { get; set; }
    public required string EventType { get; set; }
    public required string Payload { get; set; }
    public WorkStatus Status { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
}

public sealed class OutboxMessage : Entity
{
    public required string Type { get; set; }
    public required string Payload { get; set; }
    public WorkStatus Status { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class RefundRequest : Entity
{
    public Guid PaymentAttemptId { get; set; }
    public PaymentAttempt PaymentAttempt { get; set; } = null!;
    public PaymentStatus Status { get; set; } = PaymentStatus.RefundPending;
    public string? ProviderRefundId { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }
}

public sealed class AuditEntry : Entity
{
    public required string ActorId { get; set; }
    public required string Action { get; set; }
    public required string EntityType { get; set; }
    public required string EntityId { get; set; }
    public string? Data { get; set; }
}

public sealed record EventSummary(Guid Id, string Slug, string Title, string Location, string Category,
    DateTimeOffset StartsAt, string TimeZone, long FromPriceKobo, int Available);
public sealed record TicketTypeView(Guid Id, string Name, long PriceKobo, string Currency, int Available,
    DateTimeOffset SalesStart, DateTimeOffset SalesEnd);
public sealed record EventDetail(Guid Id, string Slug, string Title, string Description, string Location,
    string Category, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string TimeZone,
    IReadOnlyList<TicketTypeView> TicketTypes);
public sealed record CreateEventRequest(string Title, string Description, string Location, string Category,
    DateTimeOffset StartsAt, DateTimeOffset EndsAt, string TimeZone, DateTimeOffset SalesStart, DateTimeOffset SalesEnd);
public sealed record CreateTicketTypeRequest(string Name, long PriceKobo, int Capacity,
    DateTimeOffset SalesStart, DateTimeOffset SalesEnd);
public sealed record CheckoutRequest(Guid TicketTypeId, int Quantity);
public sealed record CheckoutResult(Guid OrderId, OrderStatus Status, long AmountKobo, string Currency,
    DateTimeOffset ExpiresAt, bool Replayed);
public sealed record PaymentInitialization(Guid PaymentAttemptId, string Reference, string AuthorizationUrl,
    bool IsSimulator);
public sealed record PaymentVerification(string Reference, string ProviderTransactionId, bool Success, bool IsTest,
    long AmountKobo, string Currency, string? OrderAssociation);
public sealed record OrderView(Guid Id, string EventTitle, OrderStatus Status, long AmountKobo, string Currency,
    DateTimeOffset ExpiresAt, IReadOnlyList<Guid> TicketIds);
public sealed record TicketPass(Guid Id, string EventTitle, string Location, DateTimeOffset StartsAt,
    string TimeZone, string Tier, string QrData, string QrImage);
public sealed record CheckInResult(string Result, DateTimeOffset? CheckedInAt, string Message);
public sealed record SalesSummary(int ConfirmedOrders, int TicketsSold, long GrossKobo, int PendingExceptions);
