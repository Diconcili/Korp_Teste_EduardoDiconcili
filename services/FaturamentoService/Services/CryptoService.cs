using System.Security.Cryptography;
using System.Text;

namespace FaturamentoService.Services;

public class CryptoService
{
    readonly byte[] key;

    public CryptoService(string encryptionSecret)
    {
        if (string.IsNullOrWhiteSpace(encryptionSecret) || encryptionSecret.Length < 32)
            throw new InvalidOperationException("KORP_ENCRYPTION_KEY deve ser configurada com ao menos 32 caracteres.");
        key = SHA256.HashData(Encoding.UTF8.GetBytes(encryptionSecret));
    }

    public string Encrypt<T>(T value)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plain = Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(value));
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(nonce, plain, cipher, tag);
        return Convert.ToBase64String(nonce.Concat(tag).Concat(cipher).ToArray());
    }

    public T? Decrypt<T>(string data)
    {
        var raw = Convert.FromBase64String(data);
        var plain = new byte[raw.Length - 28];
        using var aes = new AesGcm(key, 16);
        aes.Decrypt(raw[..12], raw[28..], raw[12..28], plain);
        return System.Text.Json.JsonSerializer.Deserialize<T>(plain);
    }
}
