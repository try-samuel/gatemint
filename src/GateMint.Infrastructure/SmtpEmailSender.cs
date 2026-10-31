using System.Net;
using System.Net.Mail;
using GateMint.Core;
using Microsoft.Extensions.Configuration;

namespace GateMint.Infrastructure;

public sealed class SmtpEmailSender(IConfiguration configuration) : IEmailSender
{
    public async Task SendTicketReadyAsync(string recipient, Guid orderId, CancellationToken cancellationToken)
    {
        using var client = new SmtpClient(configuration["Smtp:Host"] ?? "mailpit",
            configuration.GetValue("Smtp:Port", 1025)) { EnableSsl = configuration.GetValue<bool>("Smtp:UseTls") };
        var username = configuration["Smtp:Username"];
        if (!string.IsNullOrWhiteSpace(username))
            client.Credentials = new NetworkCredential(username, configuration["Smtp:Password"]);
        using var message = new MailMessage(configuration["Smtp:From"] ?? "tickets@gatemint.local", recipient,
            "Your GateMint tickets are ready",
            $"Your payment is confirmed. Open GateMint and view order {orderId} to access your tickets.");
        await client.SendMailAsync(message, cancellationToken);
    }
}
