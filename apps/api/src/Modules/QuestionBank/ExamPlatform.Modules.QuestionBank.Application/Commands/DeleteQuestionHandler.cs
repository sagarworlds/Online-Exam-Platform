using ExamPlatform.Modules.QuestionBank.Application.Dtos;
using ExamPlatform.Modules.QuestionBank.Application.Ports;
using ExamPlatform.Modules.QuestionBank.Domain.Exceptions;

namespace ExamPlatform.Modules.QuestionBank.Application.Commands;

/// <summary>Deletes a question that nothing uses (FR-5).</summary>
public sealed class DeleteQuestionHandler(IQuestionRepository repository, IQuestionBankUnitOfWork unitOfWork, QuestionUsageReader usageReader)
{
    /// <summary>How many exam names the refusal spells out before it says "and N more".</summary>
    private const int NamedExamsInReason = 3;

    /// <summary>Deletes the question and its options, unless an exam holds it or a candidate has answered it.</summary>
    /// <remarks>
    /// Exams read their questions live from the bank and a result points at the question it answers, so a question that is in
    /// use can only be corrected, never removed. An exam takes a question in before candidates can answer it, so "in an exam"
    /// already covers every answered question; the answered check is a second line of defence, not a different rule.
    /// </remarks>
    /// <param name="questionId">The question to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="QuestionNotFoundError">No question has that id.</exception>
    /// <exception cref="QuestionInUseError">An exam holds the question, or a candidate has answered it.</exception>
    public async Task HandleAsync(Guid questionId, CancellationToken cancellationToken)
    {
        var question = await repository.GetByIdAsync(questionId, cancellationToken) ?? throw new QuestionNotFoundError();

        var usage = await usageReader.ReadOneAsync(questionId, cancellationToken);
        if (usage.IsInAnyExam || usage.Answered)
            throw new QuestionInUseError(ReasonFor(usage));

        repository.Remove(question);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static string ReasonFor(QuestionUsageDto usage)
    {
        if (!usage.IsInAnyExam)
            return "Candidates have answered this question, so it cannot be deleted.";

        var named = usage.ExamNames.Take(NamedExamsInReason).Select(name => $"\"{name}\"").ToList();
        var more = usage.ExamCount - named.Count;
        var list = string.Join(", ", named) + (more > 0 ? $" and {more} more" : string.Empty);

        return $"It is part of the {(usage.ExamCount == 1 ? "exam" : "exams")} {list}, so it cannot be deleted.";
    }
}
