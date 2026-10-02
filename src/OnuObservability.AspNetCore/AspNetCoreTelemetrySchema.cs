using OnuObservability.Model;

namespace OnuObservability.AspNetCore;

public static class AspNetCoreTelemetrySchema
{
    public const string MeterName = "OnuObservability.AspNetCore";
    public const string RequestDurationInstrument = "onu.http.server.request.duration";
    public const string RequestCountInstrument = "onu.http.server.request.count";
    public const string UnknownRoute = "other";

    public static Outcome NotFoundOutcome { get; } = new("not_found");

    public static Outcome ServerErrorOutcome { get; } = new("server_error");
}
