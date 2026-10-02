using OnuObservability.Schema;

namespace OnuObservability.Tests;

public sealed class CoreTelemetrySchemaTests
{
    [Fact]
    public void CoreSchemaIsFiniteAndVersioned()
    {
        var schema = CoreTelemetrySchema.Create();

        Assert.Equal(CoreTelemetrySchema.Name, schema.Name);
        Assert.Equal(CoreTelemetrySchema.Version, schema.Version.ToString());
        Assert.Equal(2, schema.Operations.Length);
        Assert.Equal(3, schema.Events.Length);
        Assert.All(schema.Operations.SelectMany(operation => operation.Fields), field =>
        {
            Assert.True(
                field.Kind is not TelemetryValueKind.BoundedText || field.MaximumLength is > 0);
            Assert.True(
                field.Kind is not TelemetryValueKind.FiniteString || field.FallbackValue is not null);
        });
    }
}
