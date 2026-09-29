using System.Security.Cryptography;
using System.Text;
using ExamPlatform.Modules.Identity.Application.Ports;

namespace ExamPlatform.Modules.Identity.Infrastructure;

/// <summary>Generates 6-digit numeric OTP codes and hashes them with SHA-256 for storage.</summary>
public sealed class OtpCodeGenerator : IOtpCodeGenerator
{
    /// <inheritdoc />
    public string GenerateCode() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    /// <inheritdoc />
    public string Hash(string code) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));
}
