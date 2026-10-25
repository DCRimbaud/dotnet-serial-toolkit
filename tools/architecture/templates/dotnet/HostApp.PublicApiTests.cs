// SPDX-License-Identifier: MIT
// Author: Xue Weisen <xueweisen96@gmail.com>
// GitHub: https://github.com/DCRimbaud
using System.Reflection;
using System.Text;
using Xunit;
using HostApp.Application;
using HostApp.Presentation.Core.Logging;

namespace HostApp.Tests;

/// <summary>
/// Public API baseline gate using only xUnit and System.Reflection.
/// Copy PublicApiBaseline.txt to the test output directory before running.
/// Covers Domain, Application, and Presentation.Core; excludes Infrastructure, Presentation, and Profiles.
/// </summary>
public sealed class HostAppPublicApiTests
{
    /// <summary>
    /// Public APIs in Domain, Application, and Presentation.Core must match the reviewed baseline.
    /// Review the actual output and update the baseline only for an intentional contract change.
    /// </summary>
    [Fact]
    public void PublicApiMustMatchBaseline()
    {
        var assemblies = new[]
        {
            typeof(HostApp.Domain.Logging.LogEntry).Assembly,
            typeof(IDeviceSessionFactory).Assembly,
            typeof(SessionLogService).Assembly
        };

        var actual = BuildSurface(assemblies);
        var baselinePath = Path.Combine(AppContext.BaseDirectory, "PublicApiBaseline.txt");
        Assert.True(File.Exists(baselinePath), $"Public API baseline not found: {baselinePath}");

        var expected = Normalize(File.ReadAllText(baselinePath));
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            var actualPath = Path.Combine(AppContext.BaseDirectory, "PublicApiBaseline.actual.txt");
            File.WriteAllText(actualPath, actual + "\n", new UTF8Encoding(false));
            Assert.Fail($"Public API changed. Review the diff and update the baseline explicitly. Actual: {actualPath}");
        }
    }

    /// <summary>
    /// Enumerate exported types and collect public constructors, methods, properties, fields, and events.
    /// Sort output to keep API diffs independent of reflection enumeration order.
    /// </summary>
    private static string BuildSurface(IEnumerable<Assembly> assemblies)
    {
        var lines = new List<string>();
        foreach (var assembly in assemblies)
        {
            foreach (var type in assembly.GetExportedTypes().OrderBy(type => type.FullName, StringComparer.Ordinal))
            {
                lines.Add($"T:{FormatType(type)}");
                AddMembers(lines, type);
            }
        }

        return string.Join('\n', lines.OrderBy(line => line, StringComparer.Ordinal));
    }

    /// <summary>
    /// Record declared members only; inherited members are excluded to avoid duplicate signatures.
    /// Stable member prefixes make type, constructor, method, property, field, and event changes easy to review.
    /// </summary>
    private static void AddMembers(List<string> lines, Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        foreach (var constructor in type.GetConstructors(flags))
        {
            lines.Add($"C:{FormatType(type)}({FormatParameters(constructor.GetParameters())})");
        }

        foreach (var method in type.GetMethods(flags))
        {
            if (method.IsSpecialName)
            {
                continue;
            }

            lines.Add($"M:{method.Name}({FormatParameters(method.GetParameters())}):{FormatType(method.ReturnType)}");
        }

        foreach (var property in type.GetProperties(flags))
        {
            lines.Add($"P:{property.Name}:{FormatType(property.PropertyType)}");
        }

        foreach (var field in type.GetFields(flags))
        {
            lines.Add($"F:{field.Name}:{FormatType(field.FieldType)}");
        }

        foreach (var eventInfo in type.GetEvents(flags))
        {
            lines.Add($"E:{eventInfo.Name}:{FormatType(eventInfo.EventHandlerType ?? typeof(void))}");
        }
    }

    /// <summary>
    /// Format parameter types in order; parameter names and default values are excluded from the baseline.
    /// </summary>
    private static string FormatParameters(IEnumerable<ParameterInfo> parameters) =>
        string.Join(',', parameters.Select(parameter => FormatType(parameter.ParameterType)));

    /// <summary>
    /// Format stable type text, expanding arrays, by-ref types, and generic arguments.
    /// This format is optimized for API baselines, not source-code reconstruction.
    /// </summary>
    private static string FormatType(Type type)
    {
        if (type.IsByRef)
        {
            return FormatType(type.GetElementType() ?? typeof(void)) + "&";
        }

        if (type.IsArray)
        {
            return FormatType(type.GetElementType() ?? typeof(void)) + "[]";
        }

        if (!type.IsGenericType)
        {
            return type.FullName ?? type.Name;
        }

        var definition = type.GetGenericTypeDefinition();
        var name = (definition.FullName ?? definition.Name).Split('`')[0];
        return $"{name}<{string.Join(',', type.GetGenericArguments().Select(FormatType))}>";
    }

    /// <summary>
    /// Normalize line endings and trailing whitespace across platforms.
    /// </summary>
    private static string Normalize(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n');
}
