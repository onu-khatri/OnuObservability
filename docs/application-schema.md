# Application-owned schemas

OnuObservability owns reusable pipeline, HTTP, background aggregate, and MCP-boundary schemas. A consuming application owns its domain operations and dependencies. It registers immutable descriptors at startup and emits only finite labels plus bounded aggregate fields.

For AgentMemoryMCP, application-owned descriptors include Ollama discovery/embedding, Qdrant health/query/upsert/delete, filesystem transactions and locks, maintenance passes, reconciliation/recovery, reindex, and migration. Those descriptors belong in AgentMemoryMCP composition code—not in any production OnuObservability assembly. The application maps domain exceptions to registered outcomes/error/reason codes through typed hooks; it never passes exception messages or response content.

```csharp
var maintenance = BackgroundOperationSchema.CreateDescriptor("agentmemory.maintenance.pass");
var ollama = new OperationDescriptor("agentmemory.ollama.embedding");
var qdrant = new OperationDescriptor("agentmemory.qdrant.query");
var transaction = new OperationDescriptor("agentmemory.filesystem.transaction");
var tool = new OperationDescriptor("agentmemory.tool.call");

var observability = services.AddOnuObservability(configuration)
    .AddOperation(maintenance)
    .AddOperation(ollama)
    .AddOperation(qdrant)
    .AddOperation(transaction)
    .AddOperation(tool)
    .AddOutcome(CommonOutcomes.DependencyError)
    .AddError(new ErrorCode("qdrant_unavailable"));
```

The names above are an ownership example. The consuming repository remains authoritative for its reviewed manifest and compatibility mapping.
