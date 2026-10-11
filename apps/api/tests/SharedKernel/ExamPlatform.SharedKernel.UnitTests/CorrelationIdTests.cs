using ExamPlatform.SharedKernel.Infrastructure.Observability;
using Microsoft.AspNetCore.Http;

namespace ExamPlatform.SharedKernel.UnitTests;

/// <summary>The request correlation id (NFR-9): which ids are kept, and that one id reaches the header, the request and the log.</summary>
public class CorrelationIdTests
{
    [Theory]
    [InlineData("abc")]
    [InlineData("A-b_c.9")]
    [InlineData("3f2504e0-4f89-11d3-9a0c-0305e82c3301")]
    public void AnIdOfSafeCharacters_IsAcceptable(string id) =>
        Assert.True(CorrelationId.IsAcceptable(id));

    [Fact]
    public void AnIdOfExactlyTheMaximumLength_IsAcceptable() =>
        Assert.True(CorrelationId.IsAcceptable(new string('a', CorrelationId.MaxLength)));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a b")]
    [InlineData("a,b")]
    [InlineData("a\nb")]
    [InlineData("a/b")]
    [InlineData("<script>")]
    [InlineData("café")]
    public void AnIdWithAnUnsafeOrMissingValue_IsNotAcceptable(string? id) =>
        Assert.False(CorrelationId.IsAcceptable(id));

    [Fact]
    public void AnIdLongerThanTheMaximum_IsNotAcceptable() =>
        Assert.False(CorrelationId.IsAcceptable(new string('a', CorrelationId.MaxLength + 1)));

    [Fact]
    public void ANewId_IsThirtyTwoHexCharacters_AndDiffersEachTime()
    {
        var first = CorrelationId.NewId();
        var second = CorrelationId.NewId();

        Assert.Matches("^[0-9a-f]{32}$", first);
        Assert.NotEqual(first, second);
    }

    // The response header is written when the response starts, which a bare DefaultHttpContext never does; the header is
    // asserted by the integration tests against the real pipeline (ObservabilityFlowTests).
    [Fact]
    public async Task ACallersAcceptableId_IsKept_AsTheRequestId()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationId.HeaderName] = "caller-trace-1";
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask, new CapturingLogger<CorrelationIdMiddleware>());

        await middleware.InvokeAsync(context);

        Assert.Equal("caller-trace-1", context.TraceIdentifier);
    }

    [Fact]
    public async Task WithoutACallerId_ANewOneIsMade_AsTheRequestId()
    {
        var context = new DefaultHttpContext();
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask, new CapturingLogger<CorrelationIdMiddleware>());

        await middleware.InvokeAsync(context);

        Assert.Matches("^[0-9a-f]{32}$", context.TraceIdentifier);
    }

    [Fact]
    public async Task AnUnacceptableCallerId_IsReplaced_NotRejected()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationId.HeaderName] = new string('x', CorrelationId.MaxLength + 72);
        var nextCalled = false;
        var middleware = new CorrelationIdMiddleware(
            _ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            new CapturingLogger<CorrelationIdMiddleware>());

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled, "A tracing header must never stop the request.");
        Assert.NotEqual(new string('x', CorrelationId.MaxLength + 72), context.TraceIdentifier);
        Assert.Matches("^[0-9a-f]{32}$", context.TraceIdentifier);
    }

    [Fact]
    public async Task TheLogScope_NamesTheRequestId()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationId.HeaderName] = "scoped-1";
        var logger = new CapturingLogger<CorrelationIdMiddleware>();
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask, logger);

        await middleware.InvokeAsync(context);

        var scope = Assert.Single(logger.Scopes);
        var named = Assert.IsAssignableFrom<IDictionary<string, object>>(scope);
        Assert.Equal("scoped-1", named[CorrelationIdMiddleware.ScopeKey]);
    }

    [Fact]
    public void ResolveId_KeepsAnAcceptableId_AndMakesOneOtherwise()
    {
        Assert.Equal("kept-1", CorrelationIdMiddleware.ResolveId("kept-1"));
        Assert.Matches("^[0-9a-f]{32}$", CorrelationIdMiddleware.ResolveId(string.Empty));
    }
}
