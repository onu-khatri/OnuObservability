using System.Diagnostics;
using System.Threading.Channels;
using OnuObservability.Operations;
using OpenTelemetry;

namespace OnuObservability.Hosting.Telemetry;

internal sealed class BoundedBatchExportProcessor<T> : BaseProcessor<T>
    where T : class
{
    private readonly BaseExporter<T> _exporter;
    private readonly IExportHealthObserver _health;
    private readonly TelemetrySignal _signal;
    private readonly Channel<T> _queue;
    private readonly TimeSpan _scheduledDelay;
    private readonly int _exporterTimeoutMilliseconds;
    private readonly int _maximumBatchSize;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly SemaphoreSlim _flushPulse = new(0, 1);
    private readonly Task _worker;
    private int _activeExport;
    private int _parentAssigned;
    private int _pending;
    private int _stopping;

    public BoundedBatchExportProcessor(
        BaseExporter<T> exporter,
        IExportHealthObserver health,
        TelemetrySignal signal,
        int maximumQueueSize,
        int scheduledDelayMilliseconds,
        int exporterTimeoutMilliseconds,
        int maximumBatchSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumQueueSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scheduledDelayMilliseconds);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(exporterTimeoutMilliseconds);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumBatchSize);
        if (maximumBatchSize > maximumQueueSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumBatchSize),
                "The batch size cannot exceed the queue size.");
        }

        _exporter = exporter;
        _health = health;
        _signal = signal;
        _scheduledDelay = TimeSpan.FromMilliseconds(scheduledDelayMilliseconds);
        _exporterTimeoutMilliseconds = exporterTimeoutMilliseconds;
        _maximumBatchSize = maximumBatchSize;
        _queue = Channel.CreateBounded<T>(new BoundedChannelOptions(maximumQueueSize)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
        _worker = Task.Run(ExportLoopAsync);
    }

    public override void OnEnd(T data)
    {
        Interlocked.Increment(ref _pending);
        if (Volatile.Read(ref _stopping) != 0 || !_queue.Writer.TryWrite(data))
        {
            Interlocked.Decrement(ref _pending);
            _health.RecordDrop(_signal, ExportFailureReason.QueueFull, 1);
            return;
        }
    }

    protected override bool OnForceFlush(int timeoutMilliseconds)
    {
        EnsureExporterParent();
        PulseFlush();
        var started = Environment.TickCount64;
        if (!WaitForIdle(timeoutMilliseconds))
        {
            _health.RecordDrop(_signal, ExportFailureReason.FlushFailure, 1);
            return false;
        }

        var remaining = Remaining(started, timeoutMilliseconds);
        return _exporter.ForceFlush(remaining);
    }

    protected override bool OnShutdown(int timeoutMilliseconds)
    {
        if (Interlocked.Exchange(ref _stopping, 1) != 0)
        {
            return true;
        }

        EnsureExporterParent();
        var started = Environment.TickCount64;
        _queue.Writer.TryComplete();
        PulseFlush();
        if (!_worker.Wait(Remaining(started, timeoutMilliseconds)))
        {
            return false;
        }

        return _exporter.Shutdown(Remaining(started, timeoutMilliseconds));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _queue.Writer.TryComplete();
            _shutdown.Cancel();
            PulseFlush();
            _exporter.Dispose();
            _shutdown.Dispose();
            _flushPulse.Dispose();
        }

        base.Dispose(disposing);
    }

    private async Task ExportLoopAsync()
    {
        var batch = new T[_maximumBatchSize];
        try
        {
            while (await _queue.Reader.WaitToReadAsync(_shutdown.Token).ConfigureAwait(false))
            {
                var count = Drain(batch);
                if (count == 0)
                {
                    continue;
                }

                if (count < _maximumBatchSize && Volatile.Read(ref _stopping) == 0)
                {
                    using var delayCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                        _shutdown.Token);
                    var delay = Task.Delay(_scheduledDelay, delayCancellation.Token);
                    var pulse = _flushPulse.WaitAsync(_shutdown.Token);
                    var completed = await Task.WhenAny(delay, pulse).ConfigureAwait(false);
                    delayCancellation.Cancel();
                    if (completed == delay)
                    {
                        await delay.ConfigureAwait(false);
                    }

                    count += Drain(batch.AsSpan(count));
                }

                EnsureExporterParent();
                Volatile.Write(ref _activeExport, 1);
                try
                {
                    _ = _exporter.Export(new Batch<T>(batch, count));
                }
                catch (Exception)
                {
                    _health.RecordDrop(_signal, ExportFailureReason.ExporterException, count);
                }
                finally
                {
                    Array.Clear(batch, 0, count);
                    Volatile.Write(ref _activeExport, 0);
                }
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
    }

    private int Drain(Span<T> destination)
    {
        var count = 0;
        while (count < destination.Length && _queue.Reader.TryRead(out var item))
        {
            destination[count++] = item;
            Interlocked.Decrement(ref _pending);
        }

        return count;
    }

    private bool WaitForIdle(int timeoutMilliseconds)
    {
        var stopwatch = Stopwatch.StartNew();
        while (Volatile.Read(ref _pending) != 0 || Volatile.Read(ref _activeExport) != 0)
        {
            if (stopwatch.ElapsedMilliseconds >= timeoutMilliseconds)
            {
                return false;
            }

            Thread.Sleep(1);
        }

        return true;
    }

    private void PulseFlush()
    {
        if (_flushPulse.CurrentCount == 0)
        {
            _flushPulse.Release();
        }
    }

    private void EnsureExporterParent()
    {
        if (Volatile.Read(ref _parentAssigned) != 0 || ParentProvider is not { } provider)
        {
            return;
        }

        ExporterParentProvider.Assign(_exporter, provider);
        Volatile.Write(ref _parentAssigned, 1);
    }

    private static int Remaining(long started, int timeoutMilliseconds) =>
        Math.Max(0, timeoutMilliseconds - (int)(Environment.TickCount64 - started));

}
