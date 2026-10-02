namespace ExamPlatform.Modules.Guardian.Endpoints;

/// <summary>
/// Authorization policy names for the Guardian routes (FR-2). Each is a <c>permission:{code}</c>
/// policy that Identity's policy provider resolves by name from the <c>perm</c> claims in the
/// caller's token, so this module needs no reference to Identity (ADR 0001). The codes must exist
/// in Identity's role catalog; an integration test fails when a policy names a permission that is
/// never seeded, since no one could then ever call the route.
/// </summary>
internal static class GuardianPermissions
{
    /// <summary>Register guardians, link them to candidates and revoke those links (<c>guardian.link.manage</c>).</summary>
    public const string LinkManage = "permission:guardian.link.manage";
}
