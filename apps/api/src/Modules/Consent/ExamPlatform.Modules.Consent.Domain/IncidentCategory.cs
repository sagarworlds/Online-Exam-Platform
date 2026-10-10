namespace ExamPlatform.Modules.Consent.Domain;

/// <summary>
/// The kind of an incident. The set covers what an exam platform can suffer. The CERT-In directions of 2022 are the
/// reference for which cyber incidents must be reported; counsel should confirm this list against them.
/// </summary>
public enum IncidentCategory
{
    /// <summary>Personal data was disclosed, lost, altered or made unavailable.</summary>
    DataBreach,

    /// <summary>Someone reached an account, record or system without the right to, including with stolen credentials.</summary>
    UnauthorisedAccess,

    /// <summary>A malicious code attack, a denial of service, or phishing or spoofing of the platform or its users.</summary>
    CyberAttack,

    /// <summary>Any other incident. The affected-data description and the notes say what it is.</summary>
    Other
}
