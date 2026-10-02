using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace OnuObservability.Hosting.Telemetry;

internal static class OnuTelemetrySources
{
    public const string ActivitySourceName = OnuObservabilityDefaults.ActivitySourceName;
    public const string MeterName = OnuObservabilityDefaults.MeterName;

    public static readonly ActivitySource Activities = new(ActivitySourceName);
    public static readonly Meter Meter = new(MeterName);
}

internal sealed class OnuTelemetryInstruments
{
    public Counter<long> Operations { get; } = OnuTelemetrySources.Meter.CreateCounter<long>(
        "onu.operation.count",
        "{operation}",
        "Number of bounded operations completed.");

    public Histogram<double> OperationDuration { get; } = OnuTelemetrySources.Meter.CreateHistogram<double>(
        "onu.operation.duration",
        "s",
        "Duration of bounded operations.");

    public Counter<long> DroppedTelemetry { get; } = OnuTelemetrySources.Meter.CreateCounter<long>(
        "onu.telemetry.dropped",
        "{item}",
        "Number of telemetry items dropped by bounded package policy.");
}
