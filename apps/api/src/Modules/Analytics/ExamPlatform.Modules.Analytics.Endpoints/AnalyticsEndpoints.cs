using System.Security.Claims;
using ExamPlatform.Modules.Analytics.Contracts;
using ExamPlatform.SharedKernel.Application.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ExamPlatform.Modules.Analytics.Endpoints;

/// <summary>Maps the Analytics module's HTTP endpoints: the candidate's own performance (FR-36).</summary>
public static class AnalyticsEndpoints
{
    /// <summary>Maps <c>GET /v1/me/analytics</c>, which reads the signed-in candidate's own released results.</summary>
    /// <remarks>
    /// Self-service like the other <c>/v1/me</c> routes: it needs a signed-in caller and no permission, and the candidate is taken from the
    /// token, never from the request, so a caller can only ever see their own figures.
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
    }

    private static async Task<IResult> GetMyAnalytics(ClaimsPrincipal user, ICandidateAnalytics analytics, CancellationToken cancellationToken) =>
        Results.Ok(await analytics.GetAsync(user.GetUserId(), cancellationToken));
}
