using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;
using OnuObservability.Hosting.Composition;
using OnuObservability.Mcp;
using OnuObservability.Mcp.Classification;
using OnuObservability.Sample.Mcp;

var builder = Host.CreateApplicationBuilder(args);
var observability = builder.Services.AddOnuObservability(builder.Configuration);

// Register the domain-aware outcome classifier before WithOnuObservability so the
// adapter's TryAddSingleton keeps this implementation.
builder.Services.AddSingleton<IMcpTelemetryOutcomeClassifier, SampleOutcomeClassifier>();

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<SampleTools>()
    .WithOnuObservability(observability);

await builder.Build().RunAsync();
