using OnuObservability.Operations;

namespace OnuObservability.Hosting.Telemetry;

internal sealed class ExportHealthObserverRegistry : IExportHealthObserver
{
    private readonly object _gate = new();
    private readonly List<IExportHealthObserver> _observers = [];

    public void Add(IExportHealthObserver observer)
    {
        ArgumentNullException.ThrowIfNull(observer);
        lock (_gate)
        {
            if (!_observers.Contains(observer))
            {
                _observers.Add(observer);
            }
        }
    }

    public void RecordDrop(
        TelemetrySignal signal,
        ExportFailureReason reason,
        long count)
    {
        IExportHealthObserver[] snapshot;
        lock (_gate)
        {
            snapshot = [.. _observers];
        }

        foreach (var observer in snapshot)
        {
            try
            {
                observer.RecordDrop(signal, reason, count);
            }
            catch (Exception)
            {
                // Health reporting is fail-open and cannot recurse into its exporters.
            }
        }
    }
}
