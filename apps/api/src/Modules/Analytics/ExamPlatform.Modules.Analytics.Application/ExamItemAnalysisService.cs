using ExamPlatform.Modules.Analytics.Contracts;
using ExamPlatform.Modules.Analytics.Domain;
using ExamPlatform.Modules.Analytics.Domain.Exceptions;
using ExamPlatform.Modules.ExamRuntime.Contracts;
using ExamPlatform.Modules.QuestionBank.Contracts;

namespace ExamPlatform.Modules.Analytics.Application;

/// <summary>
/// Builds an exam's item analysis (FR-37) from the released results ExamRuntime exposes and the question texts the bank holds. It reads
/// responses through <see cref="IExamResponseReader"/> and questions through <see cref="IQuestionBank"/>, never another module's storage.
/// </summary>
public sealed class ExamItemAnalysisService(IExamResponseReader responses, IQuestionBank questionBank, ItemAnalysisPolicy policy) : IExamItemAnalysis
{
    /// <inheritdoc />
    public async Task<ExamItemAnalysisDto> GetAsync(Guid examId, CancellationToken cancellationToken)
    {
        var read = await responses.ReadAsync(examId, cancellationToken) ?? throw new ExamNotFoundError();

        // With the results held, no attempt counts, so there is nothing to show; the caller is told why, not handed empty rows.
        if (!read.ResultsReleased)
            return new ExamItemAnalysisDto(read.ExamId, read.ExamName, false, 0, policy.MinimumCohortSize, 0, []);

        var best = BestAttemptPerCandidate(read.Attempts);
        var groups = ItemStatistics.GroupsOf(best.Select(a => new CandidateScore(a.CandidateId, a.Score)).ToList());
        var answers = AnswersByQuestion(best);
        var texts = await PreviewsAsync(read.QuestionIds, cancellationToken);

        var rows = read.QuestionIds
            .Select((questionId, index) => RowOf(questionId, index + 1, groups, answers, texts))
            .ToList();

        return new ExamItemAnalysisDto(
            read.ExamId,
            read.ExamName,
            true,
            best.Count,
            policy.MinimumCohortSize,
            ItemStatistics.GroupSize(best.Count),
            rows);
    }

    /// <summary>
    /// Keeps each candidate's best counted attempt, so a candidate who sat an exam twice counts once. That is the same rule the leaderboard and
    /// the rank use (FR-32): a retake must not give one candidate two places in the cohort.
    /// </summary>
    private static IReadOnlyList<AttemptResponsesOfCandidate> BestAttemptPerCandidate(IReadOnlyList<AttemptResponses> attempts) =>
        attempts
            .GroupBy(a => a.CandidateId)
            .Select(group => group.OrderByDescending(a => a.Score).ThenByDescending(a => a.SubmittedAtUtc).First())
            .Select(a => new AttemptResponsesOfCandidate(a.CandidateId, a.Score, a.Questions))
            .ToList();

    private static Dictionary<Guid, List<ItemAnswer>> AnswersByQuestion(IReadOnlyList<AttemptResponsesOfCandidate> best)
    {
        var byQuestion = new Dictionary<Guid, List<ItemAnswer>>();
        foreach (var attempt in best)
        {
            foreach (var question in attempt.Questions)
            {
                if (!byQuestion.TryGetValue(question.QuestionId, out var list))
                    byQuestion[question.QuestionId] = list = [];

                list.Add(new ItemAnswer(attempt.CandidateId, question.Correct));
            }
        }

        return byQuestion;
    }

    private async Task<Dictionary<Guid, string>> PreviewsAsync(IReadOnlyList<Guid> questionIds, CancellationToken cancellationToken)
    {
        if (questionIds.Count == 0)
            return new Dictionary<Guid, string>();

        var questions = await questionBank.GetAsync(questionIds, cancellationToken);
        return questions.ToDictionary(q => q.Id, q => QuestionText.Preview(q.Text));
    }

    private ItemRowDto RowOf(
        Guid questionId,
        int position,
        CohortGroups groups,
        IReadOnlyDictionary<Guid, List<ItemAnswer>> answers,
        IReadOnlyDictionary<Guid, string> texts)
    {
        // A question of the exam that the bank cannot return is a fault to surface, not a row to leave blank.
        if (!texts.TryGetValue(questionId, out var text))
            throw new InvalidOperationException($"Question {questionId} of the exam cannot be read from the question bank.");

        // A question nobody on a counted paper had gets no answers, so its attempts are zero and its indices are withheld.
        var questionAnswers = answers.TryGetValue(questionId, out var found) ? found : new List<ItemAnswer>();
        var indices = ItemStatistics.IndicesOf(groups, questionAnswers, policy.MinimumCohortSize);

        return new ItemRowDto(questionId, position, text, indices.Attempts, indices.CorrectCount, indices.Difficulty, indices.Discrimination);
    }

    /// <summary>A candidate's best counted attempt, reduced to what the analysis reads.</summary>
    private sealed record AttemptResponsesOfCandidate(
        Guid CandidateId, decimal Score, IReadOnlyList<QuestionResponse> Questions);
}
