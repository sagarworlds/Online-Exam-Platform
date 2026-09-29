using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.Identity.Domain;

/// <summary>
/// One login session for a user. A child entity of the <see cref="User"/> aggregate,
/// because the FR-4 invariant ("only one active session per candidate") spans
/// multiple sessions belonging to the same user and so must be enforced within
/// a single aggregate's consistency boundary.
/// </summary>
public sealed class UserSession : Entity
{
    /// <summary>The user this session belongs to.</summary>
    public Guid UserId { get; private set; }

    /// <summary>Hash of the session/bearer token, never the raw token.</summary>
    public string SessionTokenHash { get; private set; }

    /// <summary>When the session was created.</summary>
    public DateTime IssuedAtUtc { get; private set; }

    /// <summary>When the session naturally expires if never revoked.</summary>
    public DateTime ExpiresAtUtc { get; private set; }

    /// <summary>When the session was revoked, if it has been.</summary>
    public DateTime? RevokedAtUtc { get; private set; }

    /// <summary>Why the session was revoked, if it has been.</summary>
    public SessionRevocationReason? RevokedReason { get; private set; }

    /// <summary>Opaque client device fingerprint captured at login, for multi-login detection (FR-26).</summary>
    public string? DeviceFingerprint { get; private set; }

    /// <summary>Client IP address captured at login, for multi-login detection (FR-26).</summary>
    public string? IpAddress { get; private set; }

    /// <summary>Whether the session is currently usable: not revoked and not past its expiry.</summary>
    /// <param name="nowUtc">The instant to evaluate expiry against.</param>
    public bool IsActive(DateTime nowUtc) => RevokedAtUtc is null && ExpiresAtUtc > nowUtc;

    internal UserSession(
        Guid id,
        Guid userId,
        string sessionTokenHash,
        DateTime issuedAtUtc,
        DateTime expiresAtUtc,
        string? deviceFingerprint,
        string? ipAddress) : base(id)
    {
        UserId = userId;
        SessionTokenHash = sessionTokenHash;
        IssuedAtUtc = issuedAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        DeviceFingerprint = deviceFingerprint;
        IpAddress = ipAddress;
    }

    /// <summary>Marks the session revoked. Idempotent: revoking an already-revoked session is a no-op.</summary>
    /// <param name="nowUtc">When the revocation happened.</param>
    /// <param name="reason">Why the session was revoked.</param>
    internal void Revoke(DateTime nowUtc, SessionRevocationReason reason)
    {
        if (RevokedAtUtc is not null) return;
        RevokedAtUtc = nowUtc;
        RevokedReason = reason;
    }
}
