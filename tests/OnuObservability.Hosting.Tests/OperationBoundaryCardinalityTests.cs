using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OnuObservability.Hosting.Composition;
using OnuObservability.Hosting.Telemetry;
using OnuObservability.Model;
using OnuObservability.Operations;
using OnuObservability.Schema;

namespace OnuObservability.Hosting.Tests;

public sealed class OperationBoundaryCardinalityTests
{
    [Fact]
    public void UniqueCallerResultsCollapseAndUnregisteredDescriptorsFailClosed()
    {
        var descriptor = new OperationDescriptor("sample.registered");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOnuObservability(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["OnuObservability:Enabled"] = "false",
                    ["OnuObservability:ServiceName"] = "cardinality-test",
                })
                .Build())
            .AddOperation(descriptor);
        using var activities = new ActivityCapture();
        using var metrics = new MetricCapture();
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IOperationFactory>();

        for (var index = 0; index < 2_000; index++)
        {
            using var scope = factory.Start(descriptor);
            scope.Complete(new(
                new Outcome("caller_outcome_" + index),
                new ErrorCode("caller_error_" + index),
                new ReasonCode("caller_reason_" + index)));
        }

        Assert.All(activities.Stopped, activity =>
        {
            Assert.Equal("sample.registered", activity.DisplayName);
            Assert.Equal("other", activity.GetTagItem("onu.outcome"));
            Assert.Equal("other", activity.GetTagItem("error.type"));
            Assert.Equal("other", activity.GetTagItem("onu.reason"));
        });
        Assert.Equal(["other"], metrics.Outcomes.Order(StringComparer.Ordinal).ToArray());
        Assert.Throws<ArgumentException>(() => factory.Start(new("caller.operation")));
    }

    private sealed class ActivityCapture : IDisposable
    {
        private readonly ActivityListener _listener;

        public ActivityCapture()
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == OnuTelemetrySources.ActivitySourceName,
                Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                    ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = Stopped.Add,
            };
            ActivitySource.AddActivityListener(_listener);
        }

        public ConcurrentBag<Activity> Stopped { get; } = [];

        public void Dispose() => _listener.Dispose();
    }

    private sealed class MetricCapture : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly ConcurrentDictionary<string, byte> _outcomes = new(StringComparer.Ordinal);

        public MetricCapture()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == OnuTelemetrySources.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>(Capture);
            _listener.SetMeasurementEventCallback<double>(Capture);
            _listener.Start();
        }

        public IEnumerable<string> Outcomes => _outcomes.Keys;

        public void Dispose() => _listener.Dispose();

        private void Capture<T>(
            Instrument instrument,
            T measurement,
            ReadOnlySpan<KeyValuePair<string, object?>> tags,
            object? state)
            where T : struct
        {
            foreach (var tag in tags)
            {
                if (tag.Key == "outcome" && tag.Value is string outcome)
                {
                    _outcomes.TryAdd(outcome, 0);
                }
            }
        }
    }
}
