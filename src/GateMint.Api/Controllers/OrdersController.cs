using GateMint.Core;
using GateMint.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace GateMint.Api.Controllers;

[ApiController, Authorize, Route("api/v1/orders")]
public sealed class OrdersController(ApplicationDbContext db, ICheckoutService checkout, IPaymentGateway payments,
    IFulfillmentService fulfillment, UserManager<ApplicationUser> users, IConfiguration configuration) : ControllerBase
{
    [HttpPost, EnableRateLimiting("checkout")]
    public async Task<ActionResult<CheckoutResult>> Create(CheckoutRequest request,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey, CancellationToken cancellationToken) =>
        Ok(await checkout.ReserveAsync(User.UserId(), request, idempotencyKey, cancellationToken));

    [HttpGet]
    public async Task<object> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(page, 1); pageSize = Math.Clamp(pageSize, 1, 50);
        var query = db.Orders.AsNoTracking().Where(x => x.AttendeeId == User.UserId());
        return new { items = await query.OrderByDescending(x => x.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new OrderView(x.Id, x.Event.Title, x.Status, x.AmountKobo, x.Currency, x.ExpiresAt,
                x.Items.SelectMany(i => i.Tickets).Select(t => t.Id).ToList())).ToListAsync(cancellationToken),
            total = await query.CountAsync(cancellationToken), page, pageSize };
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<OrderView>> Detail(Guid id, CancellationToken cancellationToken)
    {
        var order = await db.Orders.AsNoTracking().Where(x => x.Id == id && x.AttendeeId == User.UserId())
            .Select(x => new OrderView(x.Id, x.Event.Title, x.Status, x.AmountKobo, x.Currency, x.ExpiresAt,
                x.Items.SelectMany(i => i.Tickets).Select(t => t.Id).ToList())).SingleOrDefaultAsync(cancellationToken);
        return order is null ? NotFound() : Ok(order);
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
    { await checkout.CancelAsync(User.UserId(), id, cancellationToken); return NoContent(); }

    [HttpPost("{id:guid}/payments")]
    public async Task<ActionResult<PaymentInitialization>> Initialize(Guid id, CancellationToken cancellationToken)
    {
        var order = await db.Orders.Include(x => x.PaymentAttempts).SingleOrDefaultAsync(
            x => x.Id == id && x.AttendeeId == User.UserId(), cancellationToken) ?? throw new DomainException("Order was not found.");
        if (order.Status != OrderStatus.AwaitingPayment) throw new DomainException("This order is not awaiting payment.");
        var pending = order.PaymentAttempts.FirstOrDefault(x => x.Status is PaymentStatus.Initializing or PaymentStatus.Pending);
        if (pending is not null) return Ok(new PaymentInitialization(pending.Id, pending.Reference,
            payments.Name == "simulator" ? $"/simulator?reference={pending.Reference}" : "", payments.Name == "simulator"));
        var attempt = new PaymentAttempt { OrderId = order.Id, Gateway = payments.Name,
            Reference = $"gm_{Guid.NewGuid():N}", AmountKobo = order.AmountKobo, Currency = order.Currency,
            IsTest = true, Status = PaymentStatus.Initializing };
        db.PaymentAttempts.Add(attempt); await db.SaveChangesAsync(cancellationToken);
        var user = await users.FindByIdAsync(User.UserId()) ?? throw new UnauthorizedAccessException();
        var callback = new Uri($"{Request.Scheme}://{Request.Host}/payment-return?reference={attempt.Reference}");
        var initialized = await payments.InitializeAsync(attempt, user.Email!, callback, cancellationToken);
        attempt.Status = PaymentStatus.Pending; attempt.ProviderTransactionId = initialized.ProviderTransactionId;
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new PaymentInitialization(attempt.Id, attempt.Reference, initialized.AuthorizationUrl, payments.Name == "simulator"));
    }

    [HttpPost("payments/{reference}/verify")]
    public async Task<object> Verify(string reference, CancellationToken cancellationToken)
    {
        _ = await db.PaymentAttempts.Include(x => x.Order).SingleOrDefaultAsync(
            x => x.Reference == reference && x.Order.AttendeeId == User.UserId(), cancellationToken)
            ?? throw new DomainException("Payment reference was not found.");
        var verified = await payments.VerifyAsync(reference, cancellationToken);
        var status = await fulfillment.ApplyVerifiedPaymentAsync(verified, cancellationToken);
        return new { status = status.ToString() };
    }

    [AllowAnonymous, IgnoreAntiforgeryToken, HttpPost("simulator/{reference}/complete")]
    public async Task<IActionResult> CompleteSimulation(string reference, CancellationToken cancellationToken)
    {
        if (!HttpContext.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment() ||
            !configuration.GetValue<bool>("Payments:AllowSimulator") || payments is not LocalSimulatorGateway)
            return NotFound();
        var verified = await payments.VerifyAsync(reference, cancellationToken);
        var status = await fulfillment.ApplyVerifiedPaymentAsync(verified, cancellationToken);
        return Ok(new { status = status.ToString() });
    }
}
