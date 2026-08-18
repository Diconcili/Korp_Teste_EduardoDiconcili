using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace EstoqueService.Services;

public sealed class RequestAuth(string signingSecret, string serviceKey)
{
    readonly byte[] signingKey = SHA256.HashData(Encoding.UTF8.GetBytes(signingSecret));
    readonly byte[] expectedServiceKey = SHA256.HashData(Encoding.UTF8.GetBytes(serviceKey));

    public bool HasValidUserToken(HttpRequest request)
    {
        var authorization = request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return false;

        var parts = authorization[7..].Trim().Split('.');
        if (parts.Length != 3 || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var expiresAt)) return false;
        if (expiresAt <= DateTimeOffset.UtcNow.ToUnixTimeSeconds()) return false;

        var expected = HMACSHA256.HashData(signingKey, Encoding.UTF8.GetBytes($"{parts[0]}.{parts[1]}"));
        return TryCompareHex(parts[2], expected);
    }

    public bool HasValidServiceKey(HttpRequest request)
    {
        var supplied = request.Headers["X-Korp-Service-Key"].ToString();
        if (string.IsNullOrWhiteSpace(supplied)) return false;
        var actual = SHA256.HashData(Encoding.UTF8.GetBytes(supplied));
        return CryptographicOperations.FixedTimeEquals(actual, expectedServiceKey);
    }

    static bool TryCompareHex(string value, byte[] expected)
    {
        try { return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(value), expected); }
        catch (FormatException) { return false; }
    }
}
