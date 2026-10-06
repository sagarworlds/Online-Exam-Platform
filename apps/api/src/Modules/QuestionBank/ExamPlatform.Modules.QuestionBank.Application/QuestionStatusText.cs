using ExamPlatform.Modules.QuestionBank.Domain;
using ExamPlatform.Modules.QuestionBank.Domain.Exceptions;

namespace ExamPlatform.Modules.QuestionBank.Application;

/// <summary>The text a request or response uses for a <see cref="QuestionStatus"/> (FR-8): <c>draft</c>, <c>in_review</c>, <c>approved</c>, <c>retired</c>.</summary>
public static class QuestionStatusText
{
    /// <summary>Reads a status from request text, ignoring case.</summary>
    /// <param name="text">The text; null or blank means no status.</param>
    /// <returns>The status, or null when none was given.</returns>
    /// <exception cref="InvalidQuestionError">The text names no status.</exception>
    public static QuestionStatus? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        return text.Trim().ToLowerInvariant().Replace("-", "_") switch
        {
            "draft" => QuestionStatus.Draft,
            "in_review" or "inreview" or "review" => QuestionStatus.InReview,
            "approved" => QuestionStatus.Approved,
            "retired" => QuestionStatus.Retired,
            _ => throw new InvalidQuestionError("The status must be draft, in_review, approved or retired."),
        };
    }

    /// <summary>The text a response carries for a status.</summary>
    /// <param name="status">The status.</param>
    public static string Format(QuestionStatus status) => status switch
    {
        QuestionStatus.Draft => "draft",
        QuestionStatus.InReview => "in_review",
        QuestionStatus.Approved => "approved",
        _ => "retired",
    };

    /// <summary>The text a response carries for what a review entry records.</summary>
    /// <param name="kind">The kind of entry.</param>
    public static string Format(QuestionReviewEntryKind kind) => kind switch
    {
        QuestionReviewEntryKind.Commented => "commented",
        QuestionReviewEntryKind.Submitted => "submitted",
        QuestionReviewEntryKind.Approved => "approved",
        QuestionReviewEntryKind.ChangesRequested => "changes_requested",
        QuestionReviewEntryKind.Retired => "retired",
        _ => "restored",
    };
}
