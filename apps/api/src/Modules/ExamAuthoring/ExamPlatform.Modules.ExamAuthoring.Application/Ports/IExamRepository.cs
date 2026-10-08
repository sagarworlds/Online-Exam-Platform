using ExamPlatform.Modules.ExamAuthoring.Domain;

namespace ExamPlatform.Modules.ExamAuthoring.Application.Ports;

/// <summary>Persistence port for the <see cref="Exam"/> aggregate.</summary>
public interface IExamRepository
{
    /// <summary>Starts tracking a new exam; it is stored when the unit of work saves.</summary>
    /// <param name="exam">The exam to add.</param>
    void Add(Exam exam);

    /// <summary>Loads an exam with its sections and their questions, tracked so changes to it are saved.</summary>
    /// <param name="examId">The exam's id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The exam, or <see langword="null"/> when none has that id.</returns>
    Task<Exam?> GetByIdAsync(Guid examId, CancellationToken cancellationToken = default);

    /// <summary>Like <see cref="GetByIdAsync"/>, but a missing exam is an error.</summary>
    /// <param name="examId">The exam's id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="Domain.Exceptions.ExamNotFoundError">No exam has that id.</exception>
    Task<Exam> GetByIdOrThrowAsync(Guid examId, CancellationToken cancellationToken = default);

    /// <summary>Lists the exams of a series.</summary>
    /// <param name="seriesId">The series.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<Exam>> ListBySeriesAsync(Guid seriesId, CancellationToken cancellationToken = default);

    /// <summary>Reads exams by id with their sections and questions, untracked, for callers that only read.</summary>
    /// <param name="examIds">The ids to read; unknown ids are skipped.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<Exam>> ListByIdsAsync(IReadOnlyCollection<Guid> examIds, CancellationToken cancellationToken = default);

    /// <summary>Finds the exams that contain any of the given questions, without loading the exams themselves.</summary>
    /// <param name="questionIds">The question-bank ids to look for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One entry per question and exam it sits in; a question in no exam has none.</returns>
    Task<IReadOnlyList<ExamQuestionUse>> ListUsesOfQuestionsAsync(IReadOnlyCollection<Guid> questionIds, CancellationToken cancellationToken = default);

    /// <summary>Reads the published exams that start after <paramref name="afterUtc"/> and no later than <paramref name="untilUtc"/>, with their sections, untracked.</summary>
    /// <param name="afterUtc">The earliest start, exclusive.</param>
    /// <param name="untilUtc">The latest start, inclusive.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<Exam>> ListPublishedStartingBetweenAsync(DateTime afterUtc, DateTime untilUtc, CancellationToken cancellationToken = default);

    /// <summary>Lists the most recently created exams, newest first, without their sections.</summary>
    /// <param name="take">How many to return at most.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<Exam>> ListNewestAsync(int take, CancellationToken cancellationToken = default);
}
