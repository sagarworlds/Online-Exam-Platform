using ExamPlatform.Modules.ExamAuthoring.Application.Dtos;
using ExamPlatform.Modules.ExamAuthoring.Application.Ports;
using ExamPlatform.Modules.ExamAuthoring.Domain;
using ExamPlatform.Modules.ExamAuthoring.Domain.Exceptions;
using ExamPlatform.Modules.QuestionBank.Contracts;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.ExamAuthoring.Application.Commands;

/// <summary>Handles <see cref="ScheduleExamCommand"/> (FR-13).</summary>
public sealed class ScheduleExamHandler(IExamRepository repository, IExamAuthoringUnitOfWork unitOfWork, ExamDtoFactory dtos, Clock clock)
{
    /// <summary>Sets the exam's window, duration and late-entry cutoff.</summary>
    /// <param name="command">What to set.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ExamNotFoundError">No exam has that id.</exception>
    /// <exception cref="ExamNotDraftError">The exam is already published.</exception>
    /// <exception cref="InvalidExamConfigError">The start or end is missing, or the times, duration or zone are not usable.</exception>
    public async Task<ExamDto> HandleAsync(ScheduleExamCommand command, CancellationToken cancellationToken)
    {
        // A body without a start or end binds to null, since JSON binding does not enforce the
        // non-nullable annotation; that is a 400 about the missing value, not a crash.
        if (command.StartUtc is null || command.EndUtc is null)
            throw new InvalidExamConfigError("Both a start and an end are required.");

        var exam = await repository.GetByIdOrThrowAsync(command.ExamId, cancellationToken);
        exam.Schedule(
            AsUtc(command.StartUtc.Value),
            AsUtc(command.EndUtc.Value),
            command.TimeZone,
            command.LateEntryDeadlineUtc is { } lateEntry ? AsUtc(lateEntry) : null,
            command.DurationSeconds,
            clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await dtos.ToDtoAsync(exam, cancellationToken);
    }

    // A time sent without an offset arrives with an unspecified kind; the API's contract is that
    // every instant is UTC, so it is read as UTC rather than guessed to be server-local time.
    private static DateTime AsUtc(DateTime value) =>
        value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : value.ToUniversalTime();
}

/// <summary>Handles <see cref="AddSectionCommand"/>.</summary>
public sealed class AddSectionHandler(IExamRepository repository, IExamAuthoringUnitOfWork unitOfWork)
{
    /// <summary>Adds the section and saves.</summary>
    /// <param name="command">The section to add.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ExamNotFoundError">No exam has that id.</exception>
    /// <exception cref="ExamNotDraftError">The exam is already published.</exception>
    /// <exception cref="InvalidExamConfigError">The name is blank or too long, or the time limit is not positive.</exception>
    public async Task<ExamSectionDto> HandleAsync(AddSectionCommand command, CancellationToken cancellationToken)
    {
        var exam = await repository.GetByIdOrThrowAsync(command.ExamId, cancellationToken);
        var section = exam.AddSection(command.Name!, command.TimeSeconds);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return section.ToDto(new Dictionary<Guid, string>());
    }
}

/// <summary>Handles <see cref="AddExamQuestionCommand"/>.</summary>
public sealed class AddExamQuestionHandler(IExamRepository repository, IExamAuthoringUnitOfWork unitOfWork, IQuestionBank questionBank)
{
    /// <summary>Adds the bank question to the section and saves.</summary>
    /// <param name="command">Which question to add where.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ExamNotFoundError">No exam has that id.</exception>
    /// <exception cref="ExamNotDraftError">The exam is already published.</exception>
    /// <exception cref="SectionNotFoundError">The exam has no such section.</exception>
    /// <exception cref="QuestionNotInBankError">The question bank has no such question.</exception>
    /// <exception cref="QuestionOutsideExamScopeError">The question is not in the exam's book or chapters.</exception>
    /// <exception cref="DuplicateQuestionError">The question is already in this exam.</exception>
    public async Task<ExamQuestionDto> HandleAsync(AddExamQuestionCommand command, CancellationToken cancellationToken)
    {
        var exam = await repository.GetByIdOrThrowAsync(command.ExamId, cancellationToken);

        var found = await questionBank.GetAsync([command.QuestionId], cancellationToken);
        var snapshot = found.FirstOrDefault() ?? throw new QuestionNotInBankError(command.QuestionId);

        var question = exam.AddQuestion(command.SectionId, command.QuestionId, new QuestionPlacement(snapshot.BookId, snapshot.ChapterId));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new ExamQuestionDto(question.Id, question.QuestionVersionId, question.Order, snapshot.Text);
    }
}

/// <summary>Handles publishing an exam.</summary>
public sealed class PublishExamHandler(IExamRepository repository, IExamAuthoringUnitOfWork unitOfWork, ExamDtoFactory dtos, Clock clock)
{
    /// <summary>Publishes the exam so the candidates invited to it can take it.</summary>
    /// <param name="examId">The exam to publish.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ExamNotFoundError">No exam has that id.</exception>
    /// <exception cref="ExamNotDraftError">The exam is already published.</exception>
    /// <exception cref="InvalidExamConfigError">The exam is not scheduled, ends in the past, or has no questions.</exception>
    public async Task<ExamDto> HandleAsync(Guid examId, CancellationToken cancellationToken)
    {
        var exam = await repository.GetByIdOrThrowAsync(examId, cancellationToken);
        exam.Publish(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await dtos.ToDtoAsync(exam, cancellationToken);
    }
}

/// <summary>Handles <see cref="SetExamScopeCommand"/> (FR-11).</summary>
public sealed class SetExamScopeHandler(
    IExamRepository repository,
    IExamAuthoringUnitOfWork unitOfWork,
    IQuestionBank questionBank,
    ExamScopeResolver scopeResolver,
    ExamDtoFactory dtos)
{
    /// <summary>Limits the exam to a book or chosen chapters, or lifts the limit.</summary>
    /// <param name="command">The new scope.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ExamNotFoundError">No exam has that id.</exception>
    /// <exception cref="ExamNotDraftError">The exam is already published.</exception>
    /// <exception cref="InvalidExamConfigError">The scope is incomplete, or names a book or chapters that cannot be used.</exception>
    /// <exception cref="QuestionOutsideExamScopeError">The exam already holds questions the new scope would leave outside it.</exception>
    public async Task<ExamDto> HandleAsync(SetExamScopeCommand command, CancellationToken cancellationToken)
    {
        var exam = await repository.GetByIdOrThrowAsync(command.ExamId, cancellationToken);
        var scope = await scopeResolver.ResolveAsync(command.Scope, cancellationToken);

        // Where each question already in the exam is filed, so the domain can refuse a scope that would orphan them.
        var questionIds = exam.Sections.SelectMany(s => s.Questions).Select(q => q.QuestionVersionId).Distinct().ToList();
        var placements = (await questionBank.GetAsync(questionIds, cancellationToken))
            .ToDictionary(q => q.Id, q => new QuestionPlacement(q.BookId, q.ChapterId));

        exam.SetScope(scope, placements);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await dtos.ToDtoAsync(exam, cancellationToken);
    }
}
