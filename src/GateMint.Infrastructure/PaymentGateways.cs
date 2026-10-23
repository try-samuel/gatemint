using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GateMint.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace GateMint.Infrastructure;

public sealed class LocalSimulatorGateway(ApplicationDbContext db) : IPaymentGateway
{
    public string Name => "simulator";
    public Task<(string AuthorizationUrl, string? ProviderTransactionId)> InitializeAsync(PaymentAttempt attempt,
        string email, Uri callbackUrl, CancellationToken cancellationToken) =>
        Task.FromResult(($"/simulator?reference={Uri.EscapeDataString(attempt.Reference)}", (string?)null));
    public async Task<PaymentVerification> VerifyAsync(string reference, CancellationToken cancellationToken)
    {
        var attempt = await db.PaymentAttempts.AsNoTracking().SingleAsync(x => x.Reference == reference, cancellationToken);
        return new(reference, $"sim_{reference}", true, true, attempt.AmountKobo, attempt.Currency, attempt.OrderId.ToString());
    }
    public Task<(bool Accepted, string? ProviderRefundId)> RefundAsync(PaymentAttempt attempt,
        CancellationToken cancellationToken) => Task.FromResult((true, (string?)$"sim_refund_{attempt.Reference}"));
}

public sealed class PaystackGateway(HttpClient http, IConfiguration configuration) : IPaymentGateway
{
    public string Name => "paystack";
    public async Task<(string AuthorizationUrl, string? ProviderTransactionId)> InitializeAsync(PaymentAttempt attempt,
        string email, Uri callbackUrl, CancellationToken cancellationToken)
    {
        using var request = Create(HttpMethod.Post, "transaction/initialize");
        request.Content = JsonContent.Create(new { email, amount = attempt.AmountKobo, currency = attempt.Currency,
            reference = attempt.Reference, callback_url = callbackUrl.ToString(),
            metadata = new { order_id = attempt.OrderId.ToString() } });
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        return (result.GetProperty("data").GetProperty("authorization_url").GetString()!, null);
    }
    public async Task<PaymentVerification> VerifyAsync(string reference, CancellationToken cancellationToken)
    {
        using var response = await http.SendAsync(Create(HttpMethod.Get, $"transaction/verify/{Uri.EscapeDataString(reference)}"), cancellationToken);
        response.EnsureSuccessStatusCode();
        var root = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        var data = root.GetProperty("data");
        var association = data.TryGetProperty("metadata", out var metadata) && metadata.ValueKind == JsonValueKind.Object &&
            metadata.TryGetProperty("order_id", out var orderId) ? orderId.GetString() : null;
        return new(reference, data.GetProperty("id").ToString(), data.GetProperty("status").GetString() == "success",
            data.GetProperty("domain").GetString() == "test", data.GetProperty("amount").GetInt64(),
            data.GetProperty("currency").GetString()!, association);
    }
    public async Task<(bool Accepted, string? ProviderRefundId)> RefundAsync(PaymentAttempt attempt,
        CancellationToken cancellationToken)
    {
        using var request = Create(HttpMethod.Post, "refund");
        request.Content = JsonContent.Create(new { transaction = attempt.ProviderTransactionId, amount = attempt.AmountKobo });
        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) return (false, null);
        var root = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        return (true, root.GetProperty("data").GetProperty("id").ToString());
    }
    private HttpRequestMessage Create(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", configuration["Paystack:SecretKey"]);
        return request;
    }
}

public sealed class PaystackSignatureVerifier(TimeProvider clock)
{
    public bool Verify(ReadOnlySpan<byte> body, string? modernHeader, string? signingSecret,
        string? legacyHeader, string? secretKey)
    {
        if (!string.IsNullOrWhiteSpace(modernHeader) && !string.IsNullOrWhiteSpace(signingSecret))
        {
            var fields = modernHeader.Split(',', StringSplitOptions.TrimEntries);
            var timestampText = fields.FirstOrDefault(x => x.StartsWith("t="))?[2..];
            if (!long.TryParse(timestampText, out var timestamp)) return false;
            if (Math.Abs(clock.GetUtcNow().ToUnixTimeSeconds() - timestamp) > 300) return false;
            var message = Encoding.UTF8.GetBytes($"{timestamp}.").Concat(body.ToArray()).ToArray();
            var expected = HMACSHA512.HashData(Encoding.UTF8.GetBytes(signingSecret), message);
            return fields.Where(x => x.StartsWith("v1=")).Select(x => x[3..])
                .Select(x => x.Contains(':') ? x[(x.IndexOf(':') + 1)..] : x)
                .Any(x => FixedEqualsHex(expected, x));
        }
        if (string.IsNullOrWhiteSpace(legacyHeader) || string.IsNullOrWhiteSpace(secretKey)) return false;
        return FixedEqualsHex(HMACSHA512.HashData(Encoding.UTF8.GetBytes(secretKey), body), legacyHeader);
    }
    private static bool FixedEqualsHex(byte[] expected, string value)
    {
        if (value.Length != 128) return false;
        try { return CryptographicOperations.FixedTimeEquals(expected, Convert.FromHexString(value)); }
        catch (FormatException) { return false; }
    }
}
