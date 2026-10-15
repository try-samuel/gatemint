using GateMint.Core;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace GateMint.Infrastructure;

public static class DatabaseBootstrap
{
    public static async Task MigrateAsync(IServiceProvider services, IConfiguration configuration,
        IHostEnvironment environment, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync(cancellationToken);
        if (!environment.IsDevelopment() || !configuration.GetValue<bool>("Demo:Seed")) return;
        var password = configuration["Demo:Password"];
        if (string.IsNullOrWhiteSpace(password) || password.Length < 10)
            throw new InvalidOperationException("Demo:Password must be explicitly set to at least 10 characters when demo seeding is enabled.");
        var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var organizer = await EnsureUser(manager, "organizer@gatemint.local", "Amara Organizer", password);
        var attendee = await EnsureUser(manager, "attendee@gatemint.local", "Tobi Attendee", password);
        var staff = await EnsureUser(manager, "staff@gatemint.local", "Kemi Gate Staff", password);
        if (await db.Events.AnyAsync(cancellationToken)) return;
        var now = DateTimeOffset.UtcNow;
        var eventItem = new Event
        {
            OrganizerId = organizer.Id, Slug = "lagos-night-market", Title = "Lagos Night Market",
            Description = "An after-dark gathering of food, design, music and independent makers.",
            Location = "Harbour Point, Victoria Island", Category = "Culture", TimeZone = "Africa/Lagos",
            StartsAt = now.AddDays(21), EndsAt = now.AddDays(21).AddHours(6), SalesStart = now.AddDays(-1),
            SalesEnd = now.AddDays(21).AddHours(-1), Status = EventStatus.Published,
            TicketTypes = [new TicketType { Name = "General admission", PriceKobo = 750000, Capacity = 120,
                SalesStart = now.AddDays(-1), SalesEnd = now.AddDays(21).AddHours(-1) }],
            Staff = [new EventStaff { UserId = staff.Id }]
        };
        db.Events.Add(eventItem);
        db.AuditEntries.Add(new AuditEntry { ActorId = organizer.Id, Action = "demo.seeded", EntityType = nameof(Event), EntityId = eventItem.Id.ToString() });
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task<ApplicationUser> EnsureUser(UserManager<ApplicationUser> manager, string email, string name, string password)
    {
        var existing = await manager.FindByEmailAsync(email); if (existing is not null) return existing;
        var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true, DisplayName = name };
        var result = await manager.CreateAsync(user, password);
        if (!result.Succeeded) throw new InvalidOperationException(string.Join("; ", result.Errors.Select(x => x.Description)));
        return user;
    }
}
