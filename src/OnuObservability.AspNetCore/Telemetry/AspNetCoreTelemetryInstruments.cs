using System.Diagnostics.Metrics;
using OnuObservability.AspNetCore.Schema;

namespace OnuObservability.AspNetCore.Telemetry;

internal sealed class AspNetCoreTelemetryInstruments : IDisposable
{
    private readonly Meter _meter = new(AspNetCoreTelemetrySchema.MeterName);

    public AspNetCoreTelemetryInstruments()
    {
        RequestDuration = _meter.CreateHistogram<double>(
            AspNetCoreTelemetrySchema.RequestDurationInstrument,
            unit: "s");
        RequestCount = _meter.CreateCounter<long>(
            AspNetCoreTelemetrySchema.RequestCountInstrument,
            unit: "{request}");
    }

    public Histogram<double> RequestDuration { get; }

    public Counter<long> RequestCount { get; }

    public void Dispose() => _meter.Dispose();
}
