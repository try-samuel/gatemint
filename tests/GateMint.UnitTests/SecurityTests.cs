using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using GateMint.Core;
using GateMint.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace GateMint.UnitTests;

public sealed class SecurityTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TicketQr_RoundTrips_WithoutPersonalData()
    {
        var service = Qr();
        var ticket = new Ticket { EventId = Guid.NewGuid(), IssuedAt = Now };
        var value = service.Create(ticket);
        var result = service.Verify(value);
        result.Valid.Should().BeTrue();
        result.TicketId.Should().Be(ticket.Id);
        result.EventId.Should().Be(ticket.EventId);
        value.Should().NotContain("@").And.NotContain("name");
    }

    [Fact]
    public void TicketQr_RejectsTampering()
    {
        var service = Qr();
        var value = service.Create(new Ticket { EventId = Guid.NewGuid(), IssuedAt = Now });
        service.Verify(value[..^1] + (value[^1] == 'a' ? 'b' : 'a')).Valid.Should().BeFalse();
    }

    [Fact]
    public void PaystackSignature_AcceptsCurrentTimestampedFormat()
    {
        var body = Encoding.UTF8.GetBytes("{\"event\":\"charge.success\"}");
        var secret = RandomSecret();
        var timestamp = Now.ToUnixTimeSeconds();
        var payload = Encoding.UTF8.GetBytes($"{timestamp}.").Concat(body).ToArray();
        var signature = Convert.ToHexStringLower(HMACSHA512.HashData(Encoding.UTF8.GetBytes(secret), payload));
        new PaystackSignatureVerifier(new FixedTimeProvider(Now)).Verify(body,
            $"t={timestamp},v1=whk_test:{signature}", secret, null, null).Should().BeTrue();
    }

    [Theory]
    [InlineData(-301)]
    [InlineData(301)]
    public void PaystackSignature_RejectsReplayOutsideWindow(int seconds)
    {
        var body = "{}"u8.ToArray(); var secret = RandomSecret();
        var timestamp = Now.AddSeconds(seconds).ToUnixTimeSeconds();
        var payload = Encoding.UTF8.GetBytes($"{timestamp}.").Concat(body).ToArray();
        var signature = Convert.ToHexStringLower(HMACSHA512.HashData(Encoding.UTF8.GetBytes(secret), payload));
        new PaystackSignatureVerifier(new FixedTimeProvider(Now)).Verify(body,
            $"t={timestamp},v1=whk_test:{signature}", secret, null, null).Should().BeFalse();
    }

    [Fact]
    public void PaystackSignature_RejectsMissingAndInvalidSignatures()
    {
        var verifier = new PaystackSignatureVerifier(new FixedTimeProvider(Now));
        verifier.Verify("{}"u8, null, RandomSecret(), null, RandomSecret()).Should().BeFalse();
        verifier.Verify("{}"u8, "t=broken,v1=nope", RandomSecret(), null, null).Should().BeFalse();
    }

    private static QrTicketService Qr() => new(new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> { ["Tickets:SigningKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }).Build());

    private static string RandomSecret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
}

public sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
{
    public DateTimeOffset Value { get; set; } = value;
    public override DateTimeOffset GetUtcNow() => Value;
}
