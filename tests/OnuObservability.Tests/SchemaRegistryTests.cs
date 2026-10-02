using OnuObservability.Model;
using OnuObservability.Schema;

namespace OnuObservability.Tests;

public sealed class SchemaRegistryTests
{
    [Fact]
    public void IdenticalRegistrationsAreIdempotentAndOrdered()
    {
        var first = new OperationDescriptor(
            "sample.zeta",
            [TelemetryFieldDescriptor.Flag("enabled")]);
        var identical = new OperationDescriptor(
            "sample.zeta",
            [TelemetryFieldDescriptor.Flag("enabled")]);
        var builder = new TelemetrySchemaBuilder("sample", new(1, 0));

        var snapshot = builder
            .AddOperation(first)
            .AddOperation(identical)
            .AddOperation(new("sample.alpha"))
            .AddOutcome(CommonOutcomes.Success)
            .AddOutcome(CommonOutcomes.Success)
            .Build();

        Assert.Equal(["sample.alpha", "sample.zeta"], snapshot.Operations.Select(value => value.Name));
        Assert.Single(snapshot.Outcomes);
        Assert.Throws<InvalidOperationException>(() => builder.AddOperation(new("sample.late")));
    }

    [Fact]
    public void ConflictingRegistrationFailsFast()
    {
        var builder = new TelemetrySchemaBuilder("sample", new(1, 0));
        builder.AddOperation(new(
            "sample.operation",
            [TelemetryFieldDescriptor.Flag("enabled")]));

        var error = Assert.Throws<InvalidOperationException>(() => builder.AddOperation(new(
            "sample.operation",
            [TelemetryFieldDescriptor.Count("count", 10)])));

        Assert.Contains("conflicting", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MultipleAdapterContributionsComposeDeterministically()
    {
        static void AddAdapter(TelemetrySchemaBuilder builder) => builder
            .AddOperation(new("adapter.request"))
            .AddEvent(new(42, "adapter.completed", "Adapter completed"))
            .AddOutcome(CommonOutcomes.Other);

        var builder = new TelemetrySchemaBuilder("sample", new(1, 0));
        AddAdapter(builder);
        AddAdapter(builder);

        var snapshot = builder.Build();

        Assert.Single(snapshot.Operations);
        Assert.Single(snapshot.Events);
        Assert.Single(snapshot.Outcomes);
    }
}
