using OpenTelemetry.Trace;

namespace OnuObservability.ArchitectureFixtures.Invalid;

public sealed class InvalidArchitectureFixture
{
    public static string MutableGlobalState = "invalid";

    public TracerProvider? LeakedSdkType { get; set; }

    public object? LocateService(IServiceProvider services, Type serviceType)
    {
        return services.GetService(serviceType);
    }
}
