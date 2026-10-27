using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GateMint.Core;
using Microsoft.Extensions.Configuration;
using QRCoder;

namespace GateMint.Infrastructure;

public sealed class QrTicketService(IConfiguration configuration) : IQrTicketService
{
    private const string KeyId = "v1";
    private byte[] Key => Convert.FromBase64String(configuration["Tickets:SigningKey"]
        ?? throw new InvalidOperationException("Tickets:SigningKey is required."));
    public string Create(Ticket ticket)
    {
        var payload = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { ticket = ticket.Id, @event = ticket.EventId,
            nonce = ticket.QrNonce, kid = KeyId }));
        var signature = Base64Url(HMACSHA256.HashData(Key, Encoding.UTF8.GetBytes(payload)));
        return $"gm1.{payload}.{signature}";
    }
    public (bool Valid, Guid TicketId, Guid EventId, Guid Nonce) Verify(string value)
    {
        try
        {
            var parts = value.Split('.');
            if (parts.Length != 3 || parts[0] != "gm1") return default;
            var expected = HMACSHA256.HashData(Key, Encoding.UTF8.GetBytes(parts[1]));
            if (!CryptographicOperations.FixedTimeEquals(expected, FromBase64Url(parts[2]))) return default;
            var data = JsonDocument.Parse(FromBase64Url(parts[1])).RootElement;
            return (true, data.GetProperty("ticket").GetGuid(), data.GetProperty("event").GetGuid(), data.GetProperty("nonce").GetGuid());
        }
        catch { return default; }
    }
    public string ToSvgDataUri(string value)
    {
        using var data = QRCodeGenerator.GenerateQrCode(value, QRCodeGenerator.ECCLevel.Q);
        var svg = new SvgQRCode(data).GetGraphic(5, "#081b2b", "#ffffff", true);
        return "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(svg));
    }
    private static string Base64Url(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] FromBase64Url(string value) => Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + new string('=', (4 - value.Length % 4) % 4));
}
