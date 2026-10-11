using GateMint.Core;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace GateMint.Infrastructure;

public sealed class ApplicationUser : IdentityUser
{
    public required string DisplayName { get; set; }
}

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Event> Events => Set<Event>();
    public DbSet<EventStaff> EventStaff => Set<EventStaff>();
    public DbSet<TicketType> TicketTypes => Set<TicketType>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<Reservation> Reservations => Set<Reservation>();
    public DbSet<PaymentAttempt> PaymentAttempts => Set<PaymentAttempt>();
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<CheckIn> CheckIns => Set<CheckIn>();
    public DbSet<WebhookInbox> WebhookInbox => Set<WebhookInbox>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();
    public DbSet<RefundRequest> RefundRequests => Set<RefundRequest>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasPostgresEnum<EventStatus>();
        builder.HasPostgresEnum<OrderStatus>();
        builder.HasPostgresEnum<ReservationState>();
        builder.HasPostgresEnum<PaymentStatus>();
        builder.HasPostgresEnum<TicketStatus>();
        builder.HasPostgresEnum<WorkStatus>();

        builder.Entity<Event>(b =>
        {
            b.HasIndex(x => x.Slug).IsUnique();
            b.Property(x => x.Slug).HasMaxLength(120);
            b.Property(x => x.Title).HasMaxLength(180);
            b.Property(x => x.TimeZone).HasMaxLength(80);
        });
        builder.Entity<EventStaff>().HasIndex(x => new { x.EventId, x.UserId }).IsUnique();
        builder.Entity<TicketType>(b =>
        {
            b.ToTable(t => t.HasCheckConstraint("CK_TicketTypes_Inventory",
                "\"ReservedCount\" >= 0 AND \"SoldCount\" >= 0 AND \"SoldCount\" + \"ReservedCount\" <= \"Capacity\""));
        });
        builder.Entity<Order>(b =>
        {
            b.HasIndex(x => new { x.AttendeeId, x.IdempotencyKey }).IsUnique();
            b.HasOne(x => x.Reservation).WithOne(x => x.Order).HasForeignKey<Reservation>(x => x.OrderId);
        });
        builder.Entity<Reservation>().HasIndex(x => x.OrderId).IsUnique();
        builder.Entity<PaymentAttempt>(b =>
        {
            b.HasIndex(x => x.Reference).IsUnique();
            b.HasIndex(x => x.ProviderTransactionId).IsUnique();
        });
        builder.Entity<Ticket>().HasIndex(x => new { x.OrderItemId, x.QrNonce }).IsUnique();
        builder.Entity<CheckIn>().HasIndex(x => x.TicketId).IsUnique();
        builder.Entity<WebhookInbox>().HasIndex(x => new { x.Provider, x.DeduplicationKey }).IsUnique();
        builder.Entity<RefundRequest>().HasIndex(x => x.PaymentAttemptId).IsUnique();
    }
}
