using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application.Dtos;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.Modules.QuestionBank.Contracts;
using ExamPlatform.SharedKernel.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Application.Queries;

/// <summary>Shows staff the questions one attempt consisted of.</summary>
public sealed class GetAttemptPaperHandler(
    IExamCatalog catalog, IAttemptRepository attempts, IQuestionBank questionBank, IProctorPolicy proctorPolicy, ITextAnswerEvaluator? evaluator = null)
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
        var paper = exam.For(attempt, proctorPolicy);

        var ids = paper.Sections.SelectMany(s => s.QuestionIds).ToList();
        var questions = await questionBank.ReadAsync(attempt, ids, cancellationToken);
        var answers = attempt.Answers.ToDictionary(a => a.QuestionId);
        var submitted = attempt.Status == AttemptStatus.Submitted;
        // A typed answer is checked by the AI evaluator only once the attempt is over, so an open one shows nothing of it.
        var over = attempt.Status != AttemptStatus.InProgress;

        var sections = paper.Sections
            .OrderBy(s => s.Order)
            .Select(s => new AttemptPaperSectionDto(
                s.Id,
                s.Name,
                AttemptOrdering.Arrange(s.QuestionIds, id => id, attempt.Id, attempt.Number, s.Id, paper.ShuffleQuestions)
                    .Select(id => Question(attempt, paper, questions.GetValueOrDefault(id), id, !fixedIds.Contains(id), answers, submitted, over))
                    .ToList()))
            .ToList();

        return new AttemptPaperDto(
            attempt.Id, attempt.Number, sections.Any(s => s.Questions.Any(q => q.Drawn)), sections,
            attempt.Status, attempt.Score, attempt.MaxScore, attempt.IsInvalidated,
            AiCheckAvailable: over && !attempt.IsInvalidated && evaluator is { IsEnabled: true });
    }

    // Where the evaluator stands on a typed answer that the accepted answers do not settle; nothing for any other answer, which it is never asked about.
    private static AttemptPaperAiCheckDto? AiCheckOf(QuestionSnapshot question, AttemptAnswer? answer)
    {
        if (!question.IsTextAnswer || answer is null || string.IsNullOrWhiteSpace(answer.AnswerText))
            return null;
        if (TypedAnswer.Matches(answer.AnswerText, question.AcceptedAnswers ?? [], question.MatchRule))
            return null;

        var state = answer.IsAwaitingEvaluation ? AiCheckState.Waiting
            : answer.EvaluationGaveUpAtUtc is not null ? AiCheckState.GaveUp
            : answer.TextVerdict is not null ? AiCheckState.Judged
            : AiCheckState.NotChecked;
        return new AttemptPaperAiCheckDto(
            state, answer.TextVerdict, answer.EvaluationReason, answer.EvaluationModel,
            answer.EvaluatedAtUtc ?? answer.EvaluationGaveUpAtUtc, answer.EvaluationFailures, answer.EvaluationLastError);
    }

    private static AttemptPaperQuestionDto Question(
        Attempt attempt, ExamSnapshot paper, QuestionSnapshot? question, Guid id, bool drawn,
        IReadOnlyDictionary<Guid, AttemptAnswer> answers, bool submitted, bool over)
    {
        // The bank no longer has the question: say so rather than failing the whole paper.
        if (question is null)
            return new AttemptPaperQuestionDto(id, null, drawn);

        var answer = answers.GetValueOrDefault(id);
        var chosenIds = answer?.SelectedOptionIds ?? [];
        var options = AttemptOrdering.Arrange(question.Options, o => o.Id, attempt.Id, attempt.Number, question.Id, paper.ShuffleOptions, o => o.IsPinned)
            .Select(o => new ReviewOptionDto(o.Id, o.Text, o.IsCorrect, chosenIds.Contains(o.Id))).ToList();
        // Staff see the accepted answers of a text question, and what the candidate typed, beside the paper.
        var accepted = question.IsTextAnswer ? question.AcceptedAnswers : null;
        var aiCheck = over ? AiCheckOf(question, answer) : null;
        if (!submitted)
            return new AttemptPaperQuestionDto(
                id, question.Text, drawn, options, AllowsMultiple: question.AllowsMultiple,
                IsTextAnswer: question.IsTextAnswer, AnswerText: answer?.AnswerText, AcceptedAnswers: accepted, AiCheck: aiCheck);

        var mark = AttemptScorer.Mark(paper, question, chosenIds, answer?.AnswerText, answer?.TextVerdict);
        return new AttemptPaperQuestionDto(
            id, question.Text, drawn, options, mark.Verdict, mark.Marks, question.AllowsMultiple,
            IsTextAnswer: question.IsTextAnswer, AnswerText: answer?.AnswerText, AcceptedAnswers: accepted, AiCheck: aiCheck);
    }
}
