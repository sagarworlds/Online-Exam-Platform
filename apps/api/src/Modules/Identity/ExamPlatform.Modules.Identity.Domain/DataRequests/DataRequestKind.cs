namespace ExamPlatform.Modules.Identity.Domain.DataRequests;

/// <summary>What a data principal is asking for under FR-48.</summary>
public enum DataRequestKind
{
    /// <summary>A copy of the personal data the platform holds about them.</summary>
    Access,

    /// <summary>A change to personal data they say is wrong.</summary>
    Correction,

    /// <summary>Removal of their personal data, where the law and the retention rules allow it.</summary>
    Erasure,
}
