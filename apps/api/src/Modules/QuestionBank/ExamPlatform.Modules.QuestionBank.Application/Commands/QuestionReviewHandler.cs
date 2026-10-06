using ExamPlatform.Modules.QuestionBank.Application.Dtos;
using ExamPlatform.Modules.QuestionBank.Application.Ports;
using ExamPlatform.Modules.QuestionBank.Domain;
using ExamPlatform.Modules.QuestionBank.Domain.Exceptions;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.QuestionBank.Application.Commands;

/// <summary>Who is taking a review step, as the token says.</summary>
/// <param name="UserId">The staff member's id.</param>
/// <param name="Label">How to show them in the thread, normally their email address.</param>
public sealed record ReviewActor(Guid UserId, string? Label);

/// <summary>
/// The review workflow of a question (FR-8): put forward, approve, send back, retire, restore, and comment. Each step is a rule of
/// <see cref="Question"/>; this loads the question, takes the step, stores the entry that records it and saves both together, so the
/// status and the thread can never disagree.
/// </summary>
public sealed class QuestionReviewHandler(IQuestionRepository repository, IQuestionBankUnitOfWork unitOfWork, Clock clock)
{
    /// <summary>Puts a draft forward for review.</summary>
    /// <param name="questionId">The question.</param>
    /// <param name="actor">Who is doing it.</param>
    /// <param name="comment">An optional note to the reviewer.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="QuestionNotFoundError">No question has that id.</exception>
    /// <exception cref="InvalidQuestionStatusError">The question is not a draft.</exception>
    public Task<QuestionReviewResultDto> SubmitAsync(Guid questionId, ReviewActor actor, string? comment, CancellationToken cancellationToken) =>
        StepAsync(questionId, q => q.SubmitForReview(actor.UserId, actor.Label, comment, clock.UtcNow), cancellationToken);

    /// <summary>Approves a question that is in review.</summary>
    /// <param name="questionId">The question.</param>
    /// <param name="actor">The reviewer.</param>
    /// <param name="comment">An optional note.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="QuestionNotFoundError">No question has that id.</exception>
    /// <exception cref="InvalidQuestionStatusError">The question is not in review.</exception>
    public Task<QuestionReviewResultDto> ApproveAsync(Guid questionId, ReviewActor actor, string? comment, CancellationToken cancellationToken) =>
        StepAsync(questionId, q => q.Approve(actor.UserId, actor.Label, comment, clock.UtcNow), cancellationToken);

    /// <summary>Sends a question in review back to its author, with the reason.</summary>
    /// <param name="questionId">The question.</param>
    /// <param name="actor">The reviewer.</param>
    /// <param name="comment">What has to change; required.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="QuestionNotFoundError">No question has that id.</exception>
    /// <exception cref="InvalidQuestionStatusError">The question is not in review.</exception>
    /// <exception cref="InvalidQuestionError">There is no comment.</exception>
    public Task<QuestionReviewResultDto> RequestChangesAsync(Guid questionId, ReviewActor actor, string? comment, CancellationToken cancellationToken) =>
        StepAsync(questionId, q => q.RequestChanges(actor.UserId, actor.Label, comment, clock.UtcNow), cancellationToken);

    /// <summary>Takes a question out of use.</summary>
    /// <param name="questionId">The question.</param>
    /// <param name="actor">Who is retiring it.</param>
    /// <param name="comment">An optional reason.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="QuestionNotFoundError">No question has that id.</exception>
    /// <exception cref="InvalidQuestionStatusError">The question is already retired.</exception>
    public Task<QuestionReviewResultDto> RetireAsync(Guid questionId, ReviewActor actor, string? comment, CancellationToken cancellationToken) =>
        StepAsync(questionId, q => q.Retire(actor.UserId, actor.Label, comment, clock.UtcNow), cancellationToken);

    /// <summary>Brings a retired question back as a draft.</summary>
    /// <param name="questionId">The question.</param>
    /// <param name="actor">Who is restoring it.</param>
    /// <param name="comment">An optional note.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="QuestionNotFoundError">No question has that id.</exception>
    /// <exception cref="InvalidQuestionStatusError">The question is not retired.</exception>
    public Task<QuestionReviewResultDto> RestoreAsync(Guid questionId, ReviewActor actor, string? comment, CancellationToken cancellationToken) =>
        StepAsync(questionId, q => q.Restore(actor.UserId, actor.Label, comment, clock.UtcNow), cancellationToken);

    /// <summary>Adds a comment to the question's thread.</summary>
    /// <param name="questionId">The question.</param>
    /// <param name="actor">Who is commenting.</param>
    /// <param name="comment">The comment; required.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="QuestionNotFoundError">No question has that id.</exception>
    /// <exception cref="InvalidQuestionError">There is no comment, or it is too long.</exception>
    public Task<QuestionReviewResultDto> CommentAsync(Guid questionId, ReviewActor actor, string? comment, CancellationToken cancellationToken) =>
        StepAsync(questionId, q => q.Comment(actor.UserId, actor.Label, comment, clock.UtcNow), cancellationToken);

    private async Task<QuestionReviewResultDto> StepAsync(
        Guid questionId, Func<Question, QuestionReviewEntry> step, CancellationToken cancellationToken)
    {
        var question = await repository.GetByIdAsync(questionId, cancellationToken) ?? throw new QuestionNotFoundError();

        var entry = step(question);
        repository.AddReviewEntry(entry);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new QuestionReviewResultDto(QuestionStatusText.Format(question.Status), entry.ToDto());
    }
}
