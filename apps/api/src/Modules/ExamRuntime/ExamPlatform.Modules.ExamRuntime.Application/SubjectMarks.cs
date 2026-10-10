using ExamPlatform.Modules.ExamAuthoring.Contracts;
using ExamPlatform.Modules.ExamRuntime.Domain;
using ExamPlatform.Modules.ExamRuntime.Domain.Exceptions;
using ExamPlatform.Modules.QuestionBank.Contracts;

namespace ExamPlatform.Modules.ExamRuntime.Application;

/// <summary>
/// The marks each attempt earned in each subject of the exam (FR-35): the questions are grouped by the subject of their book, and marked the
/// same way the review marks them, so a subject board and the overall board cannot disagree about a question.
/// </summary>
public sealed class SubjectMarks(IQuestionBank questionBank, IQuestionSubjects questionSubjects)
{
    /// <summary>Marks the given attempts by subject.</summary>
    /// <param name="exam">The exam, whose marking scheme applies.</param>
    /// <param name="attempts">The attempts, with their answers and drawn papers loaded.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The subjects found and the marks of each attempt in each of them.</returns>
    /// <exception cref="ExamContentUnavailableError">A question on a paper cannot be read, or a saved answer names an option it does not have.</exception>
    public async Task<SubjectTable> BuildAsync(ExamSnapshot exam, IReadOnlyList<Attempt> attempts, CancellationToken cancellationToken)
    {
        // Each attempt is marked against its own paper, so a drawn paper is read as the candidate sat it.
        var papers = attempts.ToDictionary(a => a.Id, a => (Attempt: a, Exam: exam.For(a)));
        var questionIds = papers.Values.SelectMany(p => p.Exam.Sections.SelectMany(s => s.QuestionIds)).Distinct().ToList();
        var subjectOf = await questionSubjects.GetSubjectsAsync(questionIds, cancellationToken);
        var snapshots = await ReadPapersAsync(papers.Values, questionBank, cancellationToken);

        var byAttempt = new Dictionary<Guid, IReadOnlyDictionary<string, decimal>>();
        foreach (var (attemptId, (attempt, paper)) in papers)
        {
            var totals = new Dictionary<string, decimal>(StringComparer.Ordinal);
            var answers = attempt.Answers.ToDictionary(a => a.QuestionId);
            foreach (var questionId in paper.Sections.SelectMany(s => s.QuestionIds))
            {
                // A question with no subject belongs to no subject board, so it is not marked for one.
                if (!subjectOf.TryGetValue(questionId, out var subject))
                    continue;

                var question = snapshots[attemptId].GetValueOrDefault(questionId) ?? throw new ExamContentUnavailableError();
                var answer = answers.GetValueOrDefault(questionId);
                var mark = AttemptScorer.Mark(paper, question, answer?.SelectedOptionIds ?? [], answer?.AnswerText).Marks;
                totals[subject] = totals.GetValueOrDefault(subject) + mark;
            }

            byAttempt[attemptId] = totals;
        }

        var subjects = subjectOf.Values.Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new SubjectTable(subjects, byAttempt);
    }

    /// <summary>
    /// Reads every attempt's questions as the candidate sat them. Attempts that were pinned to the same versions and drew the same questions are
    /// read in one call, so a large exam costs one bank read for each distinct paper, not one for each candidate.
    /// </summary>
    private async Task<Dictionary<Guid, Dictionary<Guid, QuestionSnapshot>>> ReadPapersAsync(
        IEnumerable<(Attempt Attempt, ExamSnapshot Paper)> papers, IQuestionBank bank, CancellationToken cancellationToken)
    {
        var result = new Dictionary<Guid, Dictionary<Guid, QuestionSnapshot>>();
        var groups = new Dictionary<string, List<(Attempt Attempt, List<Guid> Ids)>>(StringComparer.Ordinal);
        foreach (var (attempt, paper) in papers)
        {
            var ids = paper.Sections.SelectMany(s => s.QuestionIds).Distinct().ToList();
            var key = PaperKey(attempt, ids);
            if (!groups.TryGetValue(key, out var members))
                groups[key] = members = [];
            members.Add((attempt, ids));
        }

        foreach (var members in groups.Values)
        {
            var representative = members[0];
            var read = await AttemptQuestionReads.ReadAsync(bank, representative.Attempt, representative.Ids, cancellationToken);
            // The members share one read, which is never changed after it is made.
            foreach (var member in members)
                result[member.Attempt.Id] = read;
        }

        return result;
    }

    /// <summary>Two attempts with the same questions at the same versions read the same content, so they share one read.</summary>
    private static string PaperKey(Attempt attempt, IEnumerable<Guid> questionIds) =>
        string.Join(";", questionIds.OrderBy(id => id).Select(id => $"{id}@{attempt.QuestionVersionOf(id)?.ToString() ?? "current"}"));
}

/// <summary>The subjects of an exam's questions and what each attempt earned in each.</summary>
/// <param name="Subjects">The subjects, alphabetically.</param>
/// <param name="ByAttempt">For each attempt, its marks by subject; a subject the attempt had no question in is absent.</param>
public sealed record SubjectTable(IReadOnlyList<string> Subjects, IReadOnlyDictionary<Guid, IReadOnlyDictionary<string, decimal>> ByAttempt)
{
    /// <summary>Whether the attempt had at least one question in the subject, so it belongs on that subject's board.</summary>
    /// <param name="attemptId">The attempt.</param>
    /// <param name="subject">The subject.</param>
    public bool HasSubject(Guid attemptId, string subject) =>
        ByAttempt.TryGetValue(attemptId, out var totals) && totals.ContainsKey(subject);

    /// <summary>The marks the attempt earned in the subject; zero when it had no question there.</summary>
    /// <param name="attemptId">The attempt.</param>
    /// <param name="subject">The subject.</param>
    public decimal ScoreOf(Guid attemptId, string subject) =>
        ByAttempt.TryGetValue(attemptId, out var totals) && totals.TryGetValue(subject, out var score) ? score : 0m;
}
