using ExamPlatform.Modules.ExamRuntime.Domain;

namespace ExamPlatform.Modules.ExamRuntime.Application.Ports;

/// <summary>Persistence port for <see cref="IssueReport"/>.</summary>
public interface IIssueReportRepository
{
    /// <summary>Starts tracking a new report; it is stored when the unit of work saves.</summary>
    /// <param name="report">The report to add.</param>
    void Add(IssueReport report);

    /// <summary>Loads one report, tracked so resolving it is saved.</summary>
    /// <param name="issueReportId">The report's id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The report, or <see langword="null"/> when none has that id.</returns>
    Task<IssueReport?> GetByIdAsync(Guid issueReportId, CancellationToken cancellationToken);

    /// <summary>Counts the reports made in one attempt, resolved or not.</summary>
    /// <param name="attemptId">The attempt.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<int> CountForAttemptAsync(Guid attemptId, CancellationToken cancellationToken);

    /// <summary>Lists reports with the given status, oldest first, so the longest-waiting is at the top of a queue.</summary>
    /// <param name="status">Which reports to list.</param>
    /// <param name="take">How many to return at most.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<IssueReport>> ListAsync(IssueReportStatus status, int take, CancellationToken cancellationToken);
}
