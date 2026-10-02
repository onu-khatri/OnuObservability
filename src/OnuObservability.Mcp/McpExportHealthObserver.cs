using System.Text.Json;
using OnuObservability.Operations;

namespace OnuObservability.Mcp;

internal sealed class McpExportHealthObserver(
    McpTelemetryInstruments telemetry,
    TimeProvider timeProvider,
    TextWriter? diagnostics = null) : IExportHealthObserver
{
    private readonly object _gate = new();
    private readonly TextWriter _diagnostics = diagnostics ?? Console.Error;
    private readonly HashSet<(TelemetrySignal Signal, ExportFailureReason Reason)> _emitted = [];

    public void RecordDrop(
        TelemetrySignal signal,
        ExportFailureReason reason,
        long count)
    {
        var signalName = SignalName(signal);
        var reasonName = ReasonName(reason);
        count = Math.Clamp(count, 1, 1_024);
        telemetry.TelemetryDropped.Add(
            count,
            new("signal", signalName),
            new("reason", reasonName));

        lock (_gate)
        {
            if (!_emitted.Add((signal, reason)))
            {
                return;
            }

            _diagnostics.WriteLine(JsonSerializer.Serialize(new
            {
                Timestamp = timeProvider.GetUtcNow().UtcDateTime,
                EventId = 1090,
                LogLevel = "Warning",
                Category = McpTelemetrySchema.SelfDiagnosticsCategory,
                EventName = "mcp.telemetry.dropped",
                Signal = signalName,
                Reason = reasonName,
                Count = count,
            }));
        }
    }

    private static string SignalName(TelemetrySignal signal) => signal switch
    {
        TelemetrySignal.Trace => "trace",
        TelemetrySignal.Metric => "metric",
        TelemetrySignal.Log => "log",
        _ => McpTelemetrySchema.Other,
    };

    private static string ReasonName(ExportFailureReason reason) => reason switch
    {
        ExportFailureReason.ExportFailure => "export_failure",
        ExportFailureReason.ExporterException => "exporter_exception",
        ExportFailureReason.QueueFull => "queue_full",
        ExportFailureReason.FlushFailure => "flush_failure",
        ExportFailureReason.ShutdownFailure => "shutdown_failure",
        _ => McpTelemetrySchema.Other,
    };
}
