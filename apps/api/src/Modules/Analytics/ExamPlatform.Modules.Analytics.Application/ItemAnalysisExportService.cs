using ExamPlatform.Modules.Admin.Contracts;
using ExamPlatform.Modules.Analytics.Application.Ports;
using ExamPlatform.Modules.Analytics.Contracts;
using ExamPlatform.SharedKernel.Application;

namespace ExamPlatform.Modules.Analytics.Application;

/// <summary>
/// Exports an exam's item analysis for staff (FR-38): the same figures the page shows, written as a file and recorded in the audit log, so
/// it is known who took the figures out and when.
/// </summary>
/// <remarks>
/// The export names no candidate and carries no personal data beyond what the item analysis page already shows, which is nothing about any
/// one person. The audit entry is written before the file is returned: a file that was handed out but never recorded would be an export
/// nobody can account for (FR-40).
/// </remarks>
public sealed class ItemAnalysisExportService(
    IExamItemAnalysis analysis,
    IItemAnalysisCsvWriter writer,
    IAuditLogger audit,
    Clock clock) : IItemAnalysisExport
{
    /// <summary>The audit action recorded for every export of an item analysis.</summary>
    public const string ExportAction = "Analytics.ReportExported";

    /// <summary>The report's name in the audit trail.</summary>
    public const string ReportName = "item-analysis";

    /// <inheritdoc />
    public async Task<ExportedReport> ExportAsync(ItemAnalysisExportRequest request, CancellationToken cancellationToken)
    {
        // Reading the analysis first means an unknown exam fails here, before anything is recorded as exported.
        var result = await analysis.GetAsync(request.ExamId, cancellationToken);
        var content = writer.Write(result);

        await audit.RecordAsync(
            new AuditEntry(
                request.ActorUserId,
                request.ActorRole,
                ExportAction,
                "Exam",
                request.ExamId.ToString(),
                new Dictionary<string, string>
                {
                    ["report"] = ReportName,
                    ["format"] = "csv",
                    ["rows"] = result.Questions.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["candidates"] = result.CandidateCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                },
                request.CorrelationId),
            cancellationToken);

        return new ExportedReport(
            ReportFileName.ForItemAnalysis(result.ExamName, clock.UtcNow),
            "text/csv",
            content,
            result.Questions.Count);
    }
}
