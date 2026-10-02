namespace ExamPlatform.Modules.QuestionBank.Endpoints;

/// <summary>
/// Authorization policy names for the QuestionBank routes (FR-2). Each is a <c>permission:{code}</c>
/// policy that Identity's policy provider resolves from the <c>perm</c> claims in the caller's token, so
/// this module needs no reference to Identity (ADR 0001). The code must exist in Identity's role catalog.
/// </summary>
internal static class QuestionBankPermissions
{
    /// <summary>Create and read questions, answer key included (<c>question.manage</c>).</summary>
    public const string Manage = "permission:question.manage";
}
