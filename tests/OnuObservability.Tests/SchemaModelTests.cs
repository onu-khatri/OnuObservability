using OnuObservability.Model;
using OnuObservability.Schema;

namespace OnuObservability.Tests;

public sealed class SchemaModelTests
{
    [Theory]
    [InlineData("")]
    [InlineData("UpperCase")]
    [InlineData("1invalid")]
    [InlineData("invalid/")]
    public void RegisteredValuesRejectInvalidNames(string value)
    {
        Assert.ThrowsAny<ArgumentException>(() => new Outcome(value));
        Assert.ThrowsAny<ArgumentException>(() => new ErrorCode(value));
        Assert.ThrowsAny<ArgumentException>(() => new ReasonCode(value));
    }

    [Fact]
    public void FiniteRegistryRejectsDuplicatesAndMissingFallback()
    {
        Assert.Throws<ArgumentException>(() => new FiniteValueRegistry<Outcome>(
            [new("success"), new("success")],
            new("success"),
            value => value.Value));
        Assert.Throws<ArgumentException>(() => new FiniteValueRegistry<Outcome>(
            [new("success")],
            new("other"),
            value => value.Value));
    }

    [Fact]
    public void FieldDescriptorsRejectUnboundedDefinitions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TelemetryFieldDescriptor.BoundedText("detail", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TelemetryFieldDescriptor.BoundedText("detail", 257));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            TelemetryFieldDescriptor.Number("ratio", double.NegativeInfinity, 1));
        Assert.Throws<ArgumentException>(() =>
            TelemetryFieldDescriptor.FiniteString("kind", ["known"], "other"));
    }

    [Fact]
    public void DescriptorsRejectDuplicateFields()
    {
        var field = TelemetryFieldDescriptor.Flag("enabled");
        Assert.Throws<ArgumentException>(() => new OperationDescriptor(
            "sample.operation",
            [field, field]));
    }

    [Theory]
    [InlineData("1.0", 1, 0)]
    [InlineData("10.24", 10, 24)]
    public void SchemaVersionUsesMajorMinorForm(string value, int major, int minor)
    {
        var version = SchemaVersion.Parse(value);

        Assert.Equal(major, version.Major);
        Assert.Equal(minor, version.Minor);
        Assert.Equal(value, version.ToString());
    }
}
