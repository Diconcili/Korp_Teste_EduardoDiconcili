using System.Security.Cryptography;
namespace FaturamentoService.Services;

public static class Totp
{
    const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static bool IsValidSecret(string? secret)
    {
        var value = secret?.TrimEnd('=').ToUpperInvariant();
        return value is { Length: >= 16 } && value.All(Base32Alphabet.Contains);
    }

    public static bool IsValid(string secret, string code) =>
        IsValidSecret(secret) && Enumerable.Range(-1, 3)
            .Any(offset => Code(secret, DateTimeOffset.UtcNow.AddSeconds(offset * 30).ToUnixTimeSeconds() / 30) == code);

    static string Code(string secret, long counter)
    {
        var key = Base32(secret);
        var bytes = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
        using var hmac = new HMACSHA1(key);
        var hash = hmac.ComputeHash(bytes);
        var index = hash[^1] & 15;
        var value = ((hash[index] & 127) << 24) | (hash[index + 1] << 16) | (hash[index + 2] << 8) | hash[index + 3];
        return (value % 1000000).ToString("D6");
    }

    static byte[] Base32(string text)
    {
        var bits = 0;
        var value = 0;
        var data = new List<byte>();
        foreach (var character in text.TrimEnd('=').ToUpperInvariant())
        {
            value = (value << 5) | Base32Alphabet.IndexOf(character);
            bits += 5;
            if (bits < 8) continue;
            data.Add((byte)((value >> (bits - 8)) & 255));
            bits -= 8;
        }
        return [.. data];
    }
}
