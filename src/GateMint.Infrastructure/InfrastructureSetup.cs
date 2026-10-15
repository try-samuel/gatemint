using GateMint.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GateMint.Infrastructure;

public static class InfrastructureSetup
{
    public static IServiceCollection AddGateMintInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(
            configuration.GetConnectionString("Default")));
        services.AddIdentityCore<ApplicationUser>(options =>
        {
            options.Password.RequiredLength = 10;
            options.Password.RequireNonAlphanumeric = true;
            options.User.RequireUniqueEmail = true;
        }).AddSignInManager().AddEntityFrameworkStores<ApplicationDbContext>().AddDefaultTokenProviders();
        services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.Name = "GateMint.Session";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
            options.Events.OnRedirectToAccessDenied = context => { context.Response.StatusCode = 403; return Task.CompletedTask; };
        });
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<ICheckoutService, CheckoutService>();
        services.AddScoped<IFulfillmentService, FulfillmentService>();
        services.AddScoped<IQrTicketService, QrTicketService>();
        services.AddScoped<LocalSimulatorGateway>();
        services.AddHttpClient<PaystackGateway>(client =>
        {
            client.BaseAddress = new Uri("https://api.paystack.co/");
            client.Timeout = TimeSpan.FromSeconds(15);
        });
        services.AddScoped<IPaymentGateway>(provider => configuration["Payments:Provider"] == "Paystack"
            ? provider.GetRequiredService<PaystackGateway>() : provider.GetRequiredService<LocalSimulatorGateway>());
        services.AddScoped<PaystackSignatureVerifier>();
        services.AddScoped<IEmailSender, SmtpEmailSender>();
        services.AddHostedService<ReservationWorker>();
        services.AddHostedService<OutboxWorker>();
        services.AddHostedService<WebhookWorker>();
        services.AddHostedService<RefundReconciliationWorker>();
        return services;
    }
}
