using ModelContextProtocol.Server;

namespace OnuObservability.Mcp.Tests;

public sealed class McpRegisteredToolRegistryTests
{
    [Fact]
    public void RegistryUsesTheExactSdkDescriptorsAndUnknownFallback()
    {
        var first = Tool("first_tool", destructive: false);
        var destructive = Tool("dangerous_tool", destructive: true);
        var registry = new McpRegisteredToolRegistry([first, destructive]);

        Assert.Equal(["dangerous_tool", "first_tool"], registry.Names.ToArray());
        Assert.Equal("first_tool", registry.Resolve(first));
        Assert.Equal("dangerous_tool", registry.Resolve(destructive));
        Assert.True(registry.IsDestructive(destructive));
        Assert.Equal("unknown", registry.Resolve(Tool("first_tool", destructive: false)));
    }

    [Fact]
    public void ToolsetVersionIsStableAcrossInputOrderAndChangesWithSdkMetadata()
    {
        var first = Tool("first_tool", destructive: false);
        var second = Tool("second_tool", destructive: false);
        var forward = new McpRegisteredToolRegistry([first, second]);
        var reverse = new McpRegisteredToolRegistry([second, first]);
        var changed = new McpRegisteredToolRegistry(
            [Tool("first_tool", destructive: true), second]);

        Assert.Equal(forward.ToolsetVersion, reverse.ToolsetVersion);
        Assert.StartsWith("sha256:", forward.ToolsetVersion, StringComparison.Ordinal);
        Assert.NotEqual(forward.ToolsetVersion, changed.ToolsetVersion);
    }

    private static McpServerTool Tool(string name, bool destructive) =>
        McpServerTool.Create(
            (Func<string>)(() => "ok"),
            new McpServerToolCreateOptions
            {
                Name = name,
                Destructive = destructive,
            });
}
