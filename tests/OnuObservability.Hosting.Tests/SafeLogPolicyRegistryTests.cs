using OnuObservability.Hosting.Logging;

namespace OnuObservability.Hosting.Tests;

public sealed class SafeLogPolicyRegistryTests
{
    [Fact]
    public void EquivalentPoliciesAreIdempotentAcrossImmutableInstances()
    {
        var registry = new SafeLogPolicyRegistry();

        registry.Add(Policy("FieldB", "FieldA"));
        registry.Add(Policy("FieldA", "FieldB"));

        Assert.True(registry.TryGetEvent("sample.category", 42, out var policy));
        Assert.Equal(
            ["FieldA", "FieldB"],
            policy!.ApprovedFields.Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void ConflictingPoliciesFailClosed()
    {
        var registry = new SafeLogPolicyRegistry();
        registry.Add(Policy("FieldA"));

        Assert.Throws<InvalidOperationException>(() => registry.Add(Policy("FieldB")));
    }

    private static SafeLogPolicy Policy(params string[] fields) => new(
        "sample.category",
        [new SafeLogEventPolicy(42, fields)]);
}
