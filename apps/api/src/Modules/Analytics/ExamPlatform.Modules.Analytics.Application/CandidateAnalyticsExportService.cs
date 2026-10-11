using ExamPlatform.Modules.Admin.Contracts;
using ExamPlatform.Modules.Analytics.Application.Ports;
using ExamPlatform.Modules.Analytics.Contracts;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.Analytics.Application;

/// <summary>
/// Exports a candidate's own analytics (FR-38) as CSV or PDF. The figures are the ones the candidate's analytics page shows, built by
/// <see cref="ICandidateAnalytics"/> from released results only, so the file cannot hold a result the candidate could not open.
/// </summary>
/// <remarks>
/// The file carries no name, no candidate id and nothing about another candidate. Its figures are the candidate's own, and the candidate
/// already sees them on screen, so the export adds no new disclosure. The export is audited like the staff exports: the entry names the
/// candidate as the actor and records the format, so a copy taken from a shared device can be traced.
/// </remarks>
public sealed class CandidateAnalyticsExportService(
    ICandidateAnalytics analytics,
    ICandidateAnalyticsCsvWriter csvWriter,
    ICandidateAnalyticsPdfWriter pdfWriter,
    IAuditLogger audit,
    Clock clock) : ICandidateAnalyticsExport
{
    /// <summary>The audit action recorded for every export, the same one the staff exports use.</summary>
    public const string ExportAction = "Analytics.ReportExported";

    /// <summary>The report's name in the audit trail.</summary>
    public const string ReportName = "candidate-analytics";

    /// <summary>The audit entity type: the candidate whose figures were exported.</summary>
    public const string EntityType = "Candidate";

    /// <inheritdoc />
    /// <exception cref="ArgumentOutOfRangeException">The requested format is not one this module writes.</exception>
    public async Task<ExportedReport> ExportAsync(CandidateAnalyticsExportRequest request, CancellationToken cancellationToken)
    {
        var figures = await analytics.GetAsync(request.CandidateId, cancellationToken);
        var content = request.Format switch
        {
            ReportFormat.Csv => csvWriter.Write(figures),
            ReportFormat.Pdf => pdfWriter.Write(figures),
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.Format, "This module does not write that report format."),
        };

        await audit.RecordAsync(
            new AuditEntry(
                request.CandidateId,
                request.ActorRole,
                ExportAction,
                EntityType,
                request.CandidateId.ToString(),
                new Dictionary<string, string>
                {
                    ["report"] = ReportName,
                    ["format"] = ReportFormats.ExtensionOf(request.Format),
                    ["results"] = figures.ResultCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["sections"] = figures.Sections.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                },
                request.CorrelationId),
            cancellationToken);

        return new ExportedReport(
            ReportFileName.ForCandidateAnalytics(clock.UtcNow, request.Format),
            ReportFormats.ContentTypeOf(request.Format),
            content,
            figures.Trend.Count);
    }
}
