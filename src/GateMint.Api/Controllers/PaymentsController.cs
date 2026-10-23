using System.Text;
using System.Text.Json;
using GateMint.Core;
using GateMint.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GateMint.Api.Controllers;

[ApiController, Route("api/v1/payments")]
public sealed class PaymentsController(ApplicationDbContext db, PaystackSignatureVerifier signatures,
    IConfiguration configuration, IPaymentGateway gateway) : ControllerBase
{
    [AllowAnonymous, IgnoreAntiforgeryToken, HttpPost("paystack/webhook")]
    public async Task<IActionResult> Webhook(CancellationToken cancellationToken)
    {
        using var memory = new MemoryStream();
        await Request.Body.CopyToAsync(memory, cancellationToken);
        var body = memory.ToArray();
        if (!signatures.Verify(body, Request.Headers["Paystack-Signature"].FirstOrDefault(),
            configuration["Paystack:WebhookSecret"], Request.Headers["X-Paystack-Signature"].FirstOrDefault(),
            configuration["Paystack:SecretKey"])) return Unauthorized();
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var eventType = root.GetProperty("event").GetString() ?? "unknown";
        var data = root.GetProperty("data");
        var reference = data.TryGetProperty("reference", out var refValue) ? refValue.GetString() : null;
        var providerId = data.TryGetProperty("id", out var idValue) ? idValue.ToString() : reference;
        var dedupe = $"{eventType}:{providerId}";
        if (!await db.WebhookInbox.AnyAsync(x => x.Provider == "paystack" && x.DeduplicationKey == dedupe, cancellationToken))
        {
            db.WebhookInbox.Add(new WebhookInbox { Provider = "paystack", DeduplicationKey = dedupe,
                EventType = eventType, Payload = JsonSerializer.Serialize(new { reference }), Status = WorkStatus.Pending });
            await db.SaveChangesAsync(cancellationToken);
        }
        return Ok();
    }

    [Authorize, HttpGet("events/{eventId:guid}/exceptions")]
    public async Task<object> Exceptions(Guid eventId, CancellationToken cancellationToken)
    {
        var userId = User.UserId();
        if (!await db.Events.AnyAsync(x => x.Id == eventId && x.OrganizerId == userId, cancellationToken))
            throw new UnauthorizedAccessException("You do not manage this event.");
        return await db.PaymentAttempts.AsNoTracking().Where(x => x.Order.EventId == eventId &&
            (x.Status == PaymentStatus.NeedsRefund || x.Status == PaymentStatus.RefundPending))
            .Select(x => new { x.Id, x.Reference, x.AmountKobo, x.Currency, status = x.Status.ToString(), x.OrderId })
            .ToListAsync(cancellationToken);
    }

    [Authorize, HttpPost("events/{eventId:guid}/exceptions/{attemptId:guid}/refund")]
    public async Task<object> Refund(Guid eventId, Guid attemptId, CancellationToken cancellationToken)
    {
        var userId = User.UserId();
        var attempt = await db.PaymentAttempts.Include(x => x.Order).SingleOrDefaultAsync(
            x => x.Id == attemptId && x.Order.EventId == eventId && x.Order.Event.OrganizerId == userId, cancellationToken)
            ?? throw new UnauthorizedAccessException("Payment exception was not found.");
        var refund = await db.RefundRequests.SingleAsync(x => x.PaymentAttemptId == attemptId, cancellationToken);
        if (attempt.Status == PaymentStatus.Refunded) return new { status = "Refunded" };
        refund.Attempts++;
        var result = await gateway.RefundAsync(attempt, cancellationToken);
        if (result.Accepted)
        {
            refund.ProviderRefundId = result.ProviderRefundId;
            refund.Status = PaymentStatus.RefundPending;
            attempt.Status = PaymentStatus.RefundPending;
        }
        else refund.LastError = "Provider did not accept the refund request.";
        await db.SaveChangesAsync(cancellationToken);
        return new { status = attempt.Status.ToString(), accepted = result.Accepted };
    }
}
