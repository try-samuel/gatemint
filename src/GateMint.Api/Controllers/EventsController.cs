using System.Text.RegularExpressions;
using GateMint.Core;
using GateMint.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GateMint.Api.Controllers;

[ApiController, Route("api/v1/events")]
public sealed class EventsController(ApplicationDbContext db, TimeProvider clock, UserManager<ApplicationUser> users) : ControllerBase
{
    [AllowAnonymous, HttpGet]
    public async Task<object> List([FromQuery] string? search, [FromQuery] string? category, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 12, CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 50);
        var query = db.Events.AsNoTracking().Where(x => x.Status == EventStatus.Published && x.EndsAt >= clock.GetUtcNow());
        if (!string.IsNullOrWhiteSpace(search)) query = query.Where(x => x.Title.Contains(search) || x.Location.Contains(search));
        if (!string.IsNullOrWhiteSpace(category)) query = query.Where(x => x.Category == category);
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(x => x.StartsAt).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new EventSummary(x.Id, x.Slug, x.Title, x.Location, x.Category, x.StartsAt, x.TimeZone,
                x.TicketTypes.Min(t => t.PriceKobo), x.TicketTypes.Sum(t => t.Capacity - t.SoldCount - t.ReservedCount)))
            .ToListAsync(cancellationToken);
        return new { items, page, pageSize, total };
    }

    [AllowAnonymous, HttpGet("{slug}")]
    public async Task<ActionResult<EventDetail>> Detail(string slug, CancellationToken cancellationToken)
    {
        var item = await db.Events.AsNoTracking().Where(x => x.Slug == slug && x.Status == EventStatus.Published)
            .Select(x => new EventDetail(x.Id, x.Slug, x.Title, x.Description, x.Location, x.Category,
                x.StartsAt, x.EndsAt, x.TimeZone, x.TicketTypes.OrderBy(t => t.PriceKobo)
                    .Select(t => new TicketTypeView(t.Id, t.Name, t.PriceKobo, t.Currency,
                        t.Capacity - t.SoldCount - t.ReservedCount, t.SalesStart, t.SalesEnd)).ToList()))
            .SingleOrDefaultAsync(cancellationToken);
        return item is null ? NotFound() : Ok(item);
    }

    [Authorize, HttpGet("mine")]
    public async Task<object> Mine(CancellationToken cancellationToken) => await db.Events.AsNoTracking()
        .Where(x => x.OrganizerId == User.UserId()).OrderByDescending(x => x.CreatedAt)
        .Select(x => new { x.Id, x.Slug, x.Title, status = x.Status.ToString(), x.StartsAt,
            sold = x.TicketTypes.Sum(t => t.SoldCount), capacity = x.TicketTypes.Sum(t => t.Capacity) })
        .ToListAsync(cancellationToken);

    [Authorize, HttpPost]
    public async Task<IActionResult> Create(CreateEventRequest request, CancellationToken cancellationToken)
    {
        if (request.EndsAt <= request.StartsAt || request.SalesEnd <= request.SalesStart)
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["dates"] = ["End dates must be after start dates."] }));
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(request.TimeZone); }
        catch { return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["timeZone"] = ["Use a valid IANA timezone."] })); }
        var slugBase = Regex.Replace(request.Title.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        var slug = $"{slugBase}-{Guid.NewGuid().ToString("N")[..6]}";
        var item = new Event { OrganizerId = User.UserId(), Slug = slug, Title = request.Title.Trim(),
            Description = request.Description.Trim(), Location = request.Location.Trim(), Category = request.Category.Trim(),
            StartsAt = request.StartsAt, EndsAt = request.EndsAt, TimeZone = request.TimeZone,
            SalesStart = request.SalesStart, SalesEnd = request.SalesEnd };
        db.Events.Add(item);
        await db.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(Detail), new { slug }, new { item.Id, item.Slug });
    }

    [Authorize, HttpPost("{id:guid}/tiers")]
    public async Task<IActionResult> AddTier(Guid id, CreateTicketTypeRequest request, CancellationToken cancellationToken)
    {
        var item = await Owned(id, cancellationToken);
        if (request.PriceKobo < 100 || request.Capacity < 1) return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]>
        { ["ticketType"] = ["Price must be at least ₦1 and capacity must be positive."] }));
        var tier = new TicketType { EventId = item.Id, Name = request.Name.Trim(), PriceKobo = request.PriceKobo,
            Capacity = request.Capacity, SalesStart = request.SalesStart, SalesEnd = request.SalesEnd };
        db.TicketTypes.Add(tier); await db.SaveChangesAsync(cancellationToken);
        return Created($"/api/v1/events/{id}/tiers/{tier.Id}", new { tier.Id });
    }

    [Authorize, HttpPost("{id:guid}/publish")]
    public async Task<IActionResult> Publish(Guid id, CancellationToken cancellationToken)
    {
        var item = await Owned(id, cancellationToken);
        if (!await db.TicketTypes.AnyAsync(x => x.EventId == id, cancellationToken))
            return UnprocessableEntity(new ProblemDetails { Title = "Add at least one ticket tier before publishing." });
        item.Status = EventStatus.Published; await db.SaveChangesAsync(cancellationToken); return NoContent();
    }

    [Authorize, HttpPost("{id:guid}/unpublish")]
    public async Task<IActionResult> Unpublish(Guid id, CancellationToken cancellationToken)
    { var item = await Owned(id, cancellationToken); item.Status = EventStatus.Unpublished; await db.SaveChangesAsync(cancellationToken); return NoContent(); }

    [Authorize, HttpPost("{id:guid}/staff")]
    public async Task<IActionResult> AddStaff(Guid id, AddStaffRequest request, CancellationToken cancellationToken)
    {
        _ = await Owned(id, cancellationToken);
        var user = await users.FindByEmailAsync(request.Email) ?? throw new DomainException("No user has that email address.");
        if (!await db.EventStaff.AnyAsync(x => x.EventId == id && x.UserId == user.Id, cancellationToken))
        { db.EventStaff.Add(new EventStaff { EventId = id, UserId = user.Id }); await db.SaveChangesAsync(cancellationToken); }
        return NoContent();
    }

    [Authorize, HttpGet("{id:guid}/dashboard")]
    public async Task<object> Dashboard(Guid id, CancellationToken cancellationToken)
    {
        _ = await Owned(id, cancellationToken);
        var orders = db.Orders.Where(x => x.EventId == id);
        var summary = new SalesSummary(await orders.CountAsync(x => x.Status == OrderStatus.Confirmed, cancellationToken),
            await db.Tickets.CountAsync(x => x.EventId == id, cancellationToken),
            await orders.Where(x => x.Status == OrderStatus.Confirmed).SumAsync(x => (long?)x.AmountKobo, cancellationToken) ?? 0,
            await orders.CountAsync(x => x.Status == OrderStatus.NeedsRefund, cancellationToken));
        var attendees = await orders.Where(x => x.Status == OrderStatus.Confirmed).Select(x => new
            { x.Id, x.AttendeeId, quantity = x.Items.Sum(i => i.Quantity), x.AmountKobo, x.CreatedAt }).ToListAsync(cancellationToken);
        return new { summary, attendees };
    }

    private async Task<Event> Owned(Guid id, CancellationToken token) =>
        await db.Events.SingleOrDefaultAsync(x => x.Id == id && x.OrganizerId == User.UserId(), token)
        ?? throw new UnauthorizedAccessException("You do not manage this event.");
}

public sealed record AddStaffRequest(string Email);
