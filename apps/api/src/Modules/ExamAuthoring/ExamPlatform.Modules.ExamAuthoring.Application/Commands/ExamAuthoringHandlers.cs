using ExamPlatform.Modules.ExamAuthoring.Application.Dtos;
using ExamPlatform.Modules.ExamAuthoring.Application.Ports;
using ExamPlatform.Modules.ExamAuthoring.Domain;
using ExamPlatform.Modules.ExamAuthoring.Contracts;
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
            UtcInstant.From(command.StartUtc.Value),
            UtcInstant.From(command.EndUtc.Value),
            command.TimeZone,
            command.LateEntryDeadlineUtc is { } lateEntry ? UtcInstant.From(lateEntry) : null,
            command.DurationSeconds,
            clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await dtos.ToDtoAsync(exam, cancellationToken);
    }
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

/// <summary>Handles <see cref="UpdateExamDetailsCommand"/>.</summary>
public sealed class UpdateExamDetailsHandler(IExamRepository repository, IExamAuthoringUnitOfWork unitOfWork, ExamDtoFactory dtos, Clock clock)
{
    /// <summary>Changes the exam's name and description and saves.</summary>
    /// <param name="command">The new details.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ExamNotFoundError">No exam has that id.</exception>
    /// <exception cref="ExamArchivedError">The exam is archived.</exception>
    /// <exception cref="InvalidExamConfigError">The name is blank or too long, or the description is too long.</exception>
    public async Task<ExamDto> HandleAsync(UpdateExamDetailsCommand command, CancellationToken cancellationToken)
    {
        var exam = await repository.GetByIdOrThrowAsync(command.ExamId, cancellationToken);
        exam.Describe(command.Name, command.Description, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await dtos.ToDtoAsync(exam, cancellationToken);
    }
}

/// <summary>Handles <see cref="EditSectionCommand"/>.</summary>
public sealed class EditSectionHandler(IExamRepository repository, IExamAuthoringUnitOfWork unitOfWork)
{
    /// <summary>Renames the section, sets its time limit and saves.</summary>
    /// <param name="command">The new name and time limit.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ExamNotFoundError">No exam has that id.</exception>
    /// <exception cref="ExamNotDraftError">The exam is already published.</exception>
    /// <exception cref="SectionNotFoundError">The exam has no such section.</exception>
    /// <exception cref="InvalidExamConfigError">The name is blank or too long, or the time limit is not positive.</exception>
    public async Task HandleAsync(EditSectionCommand command, CancellationToken cancellationToken)
    {
        var exam = await repository.GetByIdOrThrowAsync(command.ExamId, cancellationToken);
        exam.EditSection(command.SectionId, command.Name, command.TimeSeconds);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Handles <see cref="RemoveSectionCommand"/>.</summary>
public sealed class RemoveSectionHandler(IExamRepository repository, IExamAuthoringUnitOfWork unitOfWork)
{
    /// <summary>Removes the section and the places its questions held, then saves. The questions stay in the bank.</summary>
    /// <param name="command">Which section to remove.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ExamNotFoundError">No exam has that id.</exception>
    /// <exception cref="ExamNotDraftError">The exam is already published.</exception>
    /// <exception cref="SectionNotFoundError">The exam has no such section.</exception>
    public async Task HandleAsync(RemoveSectionCommand command, CancellationToken cancellationToken)
    {
        var exam = await repository.GetByIdOrThrowAsync(command.ExamId, cancellationToken);
        exam.RemoveSection(command.SectionId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
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

/// <summary>Handles <see cref="DrawExamQuestionsCommand"/>.</summary>
public sealed class DrawExamQuestionsHandler(
    IExamRepository repository, IExamAuthoringUnitOfWork unitOfWork, IQuestionBank questionBank, IQuestionPicker picker)
{
    /// <summary>The most questions one draw may add, so a slip of a zero cannot fill a section with a whole bank.</summary>
    public const int MaxCount = 100;

    /// <summary>
    /// Picks <see cref="DrawExamQuestionsCommand.Count"/> questions at random from the bank questions that match the criteria, are
    /// inside the exam's scope and are not already in the exam, adds them to the section and saves. All or none.
    /// </summary>
    /// <remarks>
    /// The draw happens now, while the exam is a draft: the exam keeps the fixed list it picked, exactly as if the author had added
    /// each question by hand. Candidates therefore still all sit the same questions.
    /// </remarks>
    /// <param name="command">What to draw and where to put it.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The questions added, in the order they now sit in the section.</returns>
    /// <exception cref="InvalidExamConfigError">The count is outside 1 to <see cref="MaxCount"/>.</exception>
    /// <exception cref="ExamNotFoundError">No exam has that id.</exception>
    /// <exception cref="ExamNotDraftError">The exam is already published.</exception>
    /// <exception cref="SectionNotFoundError">The exam has no such section.</exception>
    /// <exception cref="NotEnoughQuestionsError">Fewer questions are available than were asked for; nothing is added.</exception>
    public async Task<IReadOnlyList<ExamQuestionDto>> HandleAsync(DrawExamQuestionsCommand command, CancellationToken cancellationToken)
    {
        if (command.Count is < 1 or > MaxCount)
            throw new InvalidExamConfigError($"Draw between 1 and {MaxCount} questions.");

        var exam = await repository.GetByIdOrThrowAsync(command.ExamId, cancellationToken);
        // Checked up front, so a published exam is refused before the bank is asked anything.
        exam.EnsureCanAddQuestions(command.SectionId);

        var matches = await questionBank.FindAsync(
            new QuestionCriteria(command.BookId, command.ChapterId, command.Difficulty, command.Topic), cancellationToken);

        var held = exam.Sections.SelectMany(s => s.Questions).Select(q => q.QuestionVersionId).ToHashSet();
        var candidates = matches
            .Where(q => !held.Contains(q.Id) && exam.Scope.Allows(new QuestionPlacement(q.BookId, q.ChapterId)))
            .ToList();

        if (candidates.Count < command.Count)
            throw new NotEnoughQuestionsError(command.Count, candidates.Count);

        var chosen = picker.Pick(candidates, command.Count);
        foreach (var question in chosen)
            exam.AddQuestion(command.SectionId, question.Id, new QuestionPlacement(question.BookId, question.ChapterId));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var texts = (await questionBank.GetAsync(chosen.Select(q => q.Id).ToList(), cancellationToken)).ToDictionary(q => q.Id, q => q.Text);
        var section = exam.Sections.Single(s => s.Id == command.SectionId);
        return section.Questions
            .Where(q => texts.ContainsKey(q.QuestionVersionId))
            .Select(q => new ExamQuestionDto(q.Id, q.QuestionVersionId, q.Order, texts[q.QuestionVersionId]))
            .ToList();
    }
}

/// <summary>Handles <see cref="AddDrawRuleCommand"/>.</summary>
public sealed class AddDrawRuleHandler(IExamRepository repository, IExamAuthoringUnitOfWork unitOfWork)
{
    /// <summary>Adds the rule to the section and saves. Nothing is drawn now: each candidate's questions are drawn when they start.</summary>
    /// <param name="command">The rule to add.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The rule as added.</returns>
    /// <exception cref="ExamNotFoundError">No exam has that id.</exception>
    /// <exception cref="ExamNotDraftError">The exam is already published.</exception>
    /// <exception cref="SectionNotFoundError">The exam has no such section.</exception>
    /// <exception cref="InvalidExamConfigError">The rule is invalid, or lies outside the exam's scope.</exception>
    public async Task<DrawRuleDto> HandleAsync(AddDrawRuleCommand command, CancellationToken cancellationToken)
    {
        var exam = await repository.GetByIdOrThrowAsync(command.ExamId, cancellationToken);
        var rule = exam.AddDrawRule(command.SectionId, command.Count, command.BookId, command.ChapterId, command.Difficulty, command.Topic);

        // Refused now rather than at publish, while the author is looking at the rule they just typed.
        if (DrawRuleScoping.Apply(rule, exam.Scope) is null)
            throw new InvalidExamConfigError("That rule asks for questions outside the exam's book or chapters, so it could never match.");

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return rule.ToDto();
    }
}

/// <summary>Handles <see cref="RemoveDrawRuleCommand"/>.</summary>
public sealed class RemoveDrawRuleHandler(IExamRepository repository, IExamAuthoringUnitOfWork unitOfWork)
{
    /// <summary>Takes the rule out of the section and saves.</summary>
    /// <param name="command">Which rule to remove from where.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ExamNotFoundError">No exam has that id.</exception>
    /// <exception cref="ExamNotDraftError">The exam is already published.</exception>
    /// <exception cref="SectionNotFoundError">The exam has no such section.</exception>
    /// <exception cref="DrawRuleNotFoundError">The section has no such rule.</exception>
    public async Task HandleAsync(RemoveDrawRuleCommand command, CancellationToken cancellationToken)
    {
        var exam = await repository.GetByIdOrThrowAsync(command.ExamId, cancellationToken);
        exam.RemoveDrawRule(command.SectionId, command.RuleId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Handles <see cref="RemoveExamQuestionCommand"/>.</summary>
public sealed class RemoveExamQuestionHandler(IExamRepository repository, IExamAuthoringUnitOfWork unitOfWork)
{
    /// <summary>
    /// Takes the question out of the section and saves. The question itself stays in the bank; it is only no longer part of
    /// this exam, which is also what lets the bank delete it again if nothing else holds it.
    /// </summary>
    /// <param name="command">Which question to take out of where.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ExamNotFoundError">No exam has that id.</exception>
    /// <exception cref="ExamNotDraftError">The exam is already published.</exception>
    /// <exception cref="SectionNotFoundError">The exam has no such section.</exception>
    /// <exception cref="QuestionNotInExamError">The section does not hold that question.</exception>
    public async Task HandleAsync(RemoveExamQuestionCommand command, CancellationToken cancellationToken)
    {
        var exam = await repository.GetByIdOrThrowAsync(command.ExamId, cancellationToken);
        exam.RemoveQuestion(command.SectionId, command.QuestionId);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Handles deleting a draft exam.</summary>
public sealed class DeleteExamHandler(
    IExamRepository repository,
    IExamAuthoringUnitOfWork unitOfWork,
    IEnumerable<IExamDeletionGuard> guards,
    Clock clock)
{
    /// <summary>
    /// Deletes the exam if it is a draft that nothing else refers to. The questions it held stay in the bank and, no longer
    /// in any exam, can be deleted from it.
    /// </summary>
    /// <param name="examId">The exam to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ExamNotFoundError">No exam has that id.</exception>
    /// <exception cref="ExamNotDeletableError">The exam is published, or a module that keeps its id (an invitation, a batch) objects.</exception>
    public async Task HandleAsync(Guid examId, CancellationToken cancellationToken)
    {
        var exam = await repository.GetByIdOrThrowAsync(examId, cancellationToken);

        // The exam's own rule first: a published exam is refused whatever the guards would say, and asking them is wasted work.
        exam.EnsureCanBeDeleted();

        // Every guard is asked and every reason reported, so the author learns everything in the way in one go instead of
        // finding the second reason only after sorting out the first.
        var reasons = new List<string>();
        foreach (var guard in guards)
            reasons.AddRange(await guard.FindObjectionsAsync(examId, cancellationToken));

        if (reasons.Count > 0)
            throw new ExamNotDeletableError(string.Join(" ", reasons));

        exam.Delete(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>Handles publishing an exam.</summary>
public sealed class PublishExamHandler(
    IExamRepository repository, IExamAuthoringUnitOfWork unitOfWork, ExamDtoFactory dtos, DrawPoolChecker drawPools, Clock clock)
{
    /// <summary>Publishes the exam so the candidates invited to it can take it.</summary>
    /// <param name="examId">The exam to publish.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ExamNotFoundError">No exam has that id.</exception>
    /// <exception cref="ExamNotDraftError">The exam is already published.</exception>
    /// <exception cref="InvalidExamConfigError">The exam is not scheduled, ends in the past, or has no questions or draw rules.</exception>
    /// <exception cref="DrawPoolTooSmallError">A draw rule needs more questions than the bank has for it.</exception>
    public async Task<ExamDto> HandleAsync(Guid examId, CancellationToken cancellationToken)
    {
        var exam = await repository.GetByIdOrThrowAsync(examId, cancellationToken);
        // Only a draft is checked: a published exam is refused by Publish itself, whatever the bank holds.
        if (exam.Status == ExamStatus.Draft)
            await drawPools.EnsureFillableAsync(exam, cancellationToken);
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

/// <summary>Handles <see cref="SetResultReleaseCommand"/>.</summary>
public sealed class SetResultReleaseHandler(IExamRepository repository, IExamAuthoringUnitOfWork unitOfWork, ExamDtoFactory dtos, Clock clock)
{
    /// <summary>Chooses when candidates may see which of their answers were right.</summary>
    /// <param name="command">The new setting.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ExamNotFoundError">No exam has that id.</exception>
    /// <exception cref="ExamArchivedError">The exam is archived.</exception>
    /// <exception cref="InvalidExamConfigError">No mode was sent, or a scheduled release has no time.</exception>
    public async Task<ExamDto> HandleAsync(SetResultReleaseCommand command, CancellationToken cancellationToken)
    {
        // A body without a mode binds to null, since JSON binding does not enforce the non-nullable annotation;
        // that is a 400 about the missing value, not a crash.
        if (command.Mode is null)
            throw new InvalidExamConfigError("Choose when the answers are shown: right after submitting, at a set time, or when released.");

        var exam = await repository.GetByIdOrThrowAsync(command.ExamId, cancellationToken);
        exam.SetResultRelease(command.Mode.Value, command.ReleaseTimeUtc is { } time ? UtcInstant.From(time) : null, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await dtos.ToDtoAsync(exam, cancellationToken);
    }
}

/// <summary>Handles <see cref="SetMarkingSchemeCommand"/>.</summary>
public sealed class SetMarkingSchemeHandler(IExamRepository repository, IExamAuthoringUnitOfWork unitOfWork, ExamDtoFactory dtos, Clock clock)
{
    /// <summary>Sets the marking scheme of a draft exam.</summary>
    /// <param name="command">The new marks.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ExamNotFoundError">No exam has that id.</exception>
    /// <exception cref="ExamArchivedError">The exam is archived.</exception>
    /// <exception cref="ExamNotDraftError">The exam is already published.</exception>
    /// <exception cref="InvalidExamConfigError">A mark was not sent or is out of range.</exception>
    public async Task<ExamDto> HandleAsync(SetMarkingSchemeCommand command, CancellationToken cancellationToken)
    {
        // A body missing a mark binds it to null; that is a 400 about the missing value, not a crash.
        if (command is not { CorrectMarks: { } correct, IncorrectMarks: { } incorrect, UnattemptedMarks: { } unattempted })
            throw new InvalidExamConfigError("Send the marks for a correct answer, an incorrect answer and an unattempted question.");

        var exam = await repository.GetByIdOrThrowAsync(command.ExamId, cancellationToken);
        // A caller that does not know about partial credit leaves the setting as it was rather than quietly turning it off.
        exam.SetMarkingScheme(new MarkingScheme(correct, incorrect, unattempted, command.PartialCredit ?? exam.Config.MarkingScheme.PartialCredit), clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await dtos.ToDtoAsync(exam, cancellationToken);
    }
}

/// <summary>Handles <see cref="SetShuffleCommand"/>.</summary>
public sealed class SetShuffleHandler(IExamRepository repository, IExamAuthoringUnitOfWork unitOfWork, ExamDtoFactory dtos, Clock clock)
{
    /// <summary>Sets whether the exam's questions and options are shuffled. Draft exams only.</summary>
    /// <param name="command">The new settings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ExamNotFoundError">No exam has that id.</exception>
    /// <exception cref="ExamArchivedError">The exam is archived.</exception>
    /// <exception cref="ExamNotDraftError">The exam is already published.</exception>
    /// <exception cref="InvalidExamConfigError">One of the two settings was not sent.</exception>
    public async Task<ExamDto> HandleAsync(SetShuffleCommand command, CancellationToken cancellationToken)
    {
        // A body missing a flag binds it to null; that is a 400 about the missing value, not a silent "off".
        if (command is not { ShuffleQuestions: { } questions, ShuffleOptions: { } options })
            throw new InvalidExamConfigError("Send whether to shuffle the questions and whether to shuffle the options.");

        var exam = await repository.GetByIdOrThrowAsync(command.ExamId, cancellationToken);
        exam.SetShuffle(questions, options, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await dtos.ToDtoAsync(exam, cancellationToken);
    }
}

/// <summary>Handles <see cref="SetMaxAttemptsCommand"/>.</summary>
public sealed class SetMaxAttemptsHandler(IExamRepository repository, IExamAuthoringUnitOfWork unitOfWork, ExamDtoFactory dtos, Clock clock)
{
    /// <summary>Sets how many attempts every enrolled candidate has.</summary>
    /// <param name="command">The new setting.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ExamNotFoundError">No exam has that id.</exception>
    /// <exception cref="ExamArchivedError">The exam is archived.</exception>
    /// <exception cref="InvalidExamConfigError">No number was sent, or it is outside the allowed range.</exception>
    public async Task<ExamDto> HandleAsync(SetMaxAttemptsCommand command, CancellationToken cancellationToken)
    {
        // A body without the number binds to null, since JSON binding does not enforce the non-nullable annotation;
        // that is a 400 about the missing value, not a crash.
        if (command.MaxAttempts is null)
            throw new InvalidExamConfigError($"Choose a number of attempts from {ExamConfig.FewestAttempts} to {ExamConfig.MostAttempts}.");

        var exam = await repository.GetByIdOrThrowAsync(command.ExamId, cancellationToken);
        exam.SetMaxAttempts(command.MaxAttempts.Value, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await dtos.ToDtoAsync(exam, cancellationToken);
    }
}

/// <summary>Handles <see cref="SetContentProtectionCommand"/>.</summary>
public sealed class SetContentProtectionHandler(IExamRepository repository, IExamAuthoringUnitOfWork unitOfWork, ExamDtoFactory dtos, Clock clock)
{
    /// <summary>Turns the exam page's copy, paste, right-click and print protection on or off.</summary>
    /// <param name="command">The new setting.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ExamNotFoundError">No exam has that id.</exception>
    /// <exception cref="ExamArchivedError">The exam is archived.</exception>
    /// <exception cref="InvalidExamConfigError">The setting was not sent.</exception>
    public async Task<ExamDto> HandleAsync(SetContentProtectionCommand command, CancellationToken cancellationToken)
    {
        // A body without the flag binds it to null; that is a 400 about the missing value, not a silent "off".
        if (command.ContentProtection is null)
            throw new InvalidExamConfigError("Send whether content protection is on.");

        var exam = await repository.GetByIdOrThrowAsync(command.ExamId, cancellationToken);
        exam.SetContentProtection(command.ContentProtection.Value, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await dtos.ToDtoAsync(exam, cancellationToken);
    }
}

/// <summary>Handles <see cref="SetFocusViolationLimitCommand"/>.</summary>
public sealed class SetFocusViolationLimitHandler(IExamRepository repository, IExamAuthoringUnitOfWork unitOfWork, ExamDtoFactory dtos, Clock clock)
{
    /// <summary>Sets how many times a candidate may leave the exam page before the attempt is ended.</summary>
    /// <param name="command">The new limit.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ExamNotFoundError">No exam has that id.</exception>
    /// <exception cref="ExamArchivedError">The exam is archived.</exception>
    /// <exception cref="InvalidExamConfigError">The limit was not sent, or is outside the allowed range.</exception>
    public async Task<ExamDto> HandleAsync(SetFocusViolationLimitCommand command, CancellationToken cancellationToken)
    {
        // A body without the number binds it to null; that is a 400 about the missing value, not a silent "not watched".
        if (command.FocusViolationLimit is null)
            throw new InvalidExamConfigError("Send the violation limit; 0 turns the watch off.");

        var exam = await repository.GetByIdOrThrowAsync(command.ExamId, cancellationToken);
        exam.SetFocusViolationLimit(command.FocusViolationLimit.Value, clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await dtos.ToDtoAsync(exam, cancellationToken);
    }
}

/// <summary>Handles releasing the answers of a manual-release exam.</summary>
public sealed class ReleaseResultsHandler(IExamRepository repository, IExamAuthoringUnitOfWork unitOfWork, ExamDtoFactory dtos, Clock clock)
{
    /// <summary>Makes the answers visible to candidates from now on. Safe to repeat: the first release time is kept.</summary>
    /// <param name="examId">The exam whose answers to release.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ExamNotFoundError">No exam has that id.</exception>
    /// <exception cref="InvalidExamConfigError">The exam is not published, or is not set to manual release.</exception>
    public async Task<ExamDto> HandleAsync(Guid examId, CancellationToken cancellationToken)
    {
        var exam = await repository.GetByIdOrThrowAsync(examId, cancellationToken);
        exam.ReleaseResults(clock.UtcNow);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await dtos.ToDtoAsync(exam, cancellationToken);
    }
}
