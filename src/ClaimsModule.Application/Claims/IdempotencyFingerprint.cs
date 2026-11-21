using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ClaimsModule.Application.Claims;

internal static class IdempotencyFingerprint
{
    public static string? HashKey(string? key) => key is null ? null :
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    public static string HashRequest<T>(T request) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request)));
}
