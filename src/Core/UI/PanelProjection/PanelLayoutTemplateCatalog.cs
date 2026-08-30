using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ludots.Core.UI.PanelProjection;

public sealed class PanelLayoutTemplate
{
    public PanelLayoutTemplate(
        string id,
        IReadOnlySet<string> bindings,
        PanelLayoutControl root)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Panel layout template id is required.", nameof(id));
        }

        Id = id.Trim();
        Bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
        Root = root ?? throw new ArgumentNullException(nameof(root));
    }

    public string Id { get; }
    public IReadOnlySet<string> Bindings { get; }
    public PanelLayoutControl Root { get; }
}

public sealed class PanelLayoutTemplateCatalog
{
    private readonly Dictionary<string, PanelLayoutTemplate> _templates =
        new(StringComparer.Ordinal);

    public void Register(PanelLayoutTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        if (!_templates.TryAdd(template.Id, template))
        {
            throw new InvalidOperationException(
                $"Panel layout template '{template.Id}' is already registered.");
        }
    }

    public PanelLayoutTemplate Require(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || !_templates.TryGetValue(id, out PanelLayoutTemplate? template))
        {
            throw new InvalidOperationException($"Panel layout template '{id}' is not registered.");
        }

        return template;
    }
}

public static class PanelLayoutTemplateLoader
{
    private static readonly HashSet<string> TemplateFields = new(StringComparer.Ordinal)
    {
        "id", "bindings", "root"
    };

    public static PanelLayoutTemplateCatalog LoadCatalog(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        JsonNode root = JsonNode.Parse(stream)
            ?? throw new InvalidOperationException("Panel layout template document parsed to null.");
        if (root is not JsonArray templates)
        {
            throw new InvalidOperationException("Panel layout template document must be an array.");
        }

        var catalog = new PanelLayoutTemplateCatalog();
        for (int i = 0; i < templates.Count; i++)
        {
            if (templates[i] is not JsonObject templateObject)
            {
                throw new InvalidOperationException(
                    $"Panel layout template at index {i} must be an object.");
            }

            catalog.Register(LoadTemplate(templateObject, i));
        }

        return catalog;
    }

    private static PanelLayoutTemplate LoadTemplate(JsonObject templateObject, int index)
    {
        RejectUnknownFields(templateObject, TemplateFields, $"panel layout template at index {index}");
        string id = RequireString(templateObject, "id", $"panel layout template at index {index}");
        if (templateObject["bindings"] is not JsonArray bindingsNode || bindingsNode.Count == 0)
        {
            throw new InvalidOperationException(
                $"Panel layout template '{id}' requires a non-empty bindings array.");
        }

        var bindings = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonNode? bindingNode in bindingsNode)
        {
            if (bindingNode is not JsonValue value ||
                !value.TryGetValue<string>(out string? binding) ||
                string.IsNullOrWhiteSpace(binding))
            {
                throw new InvalidOperationException(
                    $"Panel layout template '{id}' bindings must be non-empty strings.");
            }

            if (!bindings.Add(binding))
            {
                throw new InvalidOperationException(
                    $"Panel layout template '{id}' declares duplicate binding '{binding}'.");
            }
        }

        if (templateObject["root"] is not JsonObject rootControl)
        {
            throw new InvalidOperationException(
                $"Panel layout template '{id}' requires a root control object.");
        }

        return new PanelLayoutTemplate(
            id,
            bindings,
            PanelLayoutControlJsonParser.Parse(
                rootControl,
                PanelLayoutControlJsonContext.ForLayoutTemplate(id, bindings)));
    }

    private static void RejectUnknownFields(
        JsonObject node,
        IReadOnlySet<string> allowed,
        string context)
    {
        foreach ((string field, _) in node)
        {
            if (!allowed.Contains(field))
            {
                throw new InvalidOperationException($"{context} has unknown field '{field}'.");
            }
        }
    }

    private static string RequireString(JsonObject node, string field, string context)
    {
        string? value = OptionalString(node, field);
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException($"{context} requires non-empty '{field}'.");
    }

    private static string? OptionalString(JsonObject node, string field)
    {
        return node[field] is JsonValue value && value.TryGetValue<string>(out string? text)
            ? text?.Trim()
            : null;
    }

}
