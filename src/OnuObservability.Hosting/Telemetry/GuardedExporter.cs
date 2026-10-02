using OnuObservability.Operations;
using OpenTelemetry;

namespace OnuObservability.Hosting.Telemetry;

internal sealed class GuardedExporter<T>(
    BaseExporter<T> inner,
    IExportHealthObserver health,
    TelemetrySignal signal) : BaseExporter<T>
    where T : class
{
    private int _parentAssigned;

    public override ExportResult Export(in Batch<T> batch)
    {
        try
        {
            EnsureInnerParentProvider();
            var result = inner.Export(batch);
            if (result != ExportResult.Success)
            {
                health.RecordDrop(signal, ExportFailureReason.ExportFailure, batch.Count);
            }

            return result;
        }
        catch (Exception)
        {
            health.RecordDrop(signal, ExportFailureReason.ExporterException, batch.Count);
            return ExportResult.Failure;
        }
    }

    protected override bool OnForceFlush(int timeoutMilliseconds)
    {
        try
        {
            EnsureInnerParentProvider();
            var result = inner.ForceFlush(timeoutMilliseconds);
            if (!result)
            {
                health.RecordDrop(signal, ExportFailureReason.FlushFailure, 1);
            }

            return result;
        }
        catch (Exception)
        {
            health.RecordDrop(signal, ExportFailureReason.FlushFailure, 1);
            return false;
        }
    }

    protected override bool OnShutdown(int timeoutMilliseconds)
    {
        try
        {
            EnsureInnerParentProvider();
            var result = inner.Shutdown(timeoutMilliseconds);
            if (!result)
            {
                health.RecordDrop(signal, ExportFailureReason.ShutdownFailure, 1);
            }

            return result;
        }
        catch (Exception)
        {
            health.RecordDrop(signal, ExportFailureReason.ShutdownFailure, 1);
            return false;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
        }

        base.Dispose(disposing);
    }

    private void EnsureInnerParentProvider()
    {
        if (Volatile.Read(ref _parentAssigned) != 0 || ParentProvider is not { } provider)
        {
            return;
        }

        ExporterParentProvider.Assign(inner, provider);
        Volatile.Write(ref _parentAssigned, 1);
    }
}
