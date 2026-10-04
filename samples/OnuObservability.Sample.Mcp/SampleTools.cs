using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;
using OnuObservability.Model;
using OnuObservability.Mcp.Classification;
using OnuObservability.Mcp.Health;
using OnuObservability.Mcp.Schema;

namespace OnuObservability.Sample.Mcp;

[McpServerToolType]
internal sealed class SampleTools
{
    [McpServerTool(Name = "echo")]
    [Description("Returns the supplied text. Telemetry records counts only, never this value.")]
    public static string Echo(string value) => value;

    [McpServerTool(Name = "fail")]
    [Description("Returns a deterministic sample failure (internal_error).")]
    public static void Fail() => throw new InvalidOperationException("sample failure");

    [McpServerTool(Name = "purge", Destructive = true)]
    [Description("Destructive tool. Telemetry records a destructive-tool-attempt warning.")]
    public static string Purge(string resourceId) => $"purged {resourceId}";

    [McpServerTool(Name = "order")]
    [Description("Validates an order quantity, demonstrating bounded validation classification.")]
    public static string Order(int quantity)
    {
        if (quantity <= 0)
        {
            throw new McpClassifiedException(
                "Order quantity must be positive.",
                new McpTelemetryClassification(
                    McpTelemetrySchema.ToolErrorOutcome,
                    new ErrorCode("invalid_request"),
                    ValidationReason: McpValidationReason.OutOfRange));
        }

        return $"accepted {quantity} items";
    }

    [McpServerTool(Name = "approve")]
    [Description("Requests approval, demonstrating bounded denial classification.")]
    public static string Approve()
    {
        throw new McpClassifiedException(
            "Approval is required for this action.",
            new McpTelemetryClassification(
                CommonOutcomes.AuthorizationDenied,
                new ErrorCode("approval_required"),
                DenialReason: McpDenialReason.ApprovalRequired));
    }

    [McpServerTool(Name = "suspended")]
    [Description("Throws a domain exception classified by the custom classifier.")]
    public static string Suspended() => throw new AccountSuspendedException();

    [McpServerTool(Name = "search")]
    [Description("Records a bounded dependency failure (never the raw operation).")]
    public static string Search(string query, IServiceProvider services)
    {
        services.GetRequiredService<IMcpDependencyFailureRecorder>()
            .Record("qdrant", "query");
        return $"searched {query}";
    }
}
