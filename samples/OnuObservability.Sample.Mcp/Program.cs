// See https://aka.ms/new-console-template for more information
using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;
using OnuObservability.Hosting;
using OnuObservability.Mcp;

var builder = Host.CreateApplicationBuilder(args);
var observability = builder.Services.AddOnuObservability(builder.Configuration);
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<SampleTools>()
    .WithOnuObservability(observability);

await builder.Build().RunAsync();

[McpServerToolType]
internal sealed class SampleTools
{
    [McpServerTool(Name = "echo")]
    [Description("Returns the supplied text. Telemetry records counts only, never this value.")]
    public static string Echo(string value) => value;

    [McpServerTool(Name = "fail")]
    [Description("Returns a deterministic sample failure.")]
    public static void Fail() => throw new InvalidOperationException("sample failure");
}
