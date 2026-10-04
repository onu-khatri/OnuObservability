using OnuObservability.Model;
using OnuObservability.Operations;
using OnuObservability.Safety;

namespace OnuObservability.Sample.Web;

/// <summary>
/// A request-scoped checkout flow. In contrast to the worker sample, which drives
/// coarse background passes through <see cref="IBackgroundOperationRunner"/>, a web
/// request uses the lower-level <see cref="IOperationFactory"/> directly so it can
/// record bounded domain fields (finite string, number, count, flag, text) on a span.
/// </summary>
public sealed class CheckoutService(IOperationFactory operations, IHttpClientFactory httpClientFactory)
{
    public async Task<CheckoutOutcome> CheckoutAsync(
        CheckoutRequest request,
        CancellationToken cancellationToken)
    {
        using var scope = operations.Start(OrderTelemetry.Checkout);

        // An unknown payment method falls back to "other"; an out-of-range total or
        // item count is clamped; a note that looks sensitive or is too long is
        // rejected or truncated. The sink records truncation/clamp/fallback evidence.
        scope.Record(OrderTelemetry.PaymentMethod, TelemetryValue.FromText(request.PaymentMethod));
        scope.Record(OrderTelemetry.OrderTotal, TelemetryValue.FromNumber(request.Total));
        scope.Record(OrderTelemetry.ItemCount, TelemetryValue.FromCount(request.ItemCount));
        scope.Record(OrderTelemetry.RequiresReview, TelemetryValue.FromFlag(request.RequiresReview));
        scope.Record(OrderTelemetry.CheckoutNote, TelemetryValue.FromText(request.Note));

        OperationResult result;
        CheckoutOutcome outcome;
        try
        {
            using var inventory = httpClientFactory.CreateClient("inventory");
            using var response = await inventory.GetAsync("/inventory/stock", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                outcome = CheckoutOutcome.Completed;
                result = new OperationResult(OrderTelemetry.CheckoutCompleted);
            }
            else
            {
                outcome = CheckoutOutcome.OutOfStock;
                result = new OperationResult(
                    OrderTelemetry.OutOfStock,
                    OrderTelemetry.InventoryUnavailable,
                    OrderTelemetry.Retriable);
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            outcome = CheckoutOutcome.OutOfStock;
            result = new OperationResult(
                CommonOutcomes.DependencyError,
                OrderTelemetry.InventoryUnavailable,
                OrderTelemetry.Retriable);
        }

        scope.Complete(result);
        return outcome;
    }
}

public enum CheckoutOutcome
{
    Completed,
    OutOfStock,
}

public sealed record CheckoutRequest(
    string? PaymentMethod,
    double Total,
    int ItemCount,
    bool RequiresReview,
    string? Note);
