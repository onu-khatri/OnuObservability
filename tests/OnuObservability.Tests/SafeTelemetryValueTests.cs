using OnuObservability.Safety;
using OnuObservability.Schema;

namespace OnuObservability.Tests;

public sealed class SafeTelemetryValueTests
{
    [Fact]
    public void TextIsTruncatedWithoutRetainingDiscardedContent()
    {
        var field = TelemetryFieldDescriptor.BoundedText("detail", 4);

        var result = SafeTelemetryValueNormalizer.Normalize(field, TelemetryValue.FromText("abcdef"));

        Assert.True(result.IsAccepted);
        Assert.True(result.WasTruncated);
        Assert.Equal("abcd", result.Value.ToInvariantString());
        Assert.DoesNotContain("ef", result.Value.ToInvariantString(), StringComparison.Ordinal);
    }

    [Fact]
    public void CountsAreClampedWithEvidence()
    {
        var field = TelemetryFieldDescriptor.Count("processed", 100);

        var result = SafeTelemetryValueNormalizer.Normalize(field, TelemetryValue.FromCount(500));

        Assert.True(result.IsAccepted);
        Assert.True(result.WasClamped);
        Assert.Equal("100", result.Value.ToInvariantString());
    }

    [Fact]
    public void UnknownFiniteValueUsesRegisteredFallback()
    {
        var field = TelemetryFieldDescriptor.FiniteString(
            "kind",
            ["known", "other"],
            "other");

        var result = SafeTelemetryValueNormalizer.Normalize(field, TelemetryValue.FromText("caller-value"));

        Assert.True(result.IsAccepted);
        Assert.True(result.UsedFallback);
        Assert.Equal("other", result.Value.ToInvariantString());
    }

    [Theory]
    [InlineData("Authorization: Bearer synthetic-secret")]
    [InlineData("https://example.invalid/private?id=42")]
    [InlineData("C:\\Users\\person\\private.txt")]
    [InlineData("/home/person/private.txt")]
    [InlineData("cookie: session=synthetic-secret")]
    [InlineData("line1\nline2")]
    public void SensitiveTextIsRejectedBeforeCapture(string candidate)
    {
        var field = TelemetryFieldDescriptor.BoundedText("detail", 128);

        var result = SafeTelemetryValueNormalizer.Normalize(field, TelemetryValue.FromText(candidate));

        Assert.False(result.IsAccepted);
        Assert.Equal(string.Empty, result.Value.ToInvariantString());
    }

    [Fact]
    public void ValueKindMismatchIsRejected()
    {
        var field = TelemetryFieldDescriptor.Flag("enabled");

        Assert.False(SafeTelemetryValueNormalizer
            .Normalize(field, TelemetryValue.FromText("true"))
            .IsAccepted);
    }
}
