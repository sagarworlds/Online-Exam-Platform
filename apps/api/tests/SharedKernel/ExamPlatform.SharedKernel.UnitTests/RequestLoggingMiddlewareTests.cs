using ExamPlatform.SharedKernel.Infrastructure.Observability;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Logging;

namespace ExamPlatform.SharedKernel.UnitTests;

/// <summary>The request line (NFR-9): one line per request with its outcome, and nothing a caller meant to keep private.</summary>
public class RequestLoggingMiddlewareTests
{
    /// <summary>A request that the given endpoint answers with the given status.</summary>
    private static DefaultHttpContext Request(string method, string path, string? template = null, string query = "")
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.Request.QueryString = new QueryString(query);
        if (template is not null)
        {
            context.SetEndpoint(new RouteEndpoint(
                _ => Task.CompletedTask,
                RoutePatternFactory.Parse(template),
                order: 0,
                metadata: new EndpointMetadataCollection(),
                displayName: template));
        }

        return context;
    }

    [Theory]
    [InlineData(200, "/v1/batches/{batchId}", LogLevel.Information)]
    [InlineData(404, RequestLoggingMiddleware.NoRouteLabel, LogLevel.Information)]
    [InlineData(400, "/v1/auth/login", LogLevel.Information)]
    [InlineData(401, "/v1/me/profile", LogLevel.Warning)]
    [InlineData(403, "/v1/admin/audit-logs", LogLevel.Warning)]
    [InlineData(429, "/v1/auth/login", LogLevel.Warning)]
    [InlineData(500, "/v1/batches/{batchId}", LogLevel.Error)]
    [InlineData(503, "/v1/health/ready", LogLevel.Error)]
    [InlineData(200, "/v1/health/live", LogLevel.Debug)]
    [InlineData(200, "/v1/health", LogLevel.Debug)]
    public void ALevel_FollowsTheOutcome(int status, string route, LogLevel expected) =>
        Assert.Equal(expected, RequestLoggingMiddleware.LevelFor(status, route));

    [Fact]
    public async Task OneLine_NamesTheMethodTheRouteTemplateTheStatusAndTheDuration()
    {
        var logger = new CapturingLogger<RequestLoggingMiddleware>();
        var middleware = new RequestLoggingMiddleware(
            context =>
            {
                context.Response.StatusCode = 201;
                return Task.CompletedTask;
            },
            logger);

        await middleware.InvokeAsync(Request("POST", "/v1/invites/tok-123", "/v1/invites/{inviteId}"));

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Matches(@"^HTTP POST /v1/invites/\{inviteId\} responded 201 in [\d.]+ ms$", entry.Message);
    }

    [Fact]
    public async Task ThePathValuesAndTheQueryString_AreNeverLogged()
    {
        var logger = new CapturingLogger<RequestLoggingMiddleware>();
        var middleware = new RequestLoggingMiddleware(_ => Task.CompletedTask, logger);

        // The WhatsApp webhook's verify token travels in the query string, and an invite code in the path.
        await middleware.InvokeAsync(Request(
            "GET", "/v1/invites/inv-code-SECRET", "/v1/invites/{inviteId}", "?hub.verify_token=s3cr3t-token"));

        var message = Assert.Single(logger.Entries).Message;
        Assert.DoesNotContain("inv-code-SECRET", message, StringComparison.Ordinal);
        Assert.DoesNotContain("s3cr3t", message, StringComparison.Ordinal);
        Assert.DoesNotContain("verify_token", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnUnmatchedRequest_IsLabelledNoRoute_NotItsRawPath()
    {
        var logger = new CapturingLogger<RequestLoggingMiddleware>();
        var middleware = new RequestLoggingMiddleware(
            context =>
            {
                context.Response.StatusCode = 404;
                return Task.CompletedTask;
            },
            logger);

        await middleware.InvokeAsync(Request("GET", "/v1/someone-typed-their-address"));

        var message = Assert.Single(logger.Entries).Message;
        Assert.Contains(RequestLoggingMiddleware.NoRouteLabel, message, StringComparison.Ordinal);
        Assert.DoesNotContain("someone-typed-their-address", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnExceptionThatEscapesThePipeline_IsLoggedAsA500_AndRethrown()
    {
        var logger = new CapturingLogger<RequestLoggingMiddleware>();
        var middleware = new RequestLoggingMiddleware(_ => throw new InvalidOperationException("boom"), logger);

        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(Request("GET", "/v1/x", "/v1/x")));

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Error, entry.Level);
        Assert.Contains("responded 500", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RouteOf_IsTheTemplate_OrTheNoRouteLabel()
    {
        Assert.Equal("/v1/batches/{batchId}", RequestLoggingMiddleware.RouteOf(Request("GET", "/v1/batches/9", "/v1/batches/{batchId}")));
        Assert.Equal(RequestLoggingMiddleware.NoRouteLabel, RequestLoggingMiddleware.RouteOf(Request("GET", "/v1/batches/9")));
    }
}
