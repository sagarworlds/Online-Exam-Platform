using ExamPlatform.Modules.QuestionBank.Application.Dtos;
using ExamPlatform.Modules.QuestionBank.Contracts;

namespace ExamPlatform.Modules.QuestionBank.Application;

/// <summary>
/// Works out where questions are in use by asking every <see cref="IQuestionUsageSource"/> the host has registered.
/// The only place the bank combines those answers, so the list, the edit rule and the delete rule can never disagree.
/// </summary>
public sealed class QuestionUsageReader(IEnumerable<IQuestionUsageSource> sources)
{
    /// <summary>Reads the usage of several questions with one question to each source, however many questions there are.</summary>
    /// <param name="questionIds">The questions to look up.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The usage of every requested question; a question nothing uses maps to <see cref="QuestionUsageDto.Unused"/>.</returns>
    public async Task<IReadOnlyDictionary<Guid, QuestionUsageDto>> ReadAsync(IReadOnlyCollection<Guid> questionIds, CancellationToken cancellationToken)
    {
        if (questionIds.Count == 0)
            return new Dictionary<Guid, QuestionUsageDto>();

        // One after the other: the sources share a request's database connections, which do not run two queries at once.
        var uses = new List<QuestionUse>();
        foreach (var source in sources)
            uses.AddRange(await source.FindAsync(questionIds, cancellationToken));

        var byQuestion = uses.ToLookup(u => u.QuestionId);

        return questionIds.Distinct().ToDictionary(id => id, id => Summarise(byQuestion[id].ToList()));
    }

    /// <summary>Reads the usage of one question.</summary>
    /// <param name="questionId">The question to look up.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<QuestionUsageDto> ReadOneAsync(Guid questionId, CancellationToken cancellationToken) =>
        (await ReadAsync([questionId], cancellationToken))[questionId];

    private static QuestionUsageDto Summarise(IReadOnlyList<QuestionUse> uses)
    {
        if (uses.Count == 0)
            return QuestionUsageDto.Unused;

        var exams = uses.Where(u => u.Kind == QuestionUseKind.InExam).Select(u => u.Description).Order().ToList();

        return new QuestionUsageDto(
            exams.Count,
            exams.Take(QuestionUsageDto.MaxNamedExams).ToList(),
            uses.Any(u => u.Kind == QuestionUseKind.Answered));
    }
}
