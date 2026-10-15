using System.Security.Claims;

namespace GateMint.Api;

public static class ClaimsExtensions
{
    public static string UserId(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException("Sign in is required.");
}
