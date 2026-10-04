using OnuObservability.Hosting.Composition;
using OnuObservability.Model;
using OnuObservability.Schema;

namespace OnuObservability.Sample.Web;

/// <summary>
/// Application-owned domain schema for the sample order service.
///
/// OnuObservability owns the reusable pipeline/HTTP/background schemas; a consuming
/// application owns its domain operations and dependencies. It registers immutable
/// descriptors at startup and emits only finite labels plus bounded aggregate fields.
/// Every emitted value is normalized against the descriptor before it is recorded:
/// finite strings fall back, counts and numbers clamp, text truncates or is rejected
/// when it looks sensitive.
/// </summary>
public static class OrderTelemetry
{
    // Domain outcomes. Only registered outcomes survive the schema registry; anything
    // else is normalized to the "other" fallback at the sink boundary.
    public static Outcome CheckoutCompleted { get; } = new("checkout_completed");
    public static Outcome OutOfStock { get; } = new("out_of_stock");

    // Domain error codes, emitted alongside a non-success outcome.
    public static ErrorCode PaymentDeclined { get; } = new("payment_declined");
    public static ErrorCode InventoryUnavailable { get; } = new("inventory_unavailable");

    // Domain reason codes, emitted to explain why an outcome occurred.
    public static ReasonCode Retriable { get; } = new("retriable");
    public static ReasonCode NonRetriable { get; } = new("non_retriable");

    // Bounded fields. Each field constrains the shape of what may be recorded.
    public static TelemetryFieldDescriptor PaymentMethod { get; } =
        TelemetryFieldDescriptor.FiniteString(
            "payment.method",
            ["card", "wallet", "bank_transfer", "other"],
            fallbackValue: "other");

    public static TelemetryFieldDescriptor OrderTotal { get; } =
        TelemetryFieldDescriptor.Number(
            "order.total",
            minimum: 0,
            maximum: 1_000_000,
            unit: "usd");

    public static TelemetryFieldDescriptor ItemCount { get; } =
        TelemetryFieldDescriptor.Count("order.items", maximum: 10_000);

    public static TelemetryFieldDescriptor RequiresReview { get; } =
        TelemetryFieldDescriptor.Flag("order.requires_review");

    public static TelemetryFieldDescriptor CheckoutNote { get; } =
        TelemetryFieldDescriptor.BoundedText("order.note", maximumLength: 128);

    // The checkout operation exposes the bounded fields above as span attributes.
    public static OperationDescriptor Checkout { get; } = new(
        "sample.web.checkout",
        [PaymentMethod, OrderTotal, ItemCount, RequiresReview, CheckoutNote]);

    public static OnuObservabilityBuilder AddOrderTelemetry(this OnuObservabilityBuilder builder) => builder
        .AddOperation(Checkout)
        .AddOutcome(CheckoutCompleted)
        .AddOutcome(OutOfStock)
        .AddError(PaymentDeclined)
        .AddError(InventoryUnavailable)
        .AddReason(Retriable)
        .AddReason(NonRetriable);
}
