using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Application.Ports;
using ExamPlatform.Modules.ExamRuntime.Contracts;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.Modules.QuestionBank.Contracts;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.ExamRuntime.Application;

/// <summary>
/// Serves how an exam's candidates answered it to other modules (<see cref="IExamResponseReader"/>), for item analysis (FR-37). It decides
/// which attempts count, the same release rule as the candidate's own results, and marks each answer with the same scorer.
/// </summary>
/// <remarks>
/// Questions are read in one batch for the whole exam rather than once per candidate, so a large exam costs two bank reads, not one per
/// attempt. Each question is read at the version its attempt sat, so a key corrected after a candidate sat the exam does not change how
/// that candidate's answer is judged.
/// </remarks>
public sealed class ExamResponseReader(IAttemptRepository attempts, IExamCatalog catalog, IQuestionBank questionBank, Clock clock) : IExamResponseReader
{
    /// <inheritdoc />
    public async Task<ExamResponses?> ReadAsync(Guid examId, CancellationToken cancellationToken)
    {
        var exam = await catalog.FindAsync(examId, cancellationToken);
        if (exam is null)
            return null;

        // Held results are not analysed at all: until the author releases them, the scores can still change, and a dispute may follow.
        if (!ResultRelease.IsReleased(exam, clock.UtcNow))
            return new ExamResponses(exam.Id, exam.Name, ResultsReleased: false, QuestionIds: OrderOf(exam, []), Attempts: []);

        var counted = await attempts.ListCountedForExamAsync(examId, cancellationToken);
        var papers = counted.Select(attempt => (Attempt: attempt, Exam: exam.For(attempt))).ToList();
        var questions = await ReadQuestionsAsync(papers, cancellationToken);

        var responses = papers
            .Select(p => ResponsesOf(p.Attempt, p.Exam, questions))
            .ToList();

        return new ExamResponses(exam.Id, exam.Name, ResultsReleased: true, QuestionIds: OrderOf(exam, papers.Select(p => p.Exam)), Attempts: responses);
    }

    /// <summary>
    /// The exam's own question order first, then any question that only a drawn paper held, in the order first met.
    /// </summary>
    private static IReadOnlyList<Guid> OrderOf(ExamSnapshot exam, IEnumerable<ExamSnapshot> papers)
    {
        var ordered = exam.Sections.OrderBy(s => s.Order).SelectMany(s => s.QuestionIds);
        var drawn = papers.SelectMany(p => p.Sections.OrderBy(s => s.Order).SelectMany(s => s.QuestionIds));
        return ordered.Concat(drawn).Distinct().ToList();
    }

    /// <summary>
    /// Reads every question the counted papers hold, each at the version its attempt recorded for it. A question with no recorded version
    /// (an attempt made before versions were kept) is read as it is now, which is all that was ever possible for that attempt.
    /// </summary>
    private async Task<Dictionary<(Guid QuestionId, int? Version), QuestionSnapshot>> ReadQuestionsAsync(
        IEnumerable<(Attempt Attempt, ExamSnapshot Exam)> papers, CancellationToken cancellationToken)
    {
        var refs = papers
            .SelectMany(p => p.Exam.Sections.SelectMany(s => s.QuestionIds)
                .Select(id => new QuestionVersionRef(id, p.Attempt.QuestionVersionOf(id))))
            .Distinct()
            .ToList();

        var pinned = refs.Where(r => r.VersionNumber is not null).ToList();
        var current = refs.Where(r => r.VersionNumber is null).Select(r => r.QuestionId).Distinct().ToList();

        var found = new Dictionary<(Guid QuestionId, int? Version), QuestionSnapshot>();
        if (pinned.Count > 0)
        {
            foreach (var question in await questionBank.GetVersionsAsync(pinned, cancellationToken))
                found[(question.Id, question.VersionNumber)] = question;
        }

        if (current.Count > 0)
        {
            foreach (var question in await questionBank.GetAsync(current, cancellationToken))
                found[(question.Id, null)] = question;
        }

        return found;
    }

    private static AttemptResponses ResponsesOf(
        Attempt attempt, ExamSnapshot paperExam, IReadOnlyDictionary<(Guid QuestionId, int? Version), QuestionSnapshot> questions)
    {
        var answers = attempt.Answers.ToDictionary(a => a.QuestionId);
        var responses = paperExam.Sections
            .SelectMany(s => s.QuestionIds)
            .Select(questionId =>
            {
                // A question the bank cannot return cannot be judged either way; the analysis is refused rather than guessing.
                var version = attempt.QuestionVersionOf(questionId);
                var question = questions.GetValueOrDefault((questionId, version)) ?? throw new ExamContentUnavailableError();
                var answer = answers.GetValueOrDefault(questionId);
                var mark = AttemptScorer.Mark(paperExam, question, answer?.SelectedOptionIds ?? [], answer?.AnswerText);
                return new QuestionResponse(questionId, mark.Verdict == AnswerVerdict.Correct);
            })
            .ToList();

        return new AttemptResponses(
            attempt.Id,
            attempt.CandidateId,
            attempt.SubmittedAtUtc ?? throw new InvalidOperationException("A counted attempt always has a submission time."),
            attempt.Score ?? throw new InvalidOperationException("A submitted attempt always has a score."),
            responses);
    }
}
