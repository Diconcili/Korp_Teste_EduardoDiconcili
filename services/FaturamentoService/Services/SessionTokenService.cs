using System.Security.Cryptography;
using System.Text;

namespace FaturamentoService.Services;

public sealed class SessionTokenService(string signingSecret)
{
    readonly byte[] key = SHA256.HashData(Encoding.UTF8.GetBytes(signingSecret));

    public string Create(DateTime expiresAt)
    {
        var payload = $"{Convert.ToHexString(RandomNumberGenerator.GetBytes(32))}.{new DateTimeOffset(expiresAt).ToUnixTimeSeconds()}";
        var signature = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload));
        return $"{payload}.{Convert.ToHexString(signature)}";
    }
}
