using ExamPlatform.Modules.Identity.Application.Ports;
using ExamPlatform.Modules.Identity.Domain;
using Microsoft.AspNetCore.Identity;

namespace ExamPlatform.Modules.Identity.Infrastructure;

/// <summary>
/// <see cref="IPasswordHasher"/> backed by ASP.NET Core Identity's own
/// <see cref="PasswordHasher{TUser}"/> (PBKDF2 with a per-password salt) — reusing
/// a battle-tested implementation rather than rolling one, without pulling in
/// the rest of ASP.NET Core Identity's user/sign-in machinery.
/// </summary>
public sealed class PasswordHasher : IPasswordHasher
{
    private readonly PasswordHasher<User> _inner = new();

    /// <inheritdoc />
    public string Hash(string password) => _inner.HashPassword(null!, password);

    /// <inheritdoc />
    public bool Verify(string password, string hash) =>
        _inner.VerifyHashedPassword(null!, hash, password) != PasswordVerificationResult.Failed;
}
