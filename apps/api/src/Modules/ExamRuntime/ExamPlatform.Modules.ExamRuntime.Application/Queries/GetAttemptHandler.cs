using ExamPlatform.Modules.ExamRuntime.Application.Dtos;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.Modules.QuestionBank.Contracts;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.ExamRuntime.Application.Queries;

/// <summary>Reads an attempt: the questions while it is open, the result once it is over.</summary>
public sealed class GetAttemptHandler(AttemptAccess access, AttemptViewBuilder views, IClientInfo clientInfo, IExamRuntimeUnitOfWork unitOfWork, Clock clock)
{
    /// <summary>Returns one of the candidate's own attempts, closing it first if its time has run out.</summary>
    /// <param name="attemptId">The attempt.</param>
    /// <param name="candidateId">The signed-in candidate.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="lowBandwidth">Whether to leave the pictures out of the questions' text, to be fetched one at a time when wanted (FR-53).</param>
    /// <exception cref="AttemptNotFoundError">No such attempt, or it is someone else's.</exception>
    public async Task<AttemptDto> HandleAsync(Guid attemptId, Guid candidateId, CancellationToken cancellationToken, bool lowBandwidth = false)
    {
        var (attempt, exam) = await access.LoadOwnedAsync(attemptId, candidateId, cancellationToken);

        // Loading the page again from another address or device is a change of place worth keeping (FR-26).
        if (attempt.NoteClient(clientInfo.IpAddress, clientInfo.DeviceFingerprint, clock.UtcNow))
            await unitOfWork.SaveChangesAsync(cancellationToken);

        return await views.BuildAsync(attempt, exam, cancellationToken, lowBandwidth);
    }
}

/// <summary>Serves one picture of a question the candidate is sitting, for a page that was sent the attempt without its pictures (FR-53).</summary>
public sealed class GetQuestionPictureHandler(AttemptAccess access, IQuestionBank questionBank, IRequestLanguage? language = null)
{
    /// <summary>Returns the picture, as the attempt sees the question: the version it was pinned to, in the language the candidate asked for.</summary>
    /// <param name="attemptId">The attempt.</param>
    /// <param name="candidateId">The signed-in candidate.</param>
    /// <param name="questionId">The question the picture is in.</param>
    /// <param name="key">The picture's key from the marker the attempt carried, such as <c>q-0</c>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="AttemptNotFoundError">No such attempt, or it is someone else's.</exception>
    /// <exception cref="AttemptNotInProgressError">The attempt is over, so there is no exam page to show a picture on.</exception>
    /// <exception cref="QuestionNotInAttemptError">The question is not one of this attempt's.</exception>
    /// <exception cref="QuestionMediaNotFoundError">The question has no picture with that key.</exception>
    public async Task<QuestionPicture> HandleAsync(Guid attemptId, Guid candidateId, Guid questionId, string key, CancellationToken cancellationToken)
    {
        // Ownership first: someone else's attempt answers exactly like a missing one, before anything about it is revealed.
        var (attempt, exam) = await access.LoadOwnedAsync(attemptId, candidateId, cancellationToken);

        if (attempt.Status != AttemptStatus.InProgress)
            throw new AttemptNotInProgressError();
        if (!exam.Sections.SelectMany(s => s.QuestionIds).Contains(questionId))
            throw new QuestionNotInAttemptError();
        if (!QuestionMedia.TryParseKey(key, out var index))
            throw new QuestionMediaNotFoundError();

        var questions = await questionBank.ReadForCandidateAsync(attempt, [questionId], language?.Preferred ?? [], cancellationToken);
        var question = questions.GetValueOrDefault(questionId) ?? throw new ExamContentUnavailableError();

        return QuestionMedia.Find(question.Text, index) ?? throw new QuestionMediaNotFoundError();
    }
}
