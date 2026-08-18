using FaturamentoService.Data;
using Microsoft.EntityFrameworkCore;
namespace FaturamentoService.Services;

public static class Auth
{
    public static string? GetToken(HttpRequest request)
    {
        var authorization = request.Headers.Authorization.ToString();
        return authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? authorization[7..].Trim() : null;
    }

    public static async Task<bool> Valid(HttpRequest request, BillingDb db)
    {
        var token = GetToken(request);
        return !string.IsNullOrWhiteSpace(token) && await db.Sessions.AnyAsync(session => session.Token == token && session.ExpiresAt > DateTime.UtcNow);
    }
}
