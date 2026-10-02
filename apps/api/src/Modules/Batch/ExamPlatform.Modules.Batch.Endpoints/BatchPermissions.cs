namespace ExamPlatform.Modules.Batch.Endpoints;

/// <summary>
/// Authorization policy names for the Batch routes (FR-2). Each is a <c>permission:{code}</c>
/// policy that Identity's policy provider resolves by name from the <c>perm</c> claims in the
/// caller's token, so this module needs no reference to Identity (ADR 0001). The codes must exist
/// in Identity's role catalog; an integration test fails when a policy names a permission that is
/// never seeded, since no one could then ever call the route.
/// </summary>
internal static class BatchPermissions
{
    /// <summary>Create batches, manage their rosters and open or close them (<c>batch.manage</c>).</summary>
    public const string Manage = "permission:batch.manage";
}
