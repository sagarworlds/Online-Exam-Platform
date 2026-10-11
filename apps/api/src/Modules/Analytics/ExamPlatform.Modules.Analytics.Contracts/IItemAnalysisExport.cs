namespace ExamPlatform.Modules.Analytics.Contracts;

/// <summary>
/// Exports an exam's item analysis as a file for staff (FR-38). Every export is recorded in the platform's audit log before the file is
/// handed out, so an export that cannot be recorded is not made.
/// </summary>
public interface IItemAnalysisExport
{
    /// <summary>Builds the item analysis file and records who asked for it and when.</summary>
    /// <param name="request">The exam, and who asked for the file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The file, ready to download.</returns>
    /// <exception cref="ExamPlatform.SharedKernel.Domain.DomainException">No exam has that id, so there is nothing to export; nothing is recorded.</exception>
    Task<ExportedReport> ExportAsync(ItemAnalysisExportRequest request, CancellationToken cancellationToken);
}

/// <summary>Who asked for an item analysis export, and of which exam.</summary>
/// <param name="ExamId">The exam.</param>
/// <param name="ActorUserId">The staff member asking, taken from their token.</param>
/// <param name="ActorRole">The staff member's primary role, a snapshot for the audit trail.</param>
/// <param name="CorrelationId">Ties the audit entry to the request that made it; null outside a request.</param>
public sealed record ItemAnalysisExportRequest(Guid ExamId, Guid ActorUserId, string ActorRole, string? CorrelationId);

/// <summary>A report ready to download.</summary>
/// <param name="FileName">The name to save it as, including the extension.</param>
/// <param name="ContentType">The media type of the content, e.g. <c>text/csv</c>.</param>
/// <param name="Content">The file's bytes.</param>
/// <param name="RowCount">How many questions the report lists.</param>
public sealed record ExportedReport(string FileName, string ContentType, byte[] Content, int RowCount);
