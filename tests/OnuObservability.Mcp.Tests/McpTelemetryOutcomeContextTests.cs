using OnuObservability.Model;

namespace OnuObservability.Mcp.Tests;

public sealed class McpTelemetryOutcomeContextTests
{
    [Fact]
    public void FirstClassificationWinsAndScopeClearsOnDispose()
    {
        var context = new McpTelemetryOutcomeContext();
        var first = new McpTelemetryClassification(
            CommonOutcomes.ValidationError,
            new ErrorCode("invalid_request"));
        var second = new McpTelemetryClassification(CommonOutcomes.InternalError);

        using (var scope = context.BeginScope())
        {
            context.Record(first);
            context.Record(second);
            Assert.Same(first, scope.Classification);
        }

        using var next = context.BeginScope();
        Assert.Null(next.Classification);
    }

    [Fact]
    public void NestedScopesRestoreOuterClassification()
    {
        var context = new McpTelemetryOutcomeContext();
        var outerClassification = new McpTelemetryClassification(CommonOutcomes.ValidationError);
        var innerClassification = new McpTelemetryClassification(CommonOutcomes.DependencyError);
        using var outer = context.BeginScope();
        context.Record(outerClassification);

        using (var inner = context.BeginScope())
        {
            context.Record(innerClassification);
            Assert.Same(innerClassification, inner.Classification);
        }

        Assert.Same(outerClassification, outer.Classification);
    }

    [Fact]
    public async Task ConcurrentFlowsRemainIsolated()
    {
        var context = new McpTelemetryOutcomeContext();
        var outcomes = await Task.WhenAll(
            Enumerable.Range(0, 50).Select(async index =>
            {
                using var scope = context.BeginScope();
                var classification = new McpTelemetryClassification(
                    index % 2 == 0 ? CommonOutcomes.ValidationError : CommonOutcomes.DependencyError,
                    new ErrorCode(index % 2 == 0 ? "invalid_request" : "dependency_error"));
                await Task.Yield();
                context.Record(classification);
                await Task.Yield();
                return scope.Classification;
            }));

        Assert.Equal(50, outcomes.Length);
        Assert.All(outcomes, Assert.NotNull);
    }

    [Fact]
    public void ClassifiedExceptionRecordsTypedClassificationWithoutMessageParsing()
    {
        var context = new McpTelemetryOutcomeContext();
        using var scope = context.BeginScope();
        var classification = new McpTelemetryClassification(
            CommonOutcomes.AuthorizationDenied,
            new ErrorCode("policy_denied"),
            DenialReason: McpDenialReason.PolicyDenied);

        var exception = McpClassifiedException.Create(
            "safe client response",
            classification,
            context);

        Assert.Same(classification, exception.Classification);
        Assert.Same(classification, scope.Classification);
    }
}
