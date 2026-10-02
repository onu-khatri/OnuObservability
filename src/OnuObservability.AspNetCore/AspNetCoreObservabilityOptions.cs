namespace OnuObservability.AspNetCore;

public sealed class AspNetCoreObservabilityOptions
{
    public bool InstrumentRequests { get; set; } = true;

    public int MaximumRouteTemplates { get; set; } = 1024;

    public string[] ExcludedRouteTemplates { get; set; } = [];
}
