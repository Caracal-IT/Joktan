using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Joktan.Containers;

/// <summary>A container of a compose project, as <c>compose ps --all --format json</c> reports it.</summary>
public sealed record ComposeContainer(string Service, string Name, string State, string Status, string Ports,
    string Image = "", string Health = "")
{
    public bool IsRunning => State.Equals("running", StringComparison.OrdinalIgnoreCase);

    public bool IsPaused => State.Equals("paused", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Reads <c>compose ps --format json</c>. Docker's compose writes one object per line, older
    /// versions and podman-compose an array; podman-compose uses <c>podman ps</c>'s field names.
    /// </summary>
    public static List<ComposeContainer> Parse(string json)
    {
        var text = json.Trim();
        if (text.Length == 0)
            return [];

        var elements = new List<JsonElement>();
        if (text.StartsWith('['))
        {
            using var document = JsonDocument.Parse(text);
            elements.AddRange(document.RootElement.EnumerateArray().Select(e => e.Clone()));
        }
        else
        {
            foreach (var line in text.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith('{')))
            {
                using var document = JsonDocument.Parse(line);
                elements.Add(document.RootElement.Clone());
            }
        }

        return elements.Select(From).ToList();
    }

    private static ComposeContainer From(JsonElement e)
    {
        var name = Text(e, "Name") ?? Text(e, "Names") ?? "";
        var service = Text(e, "Service") ?? Label(e, "com.docker.compose.service") ?? name;
        var state = Text(e, "State") ?? "";
        var status = Text(e, "Status") ?? state;
        return new ComposeContainer(service, name, state, status, PortsOf(e), Text(e, "Image") ?? "", Text(e, "Health") ?? "");
    }

    private static string PortsOf(JsonElement e)
    {
        if (e.TryGetProperty("Publishers", out var publishers) && publishers.ValueKind == JsonValueKind.Array)
        {
            // Ports the image exposes are listed too, with no published port.
            return string.Join(", ", publishers.EnumerateArray()
                .Where(p => Number(p, "PublishedPort") > 0)
                .Select(p => $"{Number(p, "PublishedPort")}→{Number(p, "TargetPort")}")
                .Distinct());
        }

        // podman ps: [{ "host_port": 1883, "container_port": 1883, ... }]
        if (e.TryGetProperty("Ports", out var ports) && ports.ValueKind == JsonValueKind.Array)
        {
            return string.Join(", ", ports.EnumerateArray()
                .Select(p => (Host: Number(p, "host_port"), Container: Number(p, "container_port"), Range: Math.Max(1, Number(p, "range"))))
                .Where(p => p.Host > 0)
                .Select(p => p.Range > 1
                    ? $"{p.Host}-{p.Host + p.Range - 1}→{p.Container}-{p.Container + p.Range - 1}"
                    : $"{p.Host}→{p.Container}"));
        }

        return Text(e, "Ports") ?? "";
    }

    private static string? Text(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var value))
            return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Array => value.EnumerateArray().Select(v => v.ToString()).FirstOrDefault(),
            _ => null,
        };
    }

    private static string? Label(JsonElement e, string name)
        => e.TryGetProperty("Labels", out var labels) && labels.ValueKind == JsonValueKind.Object && labels.TryGetProperty(name, out var value)
            ? value.GetString()
            : null;

    private static int Number(JsonElement e, string name)
        => e.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : 0;
}
