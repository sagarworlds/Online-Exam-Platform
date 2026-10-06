using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Domain;

/// <summary>Why an <see cref="AttemptClientSighting"/> was recorded.</summary>
public enum ClientSightingReason
{
    /// <summary>The first time the attempt was seen: where and on what device the candidate began it.</summary>
    Started,

    /// <summary>The candidate's address or device signature differs from the last time the attempt was seen.</summary>
    Changed,
}

/// <summary>
/// Where an <see cref="Attempt"/> was being sat from at a moment (FR-26): the candidate's IP address and device signature as the server
/// saw them. The first sighting is the start; another is added only when either differs from the last, so an attempt's rows read as its
/// history of devices and networks, not a log of every request.
/// </summary>
/// <remarks>
/// A change is evidence, not a verdict: a phone moving between wifi and mobile data changes its address, and a candidate whose browser
/// updated changes its signature. Staff read it together with the rest (FR-27); nothing here acts on it.
/// </remarks>
public sealed class AttemptClientSighting : Entity
{
    /// <summary>The attempt this belongs to.</summary>
    public Guid AttemptId { get; private set; }

    /// <summary>The candidate's IP address as the server saw it; null when it could not be read.</summary>
    public string? IpAddress { get; private set; }

    /// <summary>The device signature the web app sent; null when none was sent.</summary>
    public string? DeviceFingerprint { get; private set; }

    /// <summary>When the server saw it, by the server's clock.</summary>
    public DateTime SeenAtUtc { get; private set; }

    /// <summary>Whether this is where the attempt began or a change from where it was.</summary>
    public ClientSightingReason Reason { get; private set; }

    // For EF Core.
    private AttemptClientSighting() : base(Guid.Empty)
    {
    }

    internal AttemptClientSighting(Guid attemptId, string? ipAddress, string? deviceFingerprint, DateTime seenAtUtc, ClientSightingReason reason)
        : base(Guid.NewGuid())
    {
        AttemptId = attemptId;
        IpAddress = ipAddress;
        DeviceFingerprint = deviceFingerprint;
        SeenAtUtc = seenAtUtc;
        Reason = reason;
    }
}
