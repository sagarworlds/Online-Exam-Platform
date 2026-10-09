using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application.Dtos;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.Modules.QuestionBank.Contracts;

namespace ExamPlatform.Modules.ExamRuntime.Application.Queries;

/// <summary>Shows staff the questions one attempt consisted of.</summary>
public sealed class GetAttemptPaperHandler(IExamCatalog catalog, IAttemptRepository attempts, IQuestionBank questionBank)
{
    /// <summary>
    /// Builds the paper of an attempt: the drawn one if the exam draws, otherwise the exam's fixed questions, in the order and with the
    /// wording this candidate saw (FR-7). Each question carries the options with the correct one and the candidate's choice marked, and,
    /// once the attempt is submitted, the verdict and the marks it earned, so staff can read a result against the paper.
    /// </summary>
    /// <param name="examId">The exam the attempt belongs to.</param>
    /// <param name="attemptId">The attempt.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="AttemptNotFoundError">No such attempt, or it is not an attempt at that exam.</exception>
    /// <exception cref="ExamContentUnavailableError">The attempt's exam can no longer be read.</exception>
    public async Task<AttemptPaperDto> HandleAsync(Guid examId, Guid attemptId, CancellationToken cancellationToken)
    {
        var attempt = await attempts.GetByIdAsync(attemptId, cancellationToken);
        // The exam id in the route must match, so a staff link cannot be used to read an attempt of some other exam by accident.
        if (attempt is null || attempt.ExamId != examId)
            throw new AttemptNotFoundError();

        var exam = await catalog.FindAsync(examId, cancellationToken) ?? throw new ExamContentUnavailableError();
        var fixedIds = exam.Sections.SelectMany(s => s.QuestionIds).ToHashSet();
        var paper = exam.For(attempt);

        var ids = paper.Sections.SelectMany(s => s.QuestionIds).ToList();
        var questions = await questionBank.ReadAsync(attempt, ids, cancellationToken);
        var chosen = attempt.Answers.ToDictionary(a => a.QuestionId, a => (IReadOnlyCollection<Guid>)a.SelectedOptionIds);
        var submitted = attempt.Status == AttemptStatus.Submitted;

        var sections = paper.Sections
            .OrderBy(s => s.Order)
            .Select(s => new AttemptPaperSectionDto(
                s.Id,
                s.Name,
                AttemptOrdering.Arrange(s.QuestionIds, id => id, attempt.Id, attempt.Number, s.Id, paper.ShuffleQuestions)
                    .Select(id => Question(attempt, paper, questions.GetValueOrDefault(id), id, !fixedIds.Contains(id), chosen, submitted))
                    .ToList()))
            .ToList();

        return new AttemptPaperDto(
            attempt.Id, attempt.Number, sections.Any(s => s.Questions.Any(q => q.Drawn)), sections,
            attempt.Status, attempt.Score, attempt.MaxScore, attempt.IsInvalidated);
    }

    private static AttemptPaperQuestionDto Question(
        Attempt attempt, ExamSnapshot paper, QuestionSnapshot? question, Guid id, bool drawn,
        IReadOnlyDictionary<Guid, IReadOnlyCollection<Guid>> chosen, bool submitted)
    {
        // The bank no longer has the question: say so rather than failing the whole paper.
        if (question is null)
            return new AttemptPaperQuestionDto(id, null, drawn);

        var chosenIds = chosen.TryGetValue(id, out var optionIds) ? optionIds : [];
        var options = AttemptOrdering.Arrange(question.Options, o => o.Id, attempt.Id, attempt.Number, question.Id, paper.ShuffleOptions, o => o.IsPinned)
            .Select(o => new ReviewOptionDto(o.Id, o.Text, o.IsCorrect, chosenIds.Contains(o.Id))).ToList();
        if (!submitted)
            return new AttemptPaperQuestionDto(id, question.Text, drawn, options, AllowsMultiple: question.AllowsMultiple);

        var mark = AttemptScorer.Mark(paper, question, chosenIds);
        return new AttemptPaperQuestionDto(id, question.Text, drawn, options, mark.Verdict, mark.Marks, question.AllowsMultiple);
    }
}
