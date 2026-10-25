// SPDX-License-Identifier: MIT
// Author: Xue Weisen <xueweisen96@gmail.com>
// GitHub: https://github.com/DCRimbaud
using System.Reflection;
using Xunit;
using HostApp.Application;
using HostApp.Infrastructure.Serial;
using HostApp.Presentation.Core.Logging;
using HostApp.Presentation.ViewModels;
using HostApp.Profiles.DeviceProfile;

namespace HostApp.Tests;

/// <summary>
/// Assembly-level architecture gate using only xUnit and System.Reflection.
/// The test project must directly reference every project under test.
/// </summary>
public sealed class HostAppArchitectureTests
{
    private static readonly Assembly DomainAssembly =
        typeof(HostApp.Domain.Logging.LogEntry).Assembly;

    private static readonly Assembly ApplicationAssembly =
        typeof(IDeviceSessionFactory).Assembly;

    private static readonly Assembly InfrastructureAssembly =
        typeof(SerialDeviceSessionFactory).Assembly;

    private static readonly Assembly PresentationCoreAssembly =
        typeof(SessionLogService).Assembly;

    private static readonly Assembly PresentationAssembly =
        typeof(ShellViewModel).Assembly;

    private static readonly Assembly DeviceProfileAssembly =
        typeof(DeviceProfile).Assembly;

    /// <summary>
    /// Domain must not reference Application, Infrastructure, Presentation, Profiles, UI, or system I/O.
    /// Failure usually means a project reference, using directive, or public signature crossed the boundary.
    /// </summary>
    [Fact]
    public void DomainMustNotReferenceOuterLayersOrIo()
    {
        AssertNoReferences(
            DomainAssembly,
            "HostApp.Application",
            "HostApp.Infrastructure",
            "HostApp.Presentation",
            "HostApp.Profiles",
            "Avalonia",
            "System.IO.Ports",
            "System.Management",
            "System.Net.Http",
            "System.Text.Json");
    }

    /// <summary>
    /// Application must not depend on Infrastructure, Presentation, Profiles, UI, or system I/O.
    /// Application use cases must reach the outside world only through ports.
    /// </summary>
    [Fact]
    public void ApplicationMustNotReferenceInfrastructurePresentationOrProfiles()
    {
        AssertNoReferences(
            ApplicationAssembly,
            "HostApp.Infrastructure",
            "HostApp.Presentation",
            "HostApp.Profiles",
            "Avalonia",
            "System.IO.Ports",
            "System.Management",
            "System.Net.Http");
    }

    /// <summary>
    /// Infrastructure must not reference Presentation or a concrete device Profile.
    /// Adapters must implement inner ports without leaking device branches into generic transport code.
    /// </summary>
    [Fact]
    public void InfrastructureMustNotReferencePresentationOrConcreteProfiles()
    {
        AssertNoReferences(
            InfrastructureAssembly,
            "HostApp.Presentation",
            "HostApp.Profiles.BasicProfile",
            "HostApp.Profiles.DeviceProfile",
            "Avalonia");
    }

    /// <summary>
    /// Presentation.Core must not reference Avalonia, Infrastructure, or system I/O.
    /// ViewModels and presenters must remain testable without a window or real device.
    /// </summary>
    [Fact]
    public void PresentationCoreMustNotReferenceAvaloniaOrInfrastructure()
    {
        AssertNoReferences(
            PresentationCoreAssembly,
            "HostApp.Infrastructure",
            "Avalonia",
            "System.IO.Ports",
            "System.Management",
            "System.Net.Http");
    }

    /// <summary>
    /// Presentation must not reference Infrastructure directly.
    /// UI code must consume Presentation.Core and port contracts instead of constructing adapters.
    /// </summary>
    [Fact]
    public void PresentationMustNotReferenceInfrastructureDirectly()
    {
        AssertNoReferences(
            PresentationAssembly,
            "HostApp.Infrastructure",
            "System.IO.Ports",
            "System.Management",
            "System.Net.Http");
    }

    /// <summary>
    /// Profiles must not reference Infrastructure or Presentation.
    /// Device capability descriptions must remain independent of transport and UI details.
    /// </summary>
    [Fact]
    public void ProfileMustNotReferenceInfrastructureOrPresentation()
    {
        AssertNoReferences(
            DeviceProfileAssembly,
            "HostApp.Infrastructure",
            "HostApp.Presentation",
            "Avalonia");
    }

    /// <summary>
    /// Domain public signatures must not expose file, serial, network, JSON, logging-framework, or UI types.
    /// Public DTOs and services must not carry external I/O objects across the domain boundary.
    /// </summary>
    [Fact]
    public void DomainPublicSurfaceMustNotExposeIoOrSerialTypes()
    {
        AssertPublicSurfaceExcludes(
            DomainAssembly,
            "System.IO",
            "System.IO.Ports",
            "System.Net.Http",
            "System.Text.Json",
            "Microsoft.Extensions.Logging",
            "Avalonia");
    }

    /// <summary>
    /// Application public signatures must not expose Infrastructure, Presentation, serial, HTTP, or UI types.
    /// Port contracts must remain stable and replaceable.
    /// </summary>
    [Fact]
    public void ApplicationPublicSurfaceMustNotExposeInfrastructureOrUiTypes()
    {
        AssertPublicSurfaceExcludes(
            ApplicationAssembly,
            "HostApp.Infrastructure",
            "HostApp.Presentation",
            "System.IO.Ports",
            "System.Net.Http",
            "Avalonia");
    }

    /// <summary>
    /// ViewModel public signatures must not expose file, serial, HTTP, or Avalonia types.
    /// Presentation state must remain testable without concrete I/O or UI-framework types.
    /// </summary>
    [Fact]
    public void ViewModelPublicSurfaceMustNotExposeFileOrSerialTypes()
    {
        var viewModelTypes = PresentationAssembly
            .GetExportedTypes()
            .Where(type => type.Namespace?.Contains(".ViewModels", StringComparison.Ordinal) == true)
            .ToArray();

        var violations = viewModelTypes
            .SelectMany(SignatureTypes)
            .Where(type => IsForbidden(type, "System.IO", "System.IO.Ports", "System.Net.Http", "Avalonia"))
            .Select(FormatType)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"ViewModel public API exposes forbidden types: {string.Join(", ", violations)}");
    }

    /// <summary>
    /// Check direct assembly references against forbidden prefixes.
    /// A failure means a real project reference or using directive crossed the dependency direction.
    /// </summary>
    private static void AssertNoReferences(Assembly source, params string[] forbiddenPrefixes)
    {
        var references = source
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();

        var violations = references
            .Where(reference => forbiddenPrefixes.Any(prefix =>
                reference.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(reference => reference, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"{source.GetName().Name} references forbidden assemblies: {string.Join(", ", violations)}");
    }

    /// <summary>
    /// Scan all exported public signatures for forbidden namespaces.
    /// Use the reported full type name to trace the constructor, method, property, field, or event.
    /// </summary>
    private static void AssertPublicSurfaceExcludes(Assembly assembly, params string[] forbiddenNamespaces)
    {
        var violations = assembly
            .GetExportedTypes()
            .SelectMany(SignatureTypes)
            .Where(type => IsForbidden(type, forbiddenNamespaces))
            .Select(FormatType)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            $"{assembly.GetName().Name} public API exposes forbidden types: {string.Join(", ", violations)}");
    }

    /// <summary>
    /// Enumerate signature types from base types, interfaces, constructors, methods, properties, fields, and events.
    /// Collect declarations only; method bodies are not part of the public contract.
    /// </summary>
    private static IEnumerable<Type> SignatureTypes(Type type)
    {
        if (type.BaseType is { } baseType)
        {
            yield return baseType;
        }

        foreach (var implementedInterface in type.GetInterfaces())
        {
            yield return implementedInterface;
        }

        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (var constructor in type.GetConstructors(flags))
        {
            foreach (var parameter in constructor.GetParameters())
            {
                yield return parameter.ParameterType;
            }
        }

        foreach (var method in type.GetMethods(flags))
        {
            if (method.IsSpecialName)
            {
                continue;
            }

            yield return method.ReturnType;
            foreach (var parameter in method.GetParameters())
            {
                yield return parameter.ParameterType;
            }
        }

        foreach (var property in type.GetProperties(flags))
        {
            yield return property.PropertyType;
        }

        foreach (var field in type.GetFields(flags))
        {
            yield return field.FieldType;
        }

        foreach (var eventInfo in type.GetEvents(flags))
        {
            yield return eventInfo.EventHandlerType ?? typeof(void);
        }
    }

    /// <summary>
    /// Recursively inspect by-ref, array, pointer, and generic argument types.
    /// Preserve outer type information so nested forbidden element types are not hidden.
    /// </summary>
    private static bool IsForbidden(Type type, params string[] forbiddenNamespaces)
    {
        if (type.IsByRef || type.IsArray || type.IsPointer)
        {
            return type.GetElementType() is { } element && IsForbidden(element, forbiddenNamespaces);
        }

        if (type.IsGenericType)
        {
            if (forbiddenNamespaces.Any(ns => MatchesNamespace(type, ns)) ||
                type.GetGenericArguments().Any(argument => IsForbidden(argument, forbiddenNamespaces)))
            {
                return true;
            }
        }

        return forbiddenNamespaces.Any(ns => MatchesNamespace(type, ns));
    }

    /// <summary>
    /// Classify types by namespace prefix for both assembly and signature gates.
    /// Use ordinal comparison for deterministic results across machines and cultures.
    /// </summary>
    private static bool MatchesNamespace(Type type, string forbiddenNamespace) =>
        type.Namespace?.StartsWith(forbiddenNamespace, StringComparison.Ordinal) == true;

    /// <summary>
    /// Format a stable, readable full type name for failure diagnostics.
    /// </summary>
    private static string FormatType(Type type) => type.FullName ?? type.Name;
}
