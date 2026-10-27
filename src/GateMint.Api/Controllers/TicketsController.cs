using GateMint.Core;
using GateMint.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace GateMint.Api.Controllers;

[ApiController, Authorize, Route("api/v1/tickets")]
public sealed class TicketsController(ApplicationDbContext db, IQrTicketService qr, IFulfillmentService fulfillment) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TicketPass>> Detail(Guid id, CancellationToken cancellationToken)
    {
        var ticket = await Owned(id, cancellationToken);
        var code = qr.Create(ticket);
        return new TicketPass(ticket.Id, ticket.OrderItem.Order.Event.Title, ticket.OrderItem.Order.Event.Location,
            ticket.OrderItem.Order.Event.StartsAt, ticket.OrderItem.Order.Event.TimeZone, ticket.OrderItem.TicketTypeName,
            code, qr.ToSvgDataUri(code));
    }

    [HttpGet("{id:guid}/print")]
    public async Task<ContentResult> Print(Guid id, CancellationToken cancellationToken)
    {
        var ticket = await Owned(id, cancellationToken);
        var code = qr.Create(ticket);
        var html = $$"""
            <!doctype html><html><head><meta charset="utf-8"><title>GateMint ticket</title>
            <style>body{font-family:system-ui;background:#081b2b;color:#eafff7;padding:40px}article{max-width:520px;margin:auto;background:#102c3e;border:2px solid #52e3b3;border-radius:24px;padding:32px}img{width:260px;background:white;padding:12px}small{word-break:break-all}</style></head>
            <body><article><h1>{{Html(ticket.OrderItem.Order.Event.Title)}}</h1><p>{{Html(ticket.OrderItem.Order.Event.Location)}}</p>
            <p>{{ticket.OrderItem.Order.Event.StartsAt:u}} · {{Html(ticket.OrderItem.Order.Event.TimeZone)}}</p><p>{{Html(ticket.OrderItem.TicketTypeName)}}</p>
            <img src="{{qr.ToSvgDataUri(code)}}" alt="Ticket QR code"><p><small>{{Html(code)}}</small></p></article><script>window.print()</script></body></html>
            """;
        return Content(html, "text/html", System.Text.Encoding.UTF8);
    }

    [HttpPost("events/{eventId:guid}/check-in"), EnableRateLimiting("scan")]
    public async Task<ActionResult<CheckInResult>> CheckIn(Guid eventId, CheckInRequest request,
        CancellationToken cancellationToken) => Ok(await fulfillment.CheckInAsync(User.UserId(), eventId, request.Code, cancellationToken));

    private async Task<Ticket> Owned(Guid id, CancellationToken token) => await db.Tickets.AsNoTracking()
        .Include(x => x.OrderItem).ThenInclude(x => x.Order).ThenInclude(x => x.Event)
        .SingleOrDefaultAsync(x => x.Id == id && x.OrderItem.Order.AttendeeId == User.UserId(), token)
        ?? throw new UnauthorizedAccessException("Ticket was not found.");
    private static string Html(string value) => System.Net.WebUtility.HtmlEncode(value);
}

public sealed record CheckInRequest(string Code);
