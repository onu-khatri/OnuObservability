using System.Net;
using Microsoft.Extensions.Options;

namespace OnuObservability.Hosting.Configuration;

public sealed class ObservabilityOptionsValidator : IValidateOptions<OnuObservabilityOptions>
{
    public ValidateOptionsResult Validate(string? name, OnuObservabilityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var errors = new List<string>();

        RequireIdentifier(options.ServiceName, "service name", errors);
        RequireIdentifier(options.ServiceNamespace, "service namespace", errors);
        if (options.ServiceVersion is not null)
        {
            RequireIdentifier(options.ServiceVersion, "service version", errors);
        }
        RequireIdentifier(options.DeploymentEnvironment, "deployment environment", errors);
        RequireIdentifier(options.TelemetrySchemaVersion, "telemetry schema version", errors);
        RequireIdentifier(options.PolicyVersion, "policy version", errors);

        if (!double.IsFinite(options.TraceSamplingRatio) || options.TraceSamplingRatio is < 0 or > 1)
        {
            errors.Add("The trace sampling ratio must be finite and between zero and one.");
        }

        RequireBound(options.ExportTimeoutMilliseconds, 1, 120_000, "export timeout", errors);
        RequireBound(options.BatchDelayMilliseconds, 1, 60_000, "batch delay", errors);
        RequireBound(options.QueueCapacity, 1, 65_536, "queue capacity", errors);
        RequireBound(options.MaximumExportBatchSize, 1, options.QueueCapacity, "maximum export batch size", errors);
        RequireBound(options.ShutdownFlushTimeoutMilliseconds, 1, 120_000, "shutdown flush timeout", errors);
        RequireBound(options.MaximumLogBodyLength, 64, 16_384, "maximum log body length", errors);
        RequireBound(options.MaximumAttributeLength, 16, 4_096, "maximum attribute length", errors);
        RequireBound(options.LogRateLimitWindowSeconds, 1, 3_600, "log rate-limit window", errors);
        RequireBound(options.LogRateLimitBurst, 1, 10_000, "log rate-limit burst", errors);
        RequireBound(options.MaximumLogRateLimitKeys, 1, 10_000, "log rate-limit key capacity", errors);

        if (!TryParseProtocol(options.ExporterProtocol, out _))
        {
            errors.Add("The exporter protocol must be grpc or http/protobuf.");
        }

        var sidecars = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var host in options.InsecureSidecarHosts ?? [])
        {
            if (!IsSafeHost(host))
            {
                errors.Add("Insecure sidecar hosts must be bounded DNS names or IP literals.");
            }
            else
            {
                sidecars.Add(host);
            }
        }

        if (options.ExporterEnabled)
        {
            ValidateEndpoint(options.Endpoint, sidecars, errors);
        }

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }

    internal static bool TryParseProtocol(string? value, out OtlpTransportProtocol protocol)
    {
        protocol = value?.ToLowerInvariant() switch
        {
            "grpc" => OtlpTransportProtocol.Grpc,
            "http/protobuf" => OtlpTransportProtocol.HttpProtobuf,
            _ => default,
        };
        return value is not null && value.Equals("grpc", StringComparison.OrdinalIgnoreCase) ||
               value is not null && value.Equals("http/protobuf", StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateEndpoint(
        string? value,
        HashSet<string> insecureSidecars,
        List<string> errors)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint) ||
            endpoint.Scheme is not ("http" or "https") ||
            endpoint.UserInfo.Length != 0 ||
            endpoint.Query.Length != 0 ||
            endpoint.Fragment.Length != 0)
        {
            errors.Add("The exporter endpoint must be an absolute HTTP(S) URI without credentials, query, or fragment.");
            return;
        }

        if (endpoint.Scheme == "http" &&
            !IsLoopback(endpoint.DnsSafeHost) &&
            !insecureSidecars.Contains(endpoint.DnsSafeHost))
        {
            errors.Add("Clear-text export is allowed only for loopback or an explicitly configured same-host sidecar.");
        }
    }

    private static bool IsLoopback(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
        (IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address));

    private static bool IsSafeHost(string? host) =>
        !string.IsNullOrWhiteSpace(host) &&
        host.Length <= 253 &&
        (IPAddress.TryParse(host, out _) ||
         host.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-'));

    private static void RequireIdentifier(string? value, string label, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > 128 ||
            !value.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-'))
        {
            errors.Add($"The {label} must contain only letters, digits, dots, underscores, or hyphens and be at most 128 characters.");
        }
    }

    private static void RequireBound(
        int value,
        int minimum,
        int maximum,
        string label,
        List<string> errors)
    {
        if (value < minimum || value > maximum)
        {
            errors.Add($"The {label} must be between {minimum} and {maximum}.");
        }
    }
}
