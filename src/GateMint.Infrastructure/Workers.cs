using System.Text.Json;
using GateMint.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GateMint.Infrastructure;

public sealed class ReservationWorker(IServiceScopeFactory scopes, ILogger<ReservationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { using var scope = scopes.CreateScope(); await scope.ServiceProvider.GetRequiredService<ICheckoutService>().ReleaseExpiredAsync(stoppingToken); }
            catch (Exception ex) { logger.LogError(ex, "Reservation release cycle failed"); }
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}

public sealed class OutboxWorker(IServiceScopeFactory scopes, ILogger<OutboxWorker> logger, TimeProvider clock) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var messages = await db.Outbox.Where(x => x.Status != WorkStatus.Completed && x.NextAttemptAt <= clock.GetUtcNow())
                .OrderBy(x => x.CreatedAt).Take(20).ToListAsync(stoppingToken);
            foreach (var message in messages)
            {
                try
                {
                    var payload = JsonDocument.Parse(message.Payload).RootElement;
                    var attendeeId = payload.GetProperty("AttendeeId").GetString()!;
                    var recipient = await db.Users.Where(x => x.Id == attendeeId).Select(x => x.Email).SingleAsync(stoppingToken);
                    await scope.ServiceProvider.GetRequiredService<IEmailSender>().SendTicketReadyAsync(
                        recipient!, payload.GetProperty("OrderId").GetGuid(), stoppingToken);
                    message.Status = WorkStatus.Completed; message.CompletedAt = clock.GetUtcNow();
                }
                catch (Exception ex)
                {
                    message.Attempts++; message.Status = WorkStatus.Failed;
                    message.NextAttemptAt = clock.GetUtcNow().AddMinutes(Math.Min(30, message.Attempts * 2));
                    logger.LogError(ex, "Outbox message {MessageId} failed", message.Id);
                }
            }
            await db.SaveChangesAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }
}

public sealed class WebhookWorker(IServiceScopeFactory scopes, ILogger<WebhookWorker> logger, TimeProvider clock) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var inbox = await db.WebhookInbox.Where(x => x.Status != WorkStatus.Completed && x.Attempts < 8)
                .OrderBy(x => x.CreatedAt).Take(20).ToListAsync(stoppingToken);
            foreach (var item in inbox)
            {
                try
                {
                    item.Status = WorkStatus.Processing; item.Attempts++;
                    var reference = JsonDocument.Parse(item.Payload).RootElement.GetProperty("reference").GetString();
                    if (item.EventType == "charge.success" && !string.IsNullOrWhiteSpace(reference))
                    {
                        var gateway = scope.ServiceProvider.GetRequiredService<IPaymentGateway>();
                        var verified = await gateway.VerifyAsync(reference, stoppingToken);
                        await scope.ServiceProvider.GetRequiredService<IFulfillmentService>()
                            .ApplyVerifiedPaymentAsync(verified, stoppingToken);
                    }
                    item.Status = WorkStatus.Completed; item.ProcessedAt = clock.GetUtcNow();
                }
                catch (Exception ex) { item.Status = WorkStatus.Failed; logger.LogError(ex, "Webhook inbox {InboxId} failed", item.Id); }
                await db.SaveChangesAsync(stoppingToken);
            }
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}

public sealed class RefundReconciliationWorker(IServiceScopeFactory scopes) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var gateway = scope.ServiceProvider.GetRequiredService<IPaymentGateway>();
            if (gateway is LocalSimulatorGateway)
            {
                var pending = await db.RefundRequests.Include(x => x.PaymentAttempt).ThenInclude(x => x.Order)
                    .Where(x => x.Status == PaymentStatus.RefundPending && x.ProviderRefundId != null).ToListAsync(stoppingToken);
                foreach (var refund in pending)
                {
                    refund.Status = PaymentStatus.Refunded;
                    refund.PaymentAttempt.Status = PaymentStatus.Refunded;
                    refund.PaymentAttempt.Order.Status = OrderStatus.Refunded;
                }
                await db.SaveChangesAsync(stoppingToken);
            }
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
    }
}
