using System.Security.Cryptography;
using System.Text;
using GymLink.Application.Identity;
using Microsoft.Extensions.Options;

namespace GymLink.Infrastructure.Messaging;

public sealed class PasswordResetOptions
{
    public const string SectionName = "PasswordReset";

    public string CodePepper { get; init; } = string.Empty;
}

public sealed class PasswordResetCodeService(
    IOptions<PasswordResetOptions> options) : IPasswordResetCodeService
{
    private readonly byte[] key = Encoding.UTF8.GetBytes(options.Value.CodePepper);

    public string DeriveCode(Guid challengeId)
    {
        using var hmac = new HMACSHA256(key);
        var hash = hmac.ComputeHash(challengeId.ToByteArray());
        var value = BitConverter.ToUInt32(hash, 0) % 1_000_000;
        return value.ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
    }

    public string CreateSalt() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    public string HashCode(string code, string salt) =>
        Hash($"{salt}:{code}");

    public bool Verify(string code, string salt, string expectedHash)
    {
        var actual = Convert.FromHexString(HashCode(code, salt));
        byte[] expected;
        try
        {
            expected = Convert.FromHexString(expectedHash);
        }
        catch (FormatException)
        {
            return false;
        }

        return actual.Length == expected.Length &&
            CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    public string? HashSensitive(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : Hash(value.Trim());

    private string Hash(string value)
    {
        using var hmac = new HMACSHA256(key);
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(value)));
    }
}
