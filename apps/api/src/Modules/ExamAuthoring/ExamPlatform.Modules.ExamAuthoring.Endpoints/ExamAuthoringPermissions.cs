namespace ExamPlatform.Modules.ExamAuthoring.Endpoints;

/// <summary>
/// Authorization policy names for the ExamAuthoring routes (FR-2). Each is a
/// <c>permission:{code}</c> policy that Identity's policy provider resolves by name from the
/// <c>perm</c> claims in the caller's token, so this module needs no reference to Identity
/// (ADR 0001). The codes must exist in Identity's role catalog; an integration test fails when a
/// policy names a permission that is never seeded, since no one could then ever call the route.
/// </summary>
internal static class ExamAuthoringPermissions
{
    /// <summary>View exams without changing them (<c>exam.read</c>).</summary>
    public const string Read = "permission:exam.read";

    /// <summary>Create and edit exams (<c>exam.manage</c>).</summary>
    public const string Manage = "permission:exam.manage";

    /// <summary>Publish an exam so invited candidates can take it (<c>exam.publish</c>).</summary>
    public const string Publish = "permission:exam.publish";
}
