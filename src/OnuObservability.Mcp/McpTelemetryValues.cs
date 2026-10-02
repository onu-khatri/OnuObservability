using System.Collections.Frozen;
using OnuObservability.Model;

namespace OnuObservability.Mcp;

internal static class McpTelemetryValues
{
    private static readonly FrozenSet<string> DependencyTypes = new[]
    {
        "ollama", "qdrant", "filesystem", "maintenance", McpTelemetrySchema.Other,
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> DependencyOperations = new[]
    {
        "model_discovery", "embedding", "health", "collection", "migration",
        "query", "upsert", "delete", "lock", "transaction", "recovery",
        "maintenance", "reconciliation", "reindex", McpTelemetrySchema.Other,
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> ErrorCodes = new[]
    {
        "approval_required", "capacity_exceeded", "claim_conflict", "client_cancelled",
        "content_policy_rejected", "deadline_exceeded", "dependency_grpc_error",
        "dependency_http_error", "dependency_error", "event_type_reserved",
        "index_incompatible", "index_incomplete", "internal_error", "invalid_request",
        "lock_timeout", "mcp_error", "not_found", "operation_conflict", "policy_denied",
        "protocol_error", "review_required", "revision_conflict",
        "source_coverage_incomplete", "stale_claim", "storage_error", "storage_invalid",
    }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly FiniteValueRegistry<Outcome> Outcomes = new(
        CommonOutcomes.Registry.Values
            .Concat(
            [
                McpTelemetrySchema.ClientCancelledOutcome,
                McpTelemetrySchema.ToolErrorOutcome,
                McpTelemetrySchema.ProtocolErrorOutcome,
            ])
            .DistinctBy(outcome => outcome.Value, StringComparer.Ordinal),
        CommonOutcomes.Other,
        outcome => outcome.Value);

    public static IEnumerable<string> RegisteredErrorCodes => ErrorCodes;

    public static string NormalizeError(string? value) =>
        value is not null && ErrorCodes.Contains(value) ? value : McpTelemetrySchema.Other;

    public static string NormalizeDependencyType(string? value) =>
        value is not null && DependencyTypes.Contains(value) ? value : McpTelemetrySchema.Other;

    public static string NormalizeDependencyOperation(string? value) =>
        value is not null && DependencyOperations.Contains(value)
            ? value
            : McpTelemetrySchema.Other;

    public static McpTelemetryClassification Normalize(
        McpTelemetryClassification classification) =>
        classification with
        {
            Outcome = NormalizeOutcome(classification.Outcome),
            Error = classification.Error is { } error
                ? new ErrorCode(NormalizeError(error.Value))
                : null,
        };

    public static Outcome NormalizeOutcome(Outcome outcome) => Outcomes.Normalize(outcome);

    public static string ValidationReason(McpValidationReason value) => value switch
    {
        McpValidationReason.MissingRequired => "missing_required",
        McpValidationReason.WrongType => "wrong_type",
        McpValidationReason.UnknownField => "unknown_field",
        McpValidationReason.InvalidFormat => "invalid_format",
        McpValidationReason.OutOfRange => "out_of_range",
        McpValidationReason.DomainRule => "domain_rule",
        _ => McpTelemetrySchema.Other,
    };

    public static string DenialReason(McpDenialReason value) => value switch
    {
        McpDenialReason.ApprovalRequired => "approval_required",
        McpDenialReason.PolicyDenied => "policy_denied",
        _ => McpTelemetrySchema.Other,
    };
}
