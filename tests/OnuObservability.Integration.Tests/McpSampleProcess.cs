using System.Diagnostics;
using System.Text.Json;

namespace OnuObservability.Integration.Tests;

internal sealed class McpSampleProcess : IAsyncDisposable
{
    private static readonly JsonSerializerOptions WireOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly Process _process;
    private readonly Task<string> _stderr;
    private int _requestId;

    public McpSampleProcess(IReadOnlyDictionary<string, string?>? environment = null)
    {
        var sampleAssembly = SampleAssemblyResolver.Resolve(
            "ONU_MCP_SAMPLE_DLL",
            "OnuObservability.Sample.Mcp",
            "OnuObservability.Sample.Mcp.dll");

        var startInfo = new ProcessStartInfo("dotnet")
        {
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(sampleAssembly)!,
        };
        startInfo.ArgumentList.Add(sampleAssembly);
        if (environment is not null)
        {
            foreach (var (key, value) in environment)
            {
                startInfo.Environment[key] = value;
            }
        }

        _process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("The MCP sample process could not be started.");
        _stderr = _process.StandardError.ReadToEndAsync();
    }

    public async Task InitializeAsync()
    {
        var response = await RequestAsync(
            "initialize",
            new
            {
                protocolVersion = "2025-11-25",
                capabilities = new { },
                clientInfo = new { name = "onu-observability-tests", version = "1.0" },
            });
        Assert.Equal(
            "2025-11-25",
            response.GetProperty("result").GetProperty("protocolVersion").GetString());
        await NotifyAsync("notifications/initialized", new { });
    }

    public async Task<JsonElement> RequestAsync(string method, object parameters)
    {
        var id = Interlocked.Increment(ref _requestId);
        var request = JsonSerializer.Serialize(
            new { jsonrpc = "2.0", id, method, @params = parameters },
            WireOptions);
        await _process.StandardInput.WriteLineAsync(request);

        while (true)
        {
            var line = await _process.StandardOutput.ReadLineAsync()
                .WaitAsync(TimeSpan.FromSeconds(30));
            if (line is null)
            {
                throw new InvalidOperationException(
                    $"MCP sample exited before responding. stderr: {await _stderr}");
            }

            using var document = JsonDocument.Parse(line);
            Assert.Equal("2.0", document.RootElement.GetProperty("jsonrpc").GetString());
            if (document.RootElement.TryGetProperty("id", out var responseId)
                && responseId.ValueKind == JsonValueKind.Number
                && responseId.GetInt32() == id)
            {
                return document.RootElement.Clone();
            }
        }
    }

    public Task NotifyAsync(string method, object parameters) =>
        _process.StandardInput.WriteLineAsync(
            JsonSerializer.Serialize(
                new { jsonrpc = "2.0", method, @params = parameters },
                WireOptions));

    public async Task<string> StopAsync()
    {
        _process.StandardInput.Close();
        await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(0, _process.ExitCode);
        return await _stderr;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync();
        }

        _process.Dispose();
    }
}
