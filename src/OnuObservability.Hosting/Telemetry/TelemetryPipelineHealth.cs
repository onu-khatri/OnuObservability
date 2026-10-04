using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using OnuObservability.Hosting.Composition;
using OnuObservability.Operations;

namespace OnuObservability.Hosting.Telemetry;

internal sealed class TelemetryPipelineHealth : IExportHealthObserver
{
    private readonly object _gate = new();
    private readonly OnuTelemetryInstruments _instruments;
    private readonly TimeProvider _timeProvider;
    private readonly TextWriter _diagnostics;
    private readonly TimeSpan _window;
    private readonly int _burst;
    private readonly Dictionary<DiagnosticKey, DiagnosticWindow> _windows = new();
    private long _diagnosticCount;

    public TelemetryPipelineHealth(
        OnuTelemetryInstruments instruments,
        TimeProvider timeProvider,
        TextWriter? diagnostics = null,
        TimeSpan? window = null,
        int burst = 1)
    {
        _instruments = instruments;
        _timeProvider = timeProvider;
        _diagnostics = diagnostics ?? Console.Error;
        _window = window ?? TimeSpan.FromMinutes(1);
        _burst = Math.Clamp(burst, 1, 10);
    }

    internal int KeyCount
    {
        get
        {
            lock (_gate)
            {
                return _windows.Count;
            }
        }
    }

    internal long DiagnosticCount => Interlocked.Read(ref _diagnosticCount);

    public void RecordDrop(
        TelemetrySignal signal,
        ExportFailureReason reason,
        long count)
    {
        count = Math.Clamp(count, 1, 1_024);
        var signalName = SignalName(signal);
        var reasonName = ReasonName(reason);
        _instruments.DroppedTelemetry.Add(
            count,
            new TagList
            {
                { "signal", signalName },
                { "reason", reasonName },
            });

        var now = _timeProvider.GetUtcNow();
        lock (_gate)
        {
            var key = new DiagnosticKey(signal, reason);
            if (!_windows.TryGetValue(key, out var state))
            {
                if (_windows.Count >= Enum.GetValues<TelemetrySignal>().Length *
                    Enum.GetValues<ExportFailureReason>().Length)
                {
                    return;
                }

                state = new(now);
                _windows.Add(key, state);
            }
            else if (now - state.StartedAt >= _window)
            {
                state.StartedAt = now;
                state.Emitted = 0;
            }

            if (state.Emitted >= _burst)
            {
                return;
            }

            state.Emitted++;
            _diagnostics.WriteLine(JsonSerializer.Serialize(new
            {
                Timestamp = now.UtcDateTime,
                EventId = 1090,
                LogLevel = "Warning",
                Category = OnuObservabilityDefaults.SelfDiagnosticsCategory,
                EventName = "onu.telemetry.dropped",
                Signal = signalName,
                Reason = reasonName,
                Count = count,
            }));
            Interlocked.Increment(ref _diagnosticCount);
        }
    }

    internal static string SignalName(TelemetrySignal signal) => signal switch
    {
        TelemetrySignal.Trace => "trace",
        TelemetrySignal.Metric => "metric",
        TelemetrySignal.Log => "log",
        _ => "other",
    };

    internal static string ReasonName(ExportFailureReason reason) => reason switch
    {
        ExportFailureReason.ExportFailure => "export_failure",
        ExportFailureReason.ExporterException => "exporter_exception",
        ExportFailureReason.QueueFull => "queue_full",
        ExportFailureReason.FlushFailure => "flush_failure",
        ExportFailureReason.ShutdownFailure => "shutdown_failure",
        _ => "other",
    };

    private readonly record struct DiagnosticKey(
        TelemetrySignal Signal,
        ExportFailureReason Reason);

    private sealed class DiagnosticWindow(DateTimeOffset startedAt)
    {
        public DateTimeOffset StartedAt { get; set; } = startedAt;
        public int Emitted { get; set; }
    }
}
