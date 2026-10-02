using OnuObservability.Hosting;
using OnuObservability.Hosting.Background;
using OnuObservability.Schema;

namespace OnuObservability.Sample.Worker;

public static class WorkerTelemetry
{
    public static OperationDescriptor Pass { get; } =
        BackgroundOperationSchema.CreateDescriptor("sample.worker.pass");

    public static OperationDescriptor Dependency { get; } =
        new("sample.worker.dependency");

    public static OnuObservabilityBuilder AddWorkerTelemetry(
        this OnuObservabilityBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .AddOperation(Pass)
            .AddOperation(Dependency);
    }
}
