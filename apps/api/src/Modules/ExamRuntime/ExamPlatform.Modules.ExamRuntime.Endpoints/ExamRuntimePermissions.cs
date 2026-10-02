namespace ExamPlatform.Modules.ExamRuntime.Endpoints;

/// <summary>
/// Authorization policy names for the staff-facing ExamRuntime routes (FR-2). A <c>permission:{code}</c> policy that Identity's
/// policy provider resolves from the <c>perm</c> claims in the caller's token, so this module needs no reference to Identity
/// (ADR 0001). The code must exist in Identity's role catalog. The candidate-facing routes need no permission: they act on the
/// signed-in candidate's own data.
/// </summary>
internal static class ExamRuntimePermissions
{
    /// <summary>See an exam's candidates and their attempts, and grant extra attempts: <c>exam.manage</c>, the permission that runs an exam.</summary>
    public const string ManageAttempts = "permission:exam.manage";
}
