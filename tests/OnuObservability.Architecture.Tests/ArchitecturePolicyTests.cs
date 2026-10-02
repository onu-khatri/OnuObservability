using System.Runtime.CompilerServices;
using Mono.Cecil;
using Mono.Cecil.Cil;
using OnuObservability.ArchitectureFixtures.Invalid;

namespace OnuObservability.Architecture.Tests;

public sealed class ArchitecturePolicyTests
{
    private static readonly ArchitectureTarget[] ProductionTargets =
    [
        new("OnuObservability.dll", ArchitectureLayer.Core),
        new("OnuObservability.Hosting.dll", ArchitectureLayer.Hosting),
        new("OnuObservability.AspNetCore.dll", ArchitectureLayer.AspNetCore),
        new("OnuObservability.Mcp.dll", ArchitectureLayer.Mcp),
    ];

    [Fact]
    public void ProductionAssembliesSatisfyArchitecturePolicy()
    {
        var violations = ProductionTargets
            .SelectMany(target => ArchitecturePolicy.Evaluate(
                Path.Combine(AppContext.BaseDirectory, target.FileName),
                target.Layer))
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void DeliberatelyInvalidFixtureTriggersEveryExpectedRule()
    {
        var assemblyPath = typeof(InvalidArchitectureFixture).Assembly.Location;

        var ruleIds = ArchitecturePolicy.Evaluate(assemblyPath, ArchitectureLayer.Core)
            .Select(violation => violation.RuleId)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains(ArchitectureRule.ForbiddenReference, ruleIds);
        Assert.Contains(ArchitectureRule.PublicOpenTelemetryApi, ruleIds);
        Assert.Contains(ArchitectureRule.ServiceLocation, ruleIds);
        Assert.Contains(ArchitectureRule.MutableGlobalState, ruleIds);
    }

    private sealed record ArchitectureTarget(string FileName, ArchitectureLayer Layer);
}

internal static class ArchitecturePolicy
{
    private static readonly string[] CoreForbiddenReferencePrefixes =
    [
        "OnuObservability.Hosting",
        "OnuObservability.AspNetCore",
        "OnuObservability.Mcp",
        "OpenTelemetry",
        "Microsoft.AspNetCore",
        "ModelContextProtocol",
    ];

    public static IReadOnlyList<ArchitectureViolation> Evaluate(
        string assemblyPath,
        ArchitectureLayer layer)
    {
        using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);
        var violations = new List<ArchitectureViolation>();

        FindForbiddenReferences(assembly, layer, violations);

        foreach (var type in assembly.MainModule.Types.SelectMany(Flatten))
        {
            FindPublicSdkLeaks(type, violations);
            FindMutableGlobalState(type, violations);
            FindServiceLocation(type, violations);
        }

        return violations
            .OrderBy(violation => violation.RuleId, StringComparer.Ordinal)
            .ThenBy(violation => violation.Location, StringComparer.Ordinal)
            .ToArray();
    }

    private static IEnumerable<TypeDefinition> Flatten(TypeDefinition type)
    {
        yield return type;

        foreach (var nested in type.NestedTypes.SelectMany(Flatten))
        {
            yield return nested;
        }
    }

    private static void FindForbiddenReferences(
        AssemblyDefinition assembly,
        ArchitectureLayer layer,
        List<ArchitectureViolation> violations)
    {
        var forbiddenPrefixes = layer switch
        {
            ArchitectureLayer.Core => CoreForbiddenReferencePrefixes,
            ArchitectureLayer.AspNetCore => ["OnuObservability.Mcp"],
            ArchitectureLayer.Mcp => ["OnuObservability.AspNetCore"],
            _ => [],
        };

        foreach (var reference in assembly.MainModule.AssemblyReferences)
        {
            if (forbiddenPrefixes.Any(prefix =>
                    reference.Name.StartsWith(prefix, StringComparison.Ordinal)))
            {
                violations.Add(new(
                    ArchitectureRule.ForbiddenReference,
                    reference.Name));
            }
        }
    }

    private static void FindPublicSdkLeaks(
        TypeDefinition type,
        List<ArchitectureViolation> violations)
    {
        if (!IsPubliclyVisible(type))
        {
            return;
        }

        AddIfSdkType(type.BaseType, type.FullName, violations);

        foreach (var contract in type.Interfaces)
        {
            AddIfSdkType(contract.InterfaceType, type.FullName, violations);
        }

        foreach (var field in type.Fields.Where(field => field.IsPublic))
        {
            AddIfSdkType(field.FieldType, field.FullName, violations);
        }

        foreach (var property in type.Properties.Where(IsPublicProperty))
        {
            AddIfSdkType(property.PropertyType, property.FullName, violations);
        }

        foreach (var method in type.Methods.Where(method => method.IsPublic))
        {
            AddIfSdkType(method.ReturnType, method.FullName, violations);

            foreach (var parameter in method.Parameters)
            {
                AddIfSdkType(parameter.ParameterType, method.FullName, violations);
            }
        }
    }

    private static bool IsPublicProperty(PropertyDefinition property) =>
        property.GetMethod?.IsPublic == true || property.SetMethod?.IsPublic == true;

    private static void AddIfSdkType(
        TypeReference? type,
        string location,
        List<ArchitectureViolation> violations)
    {
        if (type is not null && UsesOpenTelemetrySdk(type))
        {
            violations.Add(new(
                ArchitectureRule.PublicOpenTelemetryApi,
                location));
        }
    }

    private static bool UsesOpenTelemetrySdk(TypeReference type)
    {
        if (type is GenericInstanceType generic &&
            generic.GenericArguments.Any(UsesOpenTelemetrySdk))
        {
            return true;
        }

        if (type is TypeSpecification specification)
        {
            return UsesOpenTelemetrySdk(specification.ElementType);
        }

        var scopeName = type.Scope?.Name ?? string.Empty;
        return type.Namespace.StartsWith("OpenTelemetry", StringComparison.Ordinal) ||
               scopeName.StartsWith("OpenTelemetry", StringComparison.Ordinal);
    }

    private static void FindMutableGlobalState(
        TypeDefinition type,
        List<ArchitectureViolation> violations)
    {
        if (HasCompilerGeneratedAttribute(type) ||
            type.Name.StartsWith("<>", StringComparison.Ordinal))
        {
            return;
        }

        foreach (var field in type.Fields)
        {
            if (field.IsStatic &&
                !field.IsLiteral &&
                !field.IsInitOnly &&
                !HasCompilerGeneratedAttribute(field))
            {
                violations.Add(new(
                    ArchitectureRule.MutableGlobalState,
                    field.FullName));
            }
        }
    }

    private static bool HasCompilerGeneratedAttribute(ICustomAttributeProvider provider) =>
        provider.CustomAttributes.Any(attribute =>
            attribute.AttributeType.FullName == typeof(CompilerGeneratedAttribute).FullName);

    private static void FindServiceLocation(
        TypeDefinition type,
        List<ArchitectureViolation> violations)
    {
        foreach (var method in type.Methods.Where(method => method.HasBody))
        {
            foreach (var instruction in method.Body.Instructions)
            {
                if (instruction.OpCode.Code is not (Code.Call or Code.Callvirt) ||
                    instruction.Operand is not MethodReference calledMethod)
                {
                    continue;
                }

                var declaringType = calledMethod.DeclaringType.FullName;
                var isServiceProviderCall =
                    declaringType == typeof(IServiceProvider).FullName &&
                    calledMethod.Name == nameof(IServiceProvider.GetService);
                var isExtensionCall =
                    declaringType == "Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions" &&
                    calledMethod.Name is "GetService" or "GetRequiredService";

                if (isServiceProviderCall || isExtensionCall)
                {
                    violations.Add(new(
                        ArchitectureRule.ServiceLocation,
                        method.FullName));
                }
            }
        }
    }

    private static bool IsPubliclyVisible(TypeDefinition type) =>
        type.IsPublic ||
        (type.IsNestedPublic && type.DeclaringType is not null && IsPubliclyVisible(type.DeclaringType));
}

internal static class ArchitectureRule
{
    public const string ForbiddenReference = "forbidden-reference";
    public const string PublicOpenTelemetryApi = "public-opentelemetry-api";
    public const string ServiceLocation = "service-location";
    public const string MutableGlobalState = "mutable-global-state";
}

internal enum ArchitectureLayer
{
    Core,
    Hosting,
    AspNetCore,
    Mcp,
}

internal sealed record ArchitectureViolation(string RuleId, string Location);
