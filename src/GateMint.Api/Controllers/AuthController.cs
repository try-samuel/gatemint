using System.ComponentModel.DataAnnotations;
using GateMint.Infrastructure;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace GateMint.Api.Controllers;

[ApiController, Route("api/v1/auth")]
public sealed class AuthController(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn,
    IAntiforgery antiforgery) : ControllerBase
{
    [HttpGet("antiforgery")]
    public object Antiforgery()
    {
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        return new { token = tokens.RequestToken };
    }

    [HttpPost("register"), EnableRateLimiting("auth")]
    public async Task<IActionResult> Register(RegisterRequest request)
    {
        var user = new ApplicationUser { UserName = request.Email.Trim().ToLowerInvariant(),
            Email = request.Email.Trim().ToLowerInvariant(), DisplayName = request.DisplayName.Trim() };
        var result = await users.CreateAsync(user, request.Password);
        if (!result.Succeeded) return ValidationProblem(new ValidationProblemDetails(
            new Dictionary<string, string[]> { ["registration"] = result.Errors.Select(x => x.Description).ToArray() }));
        await signIn.SignInAsync(user, false);
        return Created("/api/v1/auth/me", new { user.Id, user.Email, user.DisplayName });
    }

    [HttpPost("login"), EnableRateLimiting("auth")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var result = await signIn.PasswordSignInAsync(request.Email.Trim().ToLowerInvariant(), request.Password, request.RememberMe, true);
        return result.Succeeded ? Ok(await Current()) : Problem(statusCode: 401, title: "Invalid email or password");
    }

    [Authorize, HttpPost("logout")]
    public async Task<IActionResult> Logout() { await signIn.SignOutAsync(); return NoContent(); }

    [Authorize, HttpGet("me")]
    public async Task<object> Me() => await Current();

    [HttpPost("password-reset")]
    public IActionResult PasswordReset(PasswordResetRequest request) =>
        Accepted(new { message = "If that account exists, password reset instructions will be sent." });

    private async Task<object> Current()
    {
        var user = await users.GetUserAsync(User) ?? throw new UnauthorizedAccessException();
        return new { user.Id, user.Email, user.DisplayName };
    }
}

public sealed record RegisterRequest([Required, EmailAddress] string Email, [Required, MinLength(2)] string DisplayName,
    [Required, MinLength(10)] string Password);
public sealed record LoginRequest([Required, EmailAddress] string Email, [Required] string Password, bool RememberMe);
public sealed record PasswordResetRequest([Required, EmailAddress] string Email);
