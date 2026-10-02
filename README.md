# OnuObservability

`OnuObservability` is a privacy-first observability package family for ASP.NET Core applications, Generic Host background services, and .NET MCP servers.

This standalone repository is intentionally self-contained. Its approved remote destination is [onu-khatri/OnuObservability](https://github.com/onu-khatri/OnuObservability), and it has no project references to the sibling AgentMemoryMCP solution.

## Packages

| Package | Responsibility | Direct inward dependency |
|---|---|---|
| `OnuObservability` | Typed schemas, bounded values, outcomes, operation contracts, and safety policy | none |
| `OnuObservability.Hosting` | Generic Host configuration, OpenTelemetry composition, resources, exporters, and lifecycle | `OnuObservability` |
| `OnuObservability.AspNetCore` | ASP.NET Core request enrichment and bounded HTTP policy | `OnuObservability.Hosting` |
| `OnuObservability.Mcp` | MCP request/tool boundaries, tool registry, typed outcome channel, and stdio-safe profile | `OnuObservability.Hosting` |

Cross-adapter dependencies are forbidden. Samples reference only the adapter needed by their application type.

## Local verification

```powershell
dotnet restore OnuObservability.sln
dotnet build OnuObservability.sln -c Release --no-restore
dotnet test OnuObservability.sln -c Release --no-build
```

All projects target `net10.0` and are validated with the .NET 10 SDK/runtime on Windows and Linux. Remote export is opt-in. During development, packages are written only to the isolated local feed at `artifacts/packages`; publication to an external NuGet feed is not authorized.

See [CONTRIBUTING.md](CONTRIBUTING.md), [COMPATIBILITY.md](COMPATIBILITY.md), [VERSIONING.md](VERSIONING.md), [SECURITY.md](SECURITY.md), [SUPPORT.md](SUPPORT.md), [OWNERSHIP.md](OWNERSHIP.md), and [RELEASING.md](RELEASING.md).
