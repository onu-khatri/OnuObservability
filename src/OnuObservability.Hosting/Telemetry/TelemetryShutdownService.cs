using Microsoft.Extensions.Hosting;
using OnuObservability.Hosting.Configuration;
using OnuObservability.Operations;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace OnuObservability.Hosting.Telemetry;

internal interface IPackageTelemetryFlusher
{
    TelemetrySignal Signal { get; }

    bool Flush(int timeoutMilliseconds);
}

internal sealed class TracerProviderFlusher(TracerProvider provider) : IPackageTelemetryFlusher
{
    public TelemetrySignal Signal => TelemetrySignal.Trace;

    public bool Flush(int timeoutMilliseconds) => provider.ForceFlush(timeoutMilliseconds);
}

internal sealed class MeterProviderFlusher(MeterProvider provider) : IPackageTelemetryFlusher
{
    public TelemetrySignal Signal => TelemetrySignal.Metric;

    public bool Flush(int timeoutMilliseconds) => provider.ForceFlush(timeoutMilliseconds);
}

internal sealed class LogProcessorFlusher : IPackageTelemetryFlusher
{
    private BaseProcessor<LogRecord>? _processor;

    public TelemetrySignal Signal => TelemetrySignal.Log;

    public void SetProcessor(BaseProcessor<LogRecord> processor) =>
        Volatile.Write(ref _processor, processor);

    public bool Flush(int timeoutMilliseconds) =>
        Volatile.Read(ref _processor)?.ForceFlush(timeoutMilliseconds) ?? true;
}

internal sealed class TelemetryShutdownService(
    IEnumerable<IPackageTelemetryFlusher> flushers,
    EffectiveObservabilitySettings settings,
    IExportHealthObserver health) : IHostedService
{
    private readonly IPackageTelemetryFlusher[] _flushers = flushers.ToArray();

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        var timeout = settings.ShutdownFlushTimeout;
        var flush = FlushWithinAsync(timeout);
        try
        {
            var results = await flush.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            foreach (var result in results)
            {
                if (!result.Succeeded)
                {
                    health.RecordDrop(result.Signal, ExportFailureReason.FlushFailure, 1);
                }
            }
        }
        catch (TimeoutException)
        {
            foreach (var flusher in _flushers)
            {
                health.RecordDrop(flusher.Signal, ExportFailureReason.ShutdownFailure, 1);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            foreach (var flusher in _flushers)
            {
                health.RecordDrop(flusher.Signal, ExportFailureReason.ShutdownFailure, 1);
            }
        }
    }

    private Task<FlushResult[]> FlushWithinAsync(TimeSpan timeout)
    {
        var totalMilliseconds = (int)timeout.TotalMilliseconds;
        return Task.WhenAll(_flushers.Select(flusher => Task.Run(() =>
        {
            try
            {
                return new FlushResult(flusher.Signal, flusher.Flush(totalMilliseconds));
            }
            catch (Exception)
            {
                return new FlushResult(flusher.Signal, false);
            }
        }, CancellationToken.None)));
    }

    private readonly record struct FlushResult(TelemetrySignal Signal, bool Succeeded);
}
