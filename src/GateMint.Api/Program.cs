using System.Threading.RateLimiting;
using System.Text.Json.Serialization;
using GateMint.Api;
using GateMint.Infrastructure;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddGateMintInfrastructure(builder.Configuration);
builder.Services.AddControllersWithViews(options => options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()))
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddHealthChecks().AddDbContextCheck<ApplicationDbContext>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-XSRF-TOKEN";
    options.Cookie.Name = "XSRF-TOKEN";
    options.Cookie.HttpOnly = false;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});
builder.Services.AddDataProtection().PersistKeysToFileSystem(
    new DirectoryInfo(builder.Configuration["DataProtection:KeysPath"] ?? "./data-protection-keys"))
    .SetApplicationName("GateMint");
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new() { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
    options.AddPolicy("checkout", context => RateLimitPartition.GetTokenBucketLimiter(
        context.User.Identity?.Name ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new()
        { TokenLimit = 10, TokensPerPeriod = 5, ReplenishmentPeriod = TimeSpan.FromMinutes(1), AutoReplenishment = true }));
    options.AddPolicy("scan", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.Identity?.Name ?? "unknown", _ => new() { PermitLimit = 120, Window = TimeSpan.FromMinutes(1) }));
});

var app = builder.Build();
app.UseExceptionHandler();
if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health/live", new() { Predicate = _ => false });
app.MapHealthChecks("/health/ready");
app.MapFallbackToFile("index.html");

if (args.Contains("--migrate", StringComparer.OrdinalIgnoreCase))
{
    await DatabaseBootstrap.MigrateAsync(app.Services, app.Configuration, app.Environment, CancellationToken.None);
    return;
}
await app.RunAsync();

public partial class Program;
