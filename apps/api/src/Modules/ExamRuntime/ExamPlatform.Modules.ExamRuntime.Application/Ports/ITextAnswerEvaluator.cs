using ExamPlatform.Modules.ExamRuntime.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Application.Ports;

/// <summary>What the evaluator is asked to judge: a question, the answers its author accepted, and what a candidate typed.</summary>
/// <param name="QuestionText">The question as plain text: markup is removed before it is sent anywhere.</param>
/// <param name="AcceptedAnswers">The answers the author accepted, at least one. The candidate's answer is right when it says what one of these says.</param>
/// <param name="CandidateAnswer">What the candidate typed, exactly as typed. It is untrusted text and is only ever judged, never obeyed.</param>
public sealed record TextEvaluationRequest(string QuestionText, IReadOnlyList<string> AcceptedAnswers, string CandidateAnswer);

/// <summary>The evaluator's decision about one typed answer.</summary>
/// <param name="Verdict">Right or wrong.</param>
/// <param name="Reason">A sentence or two on why, for staff who are asked about the result; null when none was given.</param>
/// <param name="Model">The model that decided, as the service named it.</param>
public sealed record TextEvaluation(TypedAnswerVerdict Verdict, string? Reason, string Model);

/// <summary>
/// Judges a typed answer against the answers a question accepts, in words the author did not list. The implementation owns how it asks: its
/// own retries, within one call, for failures that are likely to pass.
/// </summary>
public interface ITextAnswerEvaluator
{
    /// <summary>
    /// Whether an evaluator is set up. When it is not, typed answers are marked by the accepted answers alone, as they always were, and no
    /// attempt is held back to wait for a verdict.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>Decides whether the candidate's answer is right.</summary>
    /// <param name="request">What to judge.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The verdict. A call that returns always has one.</returns>
    /// <exception cref="TextEvaluationException">No verdict could be had. <see cref="TextEvaluationException.IsPermanent"/> says whether asking again can help.</exception>
    /// <exception cref="InvalidOperationException">The evaluator is not set up (<see cref="IsEnabled"/> is false).</exception>
    Task<TextEvaluation> EvaluateAsync(TextEvaluationRequest request, CancellationToken cancellationToken);
}

/// <summary>No verdict could be had for a typed answer. The message says why in a few words and never repeats the answer.</summary>
public sealed class TextEvaluationException : Exception
{
    /// <summary>Creates the failure.</summary>
    /// <param name="message">What went wrong, without the question, the key or the answer in it.</param>
    /// <param name="isPermanent">True when asking again will not help until something is changed, such as a key or a setting.</param>
    /// <param name="innerException">The failure behind it, if there is one.</param>
    public TextEvaluationException(string message, bool isPermanent, Exception? innerException = null) : base(message, innerException) =>
        IsPermanent = isPermanent;

    /// <summary>
    /// Whether asking again will not help until something is changed: a key the service refuses, a model it does not have, a request it
    /// cannot read. A failure that is not permanent (a busy service, a dropped connection) is likely to pass.
    /// </summary>
    public bool IsPermanent { get; }
}
