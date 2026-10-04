using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OnuObservability.Hosting.Composition;
using OnuObservability.Hosting.Configuration;
using OpenTelemetry.Exporter;

namespace OnuObservability.Hosting.Tests;

public sealed class ObservabilityConfigurationTests
{
    [Fact]
    public void DefaultsProduceImmutableEffectiveSettings()
    {
        var options = ValidOptions();

        var settings = ObservabilityConfiguration.CreateEffective(options);

        Assert.True(settings.Enabled);
        Assert.True(settings.ExportTraces);
        Assert.True(settings.ExportMetrics);
        Assert.False(settings.ExportLogs);
        Assert.Equal(TimeSpan.FromSeconds(5), settings.ShutdownFlushTimeout);
        Assert.Equal(OtlpTransportProtocol.Grpc, settings.ExporterProtocol);
    }

    [Fact]
    public void OpenTelemetryEnvironmentTakesPrecedenceOverSectionValues()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["OnuObservability:ServiceName"] = "section-service",
            ["OnuObservability:ExporterProtocol"] = "grpc",
            ["OnuObservability:TraceSamplingRatio"] = "1",
            ["OTEL_SERVICE_NAME"] = "environment-service",
            ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf",
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "https://collector.example.test",
            ["OTEL_TRACES_SAMPLER_ARG"] = "0.25",
            ["OTEL_LOGS_EXPORTER"] = "otlp",
            ["OTEL_RESOURCE_ATTRIBUTES"] = "service.namespace=environment-namespace,deployment.environment.name=staging",
        });

        var options = ObservabilityConfiguration.BindEffectiveOptions(configuration);

        Assert.Equal("environment-service", options.ServiceName);
        Assert.Equal("environment-namespace", options.ServiceNamespace);
        Assert.Equal("staging", options.DeploymentEnvironment);
        Assert.Equal("http/protobuf", options.ExporterProtocol);
        Assert.Equal(0.25, options.TraceSamplingRatio);
        Assert.True(options.ExporterEnabled);
        Assert.True(options.ExportLogs);
    }

    [Fact]
    public void DependencyInjectionValidatesAndExposesEffectiveSettings()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["OnuObservability:ServiceName"] = "sample-service",
        });
        var services = new ServiceCollection();
        services.AddOnuObservabilityConfiguration(configuration);

        using var provider = services.BuildServiceProvider();
        var settings = provider.GetRequiredService<EffectiveObservabilitySettings>();

        Assert.Equal("sample-service", settings.ServiceName);
        Assert.NotNull(provider.GetRequiredService<IOptions<OnuObservabilityOptions>>());
    }

    [Fact]
    public void PerSignalExporterFlagsAreIndependent()
    {
        var configuration = Configuration(new Dictionary<string, string?>
        {
            ["OnuObservability:ServiceName"] = "sample-service",
            ["OTEL_TRACES_EXPORTER"] = "none",
            ["OTEL_METRICS_EXPORTER"] = "otlp",
            ["OTEL_LOGS_EXPORTER"] = "none",
        });

        var options = ObservabilityConfiguration.BindEffectiveOptions(configuration);

        Assert.False(options.ExportTraces);
        Assert.True(options.ExportMetrics);
        Assert.False(options.ExportLogs);
    }

    [Fact]
    public void HttpProtobufUsesSignalSpecificPathsAndGrpcUsesBaseEndpoint()
    {
        var httpOptions = ValidOptions();
        httpOptions.ExporterProtocol = "http/protobuf";
        var httpSettings = ObservabilityConfiguration.CreateEffective(httpOptions);
        var endpoint = new Uri("https://collector.example.test:4318/base/");

        var trace = OnuObservabilityServiceCollectionExtensions.CreateExporterOptions(
            httpSettings,
            endpoint,
            "trace");
        var metric = OnuObservabilityServiceCollectionExtensions.CreateExporterOptions(
            httpSettings,
            endpoint,
            "metric");

        Assert.Equal(OtlpExportProtocol.HttpProtobuf, trace.Protocol);
        Assert.Equal("https://collector.example.test:4318/base/v1/traces", trace.Endpoint.AbsoluteUri);
        Assert.Equal("https://collector.example.test:4318/base/v1/metrics", metric.Endpoint.AbsoluteUri);

        var grpcSettings = ObservabilityConfiguration.CreateEffective(ValidOptions());
        var grpc = OnuObservabilityServiceCollectionExtensions.CreateExporterOptions(
            grpcSettings,
            endpoint,
            "trace");
        Assert.Equal(OtlpExportProtocol.Grpc, grpc.Protocol);
        Assert.Equal(endpoint, grpc.Endpoint);
    }

    [Fact]
    public void EveryFiniteBoundRejectsValuesOutsideItsRange()
    {
        AssertInvalid(options => options.ExportTimeoutMilliseconds = 0);
        AssertInvalid(options => options.ExportTimeoutMilliseconds = 120_001);
        AssertInvalid(options => options.BatchDelayMilliseconds = 0);
        AssertInvalid(options => options.BatchDelayMilliseconds = 60_001);
        AssertInvalid(options => options.QueueCapacity = 0);
        AssertInvalid(options => options.QueueCapacity = 65_537);
        AssertInvalid(options => options.MaximumExportBatchSize = 0);
        AssertInvalid(options => options.MaximumExportBatchSize = options.QueueCapacity + 1);
        AssertInvalid(options => options.ShutdownFlushTimeoutMilliseconds = 0);
        AssertInvalid(options => options.ShutdownFlushTimeoutMilliseconds = 120_001);
        AssertInvalid(options => options.MaximumLogBodyLength = 63);
        AssertInvalid(options => options.MaximumLogBodyLength = 16_385);
        AssertInvalid(options => options.MaximumAttributeLength = 15);
        AssertInvalid(options => options.MaximumAttributeLength = 4_097);
        AssertInvalid(options => options.LogRateLimitWindowSeconds = 0);
        AssertInvalid(options => options.LogRateLimitWindowSeconds = 3_601);
        AssertInvalid(options => options.LogRateLimitBurst = 0);
        AssertInvalid(options => options.LogRateLimitBurst = 10_001);
        AssertInvalid(options => options.MaximumLogRateLimitKeys = 0);
        AssertInvalid(options => options.MaximumLogRateLimitKeys = 10_001);
        AssertInvalid(options => options.TraceSamplingRatio = -0.01);
        AssertInvalid(options => options.TraceSamplingRatio = 1.01);
    }

    [Theory]
    [InlineData("grpc")]
    [InlineData("http/protobuf")]
    public void SupportedProtocolsAreAccepted(string protocol)
    {
        var options = ValidOptions();
        options.ExporterProtocol = protocol;

        Assert.True(new ObservabilityOptionsValidator().Validate(null, options).Succeeded);
    }

    [Theory]
    [InlineData("http://localhost:4317")]
    [InlineData("http://127.0.0.1:4317")]
    [InlineData("https://collector.example.test:4317")]
    public void SafeEndpointsAreAccepted(string endpoint)
    {
        var options = ValidOptions();
        options.ExporterEnabled = true;
        options.Endpoint = endpoint;

        Assert.True(new ObservabilityOptionsValidator().Validate(null, options).Succeeded);
    }

    [Fact]
    public void ExplicitSidecarAllowsClearTextEndpoint()
    {
        var options = ValidOptions();
        options.ExporterEnabled = true;
        options.Endpoint = "http://otel-sidecar:4317";
        options.InsecureSidecarHosts = ["otel-sidecar"];

        Assert.True(new ObservabilityOptionsValidator().Validate(null, options).Succeeded);
    }

    [Theory]
    [InlineData("http://collector.example.test:4317")]
    [InlineData("ftp://collector.example.test")]
    [InlineData("https://user:synthetic-secret@collector.example.test")]
    [InlineData("https://collector.example.test?token=synthetic-secret")]
    public void UnsafeEndpointsFailWithoutEchoingSecrets(string endpoint)
    {
        var options = ValidOptions();
        options.ExporterEnabled = true;
        options.Endpoint = endpoint;

        var validation = new ObservabilityOptionsValidator().Validate(null, options);

        Assert.True(validation.Failed);
        Assert.DoesNotContain("synthetic-secret", string.Join(' ', validation.Failures), StringComparison.Ordinal);
    }

    private static void AssertInvalid(Action<OnuObservabilityOptions> mutate)
    {
        var options = ValidOptions();
        mutate(options);
        Assert.True(new ObservabilityOptionsValidator().Validate(null, options).Failed);
    }

    private static OnuObservabilityOptions ValidOptions() => new()
    {
        ServiceName = "sample-service",
    };

    private static IConfiguration Configuration(IDictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
