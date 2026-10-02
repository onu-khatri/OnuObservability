using OnuObservability.Operations;

namespace OnuObservability.Hosting.Telemetry;

internal sealed class TelemetryHttpMessageHandler(
    IExportHealthObserver health,
    TelemetrySignal signal,
    HttpMessageHandler innerHandler) : DelegatingHandler(innerHandler)
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            health.RecordDrop(signal, ExportFailureReason.ExportFailure, 1);
            throw;
        }
    }
}
