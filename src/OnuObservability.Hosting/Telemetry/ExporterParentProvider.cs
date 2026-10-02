using System.Reflection;
using OpenTelemetry;

namespace OnuObservability.Hosting.Telemetry;

internal static class ExporterParentProvider
{
    public static void Assign<T>(BaseExporter<T> exporter, BaseProvider provider)
        where T : class => Cache<T>.Setter.Invoke(exporter, [provider]);

    private static class Cache<T>
        where T : class
    {
        public static readonly MethodInfo Setter =
            typeof(BaseExporter<T>)
                .GetProperty(
                    nameof(BaseExporter<T>.ParentProvider),
                    BindingFlags.Instance | BindingFlags.Public)!
                .GetSetMethod(nonPublic: true)
            ?? throw new MissingMethodException(
                typeof(BaseExporter<T>).FullName,
                "set_ParentProvider");
    }
}
