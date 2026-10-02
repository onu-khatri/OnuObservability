namespace OnuObservability.Operations;

public enum TelemetrySignal
{
    Trace,
    Metric,
    Log,
}

public enum ExportFailureReason
{
    ExportFailure,
    ExporterException,
    QueueFull,
    FlushFailure,
    ShutdownFailure,
}

public interface IExportHealthObserver
{
    void RecordDrop(
        TelemetrySignal signal,
        ExportFailureReason reason,
        long count);
}
