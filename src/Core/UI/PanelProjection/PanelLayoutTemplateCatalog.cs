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

    private static readonly HashSet<string> ControlFields = new(StringComparer.Ordinal)
    {
        "type", "class", "text", "bind", "prefix", "current", "max", "showWhen",
        "children", "gap", "align", "justify", "width", "height", "widthBind", "heightBind",
        "fontSize", "bold", "textRunsBind", "objectFit", "visibleWhenNotEmpty", "classBind",
        "colorBind", "backgroundBind"
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

        return new PanelLayoutTemplate(id, bindings, ParseControl(id, rootControl, bindings));
    }

    private static PanelLayoutControl ParseControl(
        string templateId,
        JsonObject controlObject,
        IReadOnlySet<string> bindings)
    {
        RejectUnknownFields(controlObject, ControlFields, $"panel layout template '{templateId}' control");
        string typeText = RequireString(controlObject, "type", $"panel layout template '{templateId}' control");
        PanelLayoutControlType type = typeText switch
        {
            "label" => PanelLayoutControlType.Label,
            "progressBar" => PanelLayoutControlType.ProgressBar,
            "badge" => PanelLayoutControlType.Badge,
            "row" => PanelLayoutControlType.Row,
            "column" => PanelLayoutControlType.Column,
            "image" => PanelLayoutControlType.Image,
            "richText" => PanelLayoutControlType.RichText,
            "repeater" => PanelLayoutControlType.Repeater,
            _ => throw new InvalidOperationException(
                $"Panel layout template '{templateId}' control type '{typeText}' is unknown.")
        };

        string? bind = OptionalString(controlObject, "bind");
        string? current = OptionalString(controlObject, "current");
        string? max = OptionalString(controlObject, "max");
        string? widthBind = OptionalString(controlObject, "widthBind");
        string? heightBind = OptionalString(controlObject, "heightBind");
        string? textRunsBind = OptionalString(controlObject, "textRunsBind");
        string? visibleWhenNotEmpty = OptionalString(controlObject, "visibleWhenNotEmpty");
        string? classBind = OptionalString(controlObject, "classBind");
        string? colorBind = OptionalString(controlObject, "colorBind");
        string? backgroundBind = OptionalString(controlObject, "backgroundBind");

        ValidateBinding(templateId, bind, bindings);
        ValidateBinding(templateId, current, bindings);
        ValidateBinding(templateId, max, bindings);
        ValidateBinding(templateId, widthBind, bindings);
        ValidateBinding(templateId, heightBind, bindings);
        ValidateBinding(templateId, textRunsBind, bindings);
        ValidateBinding(templateId, visibleWhenNotEmpty, bindings);
        ValidateBinding(templateId, classBind, bindings);
        ValidateBinding(templateId, colorBind, bindings);
        ValidateBinding(templateId, backgroundBind, bindings);

        IReadOnlyList<PanelLayoutControl> children = ParseChildren(templateId, controlObject, bindings);
        if (type is PanelLayoutControlType.Row or PanelLayoutControlType.Column or PanelLayoutControlType.Repeater)
        {
            if (children.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Panel layout template '{templateId}' {typeText} control requires children.");
            }
        }
        else if (children.Count > 0)
        {
            throw new InvalidOperationException(
                $"Panel layout template '{templateId}' {typeText} control cannot declare children.");
        }

        if (type == PanelLayoutControlType.Image && string.IsNullOrWhiteSpace(bind))
        {
            throw new InvalidOperationException(
                $"Panel layout template '{templateId}' image control requires bind.");
        }

        if (type == PanelLayoutControlType.RichText &&
            (string.IsNullOrWhiteSpace(bind) || string.IsNullOrWhiteSpace(textRunsBind)))
        {
            throw new InvalidOperationException(
                $"Panel layout template '{templateId}' richText control requires bind and textRunsBind.");
        }

        if (type == PanelLayoutControlType.Repeater && string.IsNullOrWhiteSpace(bind))
        {
            throw new InvalidOperationException(
                $"Panel layout template '{templateId}' repeater control requires bind.");
        }

        if (type == PanelLayoutControlType.ProgressBar &&
            string.IsNullOrWhiteSpace(bind) &&
            (string.IsNullOrWhiteSpace(current) || string.IsNullOrWhiteSpace(max)))
        {
            throw new InvalidOperationException(
                $"Panel layout template '{templateId}' progressBar requires bind or current/max.");
        }

        bool? showWhen = OptionalBool(controlObject, "showWhen");
        bool bold = OptionalBool(controlObject, "bold") ?? false;
        return new PanelLayoutControl(
            type,
            OptionalString(controlObject, "class"),
            OptionalString(controlObject, "text"),
            bind,
            OptionalString(controlObject, "prefix"),
            current,
            max,
            showWhen,
            children: children,
            gap: OptionalNonNegativeFloat(controlObject, "gap", templateId),
            align: OptionalString(controlObject, "align"),
            justify: OptionalString(controlObject, "justify"),
            width: OptionalPositiveFloat(controlObject, "width", templateId),
            height: OptionalPositiveFloat(controlObject, "height", templateId),
            widthBind: widthBind,
            heightBind: heightBind,
            fontSize: OptionalPositiveFloat(controlObject, "fontSize", templateId),
            bold: bold,
            textRunsBind: textRunsBind,
            objectFit: OptionalString(controlObject, "objectFit"),
            visibleWhenNotEmpty: visibleWhenNotEmpty,
            classBind: classBind,
            colorBind: colorBind,
            backgroundBind: backgroundBind);
    }

    private static IReadOnlyList<PanelLayoutControl> ParseChildren(
        string templateId,
        JsonObject controlObject,
        IReadOnlySet<string> bindings)
    {
        if (controlObject["children"] is null)
        {
            return Array.Empty<PanelLayoutControl>();
        }

        if (controlObject["children"] is not JsonArray childrenNode)
        {
            throw new InvalidOperationException(
                $"Panel layout template '{templateId}' control children must be an array.");
        }

        var children = new List<PanelLayoutControl>(childrenNode.Count);
        foreach (JsonNode? childNode in childrenNode)
        {
            if (childNode is not JsonObject childObject)
            {
                throw new InvalidOperationException(
                    $"Panel layout template '{templateId}' child control must be an object.");
            }

            children.Add(ParseControl(templateId, childObject, bindings));
        }

        return children;
    }

    private static void ValidateBinding(
        string templateId,
        string? binding,
        IReadOnlySet<string> bindings)
    {
        if (!string.IsNullOrWhiteSpace(binding) && !bindings.Contains(binding))
        {
            throw new InvalidOperationException(
                $"Panel layout template '{templateId}' references undeclared binding '{binding}'.");
        }
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

    private static bool? OptionalBool(JsonObject node, string field)
    {
        if (node[field] is null)
        {
            return null;
        }

        if (node[field] is JsonValue value && value.TryGetValue<bool>(out bool result))
        {
            return result;
        }

        throw new InvalidOperationException($"Panel layout control field '{field}' must be a bool.");
    }

    private static float? OptionalPositiveFloat(JsonObject node, string field, string templateId)
    {
        return OptionalFloat(node, field, templateId, positive: true);
    }

    private static float? OptionalNonNegativeFloat(JsonObject node, string field, string templateId)
    {
        return OptionalFloat(node, field, templateId, positive: false);
    }

    private static float? OptionalFloat(
        JsonObject node,
        string field,
        string templateId,
        bool positive)
    {
        if (node[field] is null)
        {
            return null;
        }

        if (node[field] is not JsonValue value ||
            !value.TryGetValue<double>(out double raw) ||
            (positive ? raw <= 0d : raw < 0d))
        {
            string requirement = positive ? "positive" : "non-negative";
            throw new InvalidOperationException(
                $"Panel layout template '{templateId}' field '{field}' must be {requirement}.");
        }

        return (float)raw;
    }
}
