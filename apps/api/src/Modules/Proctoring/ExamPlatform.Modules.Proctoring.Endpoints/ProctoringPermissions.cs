namespace ExamPlatform.Modules.Proctoring.Endpoints;

/// <summary>
/// Authorization policy names for the Proctoring routes (FR-2). Each is a <c>permission:{code}</c> policy that Identity's policy provider
/// resolves from the caller's <c>perm</c> claims, so this module needs no reference to Identity (ADR 0001). The code must be seeded in
/// Identity's role catalog; an integration test fails when a policy names a permission that is never seeded.
/// </summary>
internal static class ProctoringPermissions
{
    /// <summary>Score exams, read the review queue and decide on flags (<c>proctoring.review</c>).</summary>
    public const string Review = "permission:proctoring.review";
}
