using System.Security.Claims;
using ExamPlatform.Modules.Analytics.Contracts;
using ExamPlatform.SharedKernel.Application;
using ExamPlatform.SharedKernel.Application.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ExamPlatform.Modules.Analytics.Endpoints;

/// <summary>The authorization policies of the Analytics routes. Resolved from the caller's token by Identity, so no reference to Identity is needed (ADR 0001).</summary>
internal static class AnalyticsPermissions
{
    /// <summary>
    /// Read an exam's item analysis: <c>exam.manage</c>, the permission that runs an exam. Staff who see an exam's attempts may see how its
    /// questions performed, and no one else; the figures name no candidate, but they describe a cohort of real people.
    /// </summary>
    public const string ReadItems = "permission:exam.manage";
}

/// <summary>
/// Maps the Analytics module's HTTP endpoints: the candidate's own performance and its exports (FR-36, FR-38), and the staff item analysis and its
/// exports (FR-37, FR-38).
/// </summary>
public static class AnalyticsEndpoints
{
    /// <summary>
    /// Maps <c>GET /v1/me/analytics</c>, its CSV and PDF exports, <c>GET /v1/exams/{examId}/analytics/items</c> and its CSV and PDF exports.
    /// </summary>
    /// <remarks>
    /// The candidate route is self-service: it needs a signed-in caller and no permission, and the candidate comes from the token. The item analysis
    /// needs <see cref="AnalyticsPermissions.ReadItems"/>, so a candidate who asks for an exam's figures is refused with 403.
    /// </remarks>
    /// <param name="endpoints">The endpoint route builder to map onto.</param>
    public static void MapAnalyticsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/v1/me/analytics", GetMyAnalytics)
            .RequireAuthorization()
            .WithTags("Analytics")
            .Produces<CandidateAnalyticsDto>()
            .Produces(StatusCodes.Status401Unauthorized)
            .WithName("GetMyAnalytics")
            .WithDescription("The signed-in candidate's score trend and results by section, from the results that have been released to them (FR-36)");

        endpoints.MapGet("/v1/exams/{examId:guid}/analytics/items", GetItemAnalysis)
            .RequireAuthorization(AnalyticsPermissions.ReadItems)
            .WithTags("Analytics")
            .Produces<ExamItemAnalysisDto>()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .WithName("GetExamItemAnalysis")
            .WithDescription(
                "How each question of an exam performed among the released results: its difficulty and discrimination index, shown only once enough candidates had it, "
                + "and the candidates' mean, median and score distribution (FR-37)");

        endpoints.MapPost("/v1/exams/{examId:guid}/analytics/items/exports", ExportItemAnalysis)
            .RequireAuthorization(AnalyticsPermissions.ReadItems)
            .WithTags("Analytics")
            .Produces(StatusCodes.Status200OK, contentType: "text/csv")
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .WithName("ExportItemAnalysis")
            .WithDescription("Download an exam's item analysis as CSV. The export is recorded in the audit log before the file is sent (FR-38)");

        endpoints.MapPost("/v1/exams/{examId:guid}/analytics/items/exports/pdf", ExportItemAnalysisAsPdf)
            .RequireAuthorization(AnalyticsPermissions.ReadItems)
            .WithTags("Analytics")
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound)
            .WithName("ExportItemAnalysisPdf")
            .WithDescription("Download an exam's item analysis as a PDF report. The export is recorded in the audit log before the file is sent (FR-38)");

        endpoints.MapPost("/v1/me/analytics/exports/csv", ExportMyAnalyticsAsCsv)
            .RequireAuthorization()
            .WithTags("Analytics")
            .Produces(StatusCodes.Status200OK, contentType: "text/csv")
            .Produces(StatusCodes.Status401Unauthorized)
            .WithName("ExportMyAnalyticsCsv")
            .WithDescription("Download the signed-in candidate's own analytics as CSV, from their released results. The export is recorded in the audit log (FR-38)");

        endpoints.MapPost("/v1/me/analytics/exports/pdf", ExportMyAnalyticsAsPdf)
            .RequireAuthorization()
            .WithTags("Analytics")
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
            .Produces(StatusCodes.Status401Unauthorized)
            .WithName("ExportMyAnalyticsPdf")
            .WithDescription("Download the signed-in candidate's own analytics as a PDF report, from their released results. The export is recorded in the audit log (FR-38)");
    }

    private static async Task<IResult> GetMyAnalytics(ClaimsPrincipal user, ICandidateAnalytics analytics, CancellationToken cancellationToken) =>
        Results.Ok(await analytics.GetAsync(user.GetUserId(), cancellationToken));

    private static async Task<IResult> GetItemAnalysis(Guid examId, IExamItemAnalysis analysis, CancellationToken cancellationToken) =>
        Results.Ok(await analysis.GetAsync(examId, cancellationToken));

    private static Task<IResult> ExportItemAnalysis(
        Guid examId, ClaimsPrincipal user, IRequestContext requestContext, IItemAnalysisExport export, CancellationToken cancellationToken) =>
        ExportItemAnalysisAs(ReportFormat.Csv, examId, user, requestContext, export, cancellationToken);

    private static Task<IResult> ExportItemAnalysisAsPdf(
        Guid examId, ClaimsPrincipal user, IRequestContext requestContext, IItemAnalysisExport export, CancellationToken cancellationToken) =>
        ExportItemAnalysisAs(ReportFormat.Pdf, examId, user, requestContext, export, cancellationToken);

    private static async Task<IResult> ExportItemAnalysisAs(
        ReportFormat format, Guid examId, ClaimsPrincipal user, IRequestContext requestContext, IItemAnalysisExport export, CancellationToken cancellationToken)
    {
        // The actor comes from the token, so the audit entry names the person who asked, whatever the request says.
        var report = await export.ExportAsync(
            new ItemAnalysisExportRequest(examId, user.GetUserId(), user.GetPrimaryRole(), requestContext.CorrelationId, format), cancellationToken);
        return Results.File(report.Content, report.ContentType, report.FileName);
    }

    private static Task<IResult> ExportMyAnalyticsAsCsv(
        ClaimsPrincipal user, IRequestContext requestContext, ICandidateAnalyticsExport export, CancellationToken cancellationToken) =>
        ExportMyAnalyticsAs(ReportFormat.Csv, user, requestContext, export, cancellationToken);

    private static Task<IResult> ExportMyAnalyticsAsPdf(
        ClaimsPrincipal user, IRequestContext requestContext, ICandidateAnalyticsExport export, CancellationToken cancellationToken) =>
        ExportMyAnalyticsAs(ReportFormat.Pdf, user, requestContext, export, cancellationToken);

    private static async Task<IResult> ExportMyAnalyticsAs(
        ReportFormat format, ClaimsPrincipal user, IRequestContext requestContext, ICandidateAnalyticsExport export, CancellationToken cancellationToken)
    {
        // The candidate is the signed-in user, so no request can name someone else's figures to export.
        var report = await export.ExportAsync(
            new CandidateAnalyticsExportRequest(user.GetUserId(), user.GetPrimaryRole(), requestContext.CorrelationId, format), cancellationToken);
        return Results.File(report.Content, report.ContentType, report.FileName);
    }
}
