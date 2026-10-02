# Compatibility

The initial and only supported asset baseline is `net10.0`. Consumers are tested with the .NET 10 SDK/runtime on Windows and Linux. Adding or removing a supported runtime requires the semantic-version impact, migration guidance, and support-window decision to be approved.

The MCP adapter preserves the AgentMemoryMCP `mcp.*` schema baseline until an explicitly approved additive or breaking change is recorded. Configuration and collector contracts remain compatible across a rollback window.
