namespace ExamPlatform.Modules.Consent.Endpoints;

/// <summary>
/// Authorization policy name for the incident routes (FR-2, FR-52). It is a <c>permission:{code}</c> policy that Identity's policy
/// provider resolves from the <c>perm</c> claims in the caller's token, so this module needs no reference to Identity (ADR 0001).
/// The code must exist in Identity's role catalog, or no one could ever call the routes.
/// </summary>
internal static class IncidentPermissions
{
    /// <summary>Log incidents, list the open ones with their overdue flags, and record status changes (<c>consent.incident.manage</c>).</summary>
    public const string Manage = "permission:consent.incident.manage";
}
