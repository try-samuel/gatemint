namespace GateMint.Core;

public interface IPaymentGateway
{
    string Name { get; }
    Task<(string AuthorizationUrl, string? ProviderTransactionId)> InitializeAsync(
        PaymentAttempt attempt, string email, Uri callbackUrl, CancellationToken cancellationToken);
    Task<PaymentVerification> VerifyAsync(string reference, CancellationToken cancellationToken);
    Task<(bool Accepted, string? ProviderRefundId)> RefundAsync(
        PaymentAttempt attempt, CancellationToken cancellationToken);
}

public interface ICheckoutService
{
    Task<CheckoutResult> ReserveAsync(string attendeeId, CheckoutRequest request, string idempotencyKey,
        CancellationToken cancellationToken);
    Task CancelAsync(string attendeeId, Guid orderId, CancellationToken cancellationToken);
    Task<int> ReleaseExpiredAsync(CancellationToken cancellationToken);
}

public interface IFulfillmentService
{
    Task<OrderStatus> ApplyVerifiedPaymentAsync(PaymentVerification verification, CancellationToken cancellationToken);
    Task<CheckInResult> CheckInAsync(string staffUserId, Guid eventId, string qrData, CancellationToken cancellationToken);
}

public interface IQrTicketService
{
    string Create(Ticket ticket);
    (bool Valid, Guid TicketId, Guid EventId, Guid Nonce) Verify(string value);
    string ToSvgDataUri(string value);
}

public interface IEmailSender
{
    Task SendTicketReadyAsync(string recipient, Guid orderId, CancellationToken cancellationToken);
}
