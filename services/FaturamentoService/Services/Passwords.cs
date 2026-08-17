using System.Security.Cryptography;
using FaturamentoService.Models;
namespace FaturamentoService.Services;
public static class Passwords { public static string Hash(string password, byte[] salt) => Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(password, salt, 210000, HashAlgorithmName.SHA256, 32)); public static bool Verify(string password, User user) => CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(user.PasswordHash), Convert.FromBase64String(Hash(password, Convert.FromBase64String(user.PasswordSalt)))); }
