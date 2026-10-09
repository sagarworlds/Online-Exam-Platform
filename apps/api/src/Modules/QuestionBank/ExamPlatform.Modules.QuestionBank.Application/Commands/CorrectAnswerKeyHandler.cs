using ExamPlatform.Modules.Admin.Contracts;
using ExamPlatform.Modules.QuestionBank.Application.Ports;
using ExamPlatform.Modules.QuestionBank.Contracts;
using ExamPlatform.Modules.QuestionBank.Domain.Exceptions;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.QuestionBank.Application.Commands;

/// <summary>Corrects a question's answer key after it has been published and possibly answered (FR-31).</summary>
/// <param name="QuestionId">The question to correct.</param>
/// <param name="CorrectOptionIds">The options that are actually correct, replacing the current answer key.</param>
/// <param name="Reason">Why the key is being corrected; shown to a candidate whose score moves because of it.</param>
/// <param name="ActorUserId">The staff member making the correction, for the audit trail.</param>
/// <param name="ActorRole">Their role name, for the audit trail.</param>
/// <param name="AcceptedAnswers">For a text question, the accepted answers replacing the current list; null for a multiple-choice question, whose key is <paramref name="CorrectOptionIds"/>.</param>
public sealed record CorrectAnswerKeyCommand(
    Guid QuestionId, IReadOnlyCollection<Guid> CorrectOptionIds, string Reason, Guid ActorUserId, string ActorRole,
    IReadOnlyList<string?>? AcceptedAnswers = null);

/// <summary>What a correction changed.</summary>
/// <param name="KeyChanged">Whether the answer key actually differed from what was asked for; false means nothing happened.</param>
/// <param name="AttemptsRescored">How many already-submitted attempts had their score revised because of it.</param>
public sealed record AnswerKeyCorrectionResult(bool KeyChanged, int AttemptsRescored);

/// <summary>
/// Handles <see cref="CorrectAnswerKeyCommand"/>: unlike <see cref="EditQuestionHandler"/>, this is the one path that may
/// change which option is correct after candidates have answered — a dispute means the key itself was wrong. Every
/// submitted attempt that included the question is rescored under the corrected key, and the correction is audited,
/// because it can move a candidate's result.
/// </summary>
public sealed class CorrectAnswerKeyHandler(
    IQuestionRepository repository,
    IQuestionBankUnitOfWork unitOfWork,
    IAttemptRescorer rescorer,
    IAuditLogger auditLogger,
    Clock clock)
{
    /// <summary>Corrects the key, saves it, then asks ExamRuntime to rescore every attempt it affects.</summary>
    /// <param name="command">The correction to make.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="QuestionNotFoundError">No question has that id.</exception>
    /// <exception cref="InvalidQuestionError">The named options do not belong to the question, or their count breaks the question's answer-key shape.</exception>
    public async Task<AnswerKeyCorrectionResult> HandleAsync(CorrectAnswerKeyCommand command, CancellationToken cancellationToken)
    {
        var question = await repository.GetByIdAsync(command.QuestionId, cancellationToken) ?? throw new QuestionNotFoundError();

        var changed = command.AcceptedAnswers is not null
            ? question.CorrectAcceptedAnswers(command.AcceptedAnswers, clock.UtcNow)
            : question.CorrectAnswerKey(command.CorrectOptionIds, clock.UtcNow);
        if (!changed)
            return new AnswerKeyCorrectionResult(false, 0);

        // Saved before rescoring: ExamRuntime reads the question bank fresh for each attempt it rescores, so the
        // correction must already be durable, or a rescore would mark against the key as it was a moment ago.
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var rescoredCount = await rescorer.RescoreForQuestionAsync(question.Id, command.Reason, cancellationToken);

        // The correction is what moved results, so it is what the trail records; the reason is the candidate-facing
        // explanation, kept here as what staff gave as their reasoning, not reconstructed from attempts individually.
        await auditLogger.RecordAsync(
            new AuditEntry(
                ActorUserId: command.ActorUserId,
                ActorRole: command.ActorRole,
                Action: "QuestionBank.AnswerKeyCorrected",
                EntityType: "Question",
                EntityId: question.Id.ToString(),
                Metadata: new Dictionary<string, string>
                {
                    ["reason"] = command.Reason,
                    ["attemptsRescored"] = rescoredCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                },
                CorrelationId: null),
            cancellationToken);

        return new AnswerKeyCorrectionResult(true, rescoredCount);
    }
}
