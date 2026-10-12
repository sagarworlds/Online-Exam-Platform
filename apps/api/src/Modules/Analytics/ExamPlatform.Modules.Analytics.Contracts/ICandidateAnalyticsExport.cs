namespace ExamPlatform.Modules.Analytics.Contracts;

/// <summary>
/// Exports a candidate's own analytics as a file (FR-38). The file holds the figures the candidate's analytics page shows and nothing about any
/// other candidate. Every export is recorded in the audit log before the file is handed out, as the staff export is.
/// </summary>
public interface ICandidateAnalyticsExport
{
    /// <summary>Builds the candidate's analytics file and records the export.</summary>
    /// <param name="request">The candidate, who is also the one asking, and the format wanted.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The file, ready to download. A candidate with no released result gets a file that says so, not an error.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The format is not one this module writes.</exception>
    Task<ExportedReport> ExportAsync(CandidateAnalyticsExportRequest request, CancellationToken cancellationToken);
}

/// <summary>Whose analytics to export, who asked for them, and in which format.</summary>
/// <param name="CandidateId">The candidate whose figures are exported. The caller takes it from the signed-in user, never from a request.</param>
/// <param name="ActorRole">The candidate's primary role, a snapshot for the audit trail.</param>
/// <param name="CorrelationId">Ties the audit entry to the request that made it; null outside a request.</param>
/// <param name="Format">The file format wanted.</param>
public sealed record CandidateAnalyticsExportRequest(Guid CandidateId, string ActorRole, string? CorrelationId, ReportFormat Format);
