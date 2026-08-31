using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace Ludots.Core.UI.PanelProjection;

internal sealed class PanelLayoutControlJsonContext
{
    private static readonly HashSet<string> PanelFields = new(StringComparer.Ordinal)
    {
        "type", "class", "text", "bind", "prefix", "current", "max", "showWhen",
        "viewportHeight", "itemExtent", "virtualize", "overscan", "present",
        "columns", "aggregate", "src", "width", "height"
    };

    private static readonly HashSet<string> LayoutTemplateFields = new(StringComparer.Ordinal)
    {
        "type", "class", "text", "bind", "prefix", "current", "max", "showWhen",
        "children", "gap", "align", "justify", "width", "height", "widthBind", "heightBind",
        "fontSize", "bold", "textRunsBind", "objectFit", "visibleWhenNotEmpty", "classBind",
        "colorBind", "backgroundBind", "viewportHeight", "itemExtent", "virtualize", "overscan"
    };

    private static readonly HashSet<PanelLayoutControlType> PanelTypes = new()
    {
        PanelLayoutControlType.Label,
        PanelLayoutControlType.ProgressBar,
        PanelLayoutControlType.Badge,
        PanelLayoutControlType.List,
        PanelLayoutControlType.Image
    };

    private static readonly HashSet<PanelLayoutControlType> LayoutTemplateTypes = new()
    {
        PanelLayoutControlType.Label,
        PanelLayoutControlType.ProgressBar,
        PanelLayoutControlType.Badge,
        PanelLayoutControlType.Row,
        PanelLayoutControlType.Column,
        PanelLayoutControlType.Image,
        PanelLayoutControlType.RichText,
        PanelLayoutControlType.Repeater
    };

    private PanelLayoutControlJsonContext(
        string templateId,
        string description,
        IReadOnlySet<string> fields,
        IReadOnlySet<PanelLayoutControlType> types,
        IReadOnlySet<string> bindings,
        IReadOnlySet<string> collections,
        bool allowRatioProgressBinding,
        bool allowNonImageDimensions)
    {
        TemplateId = templateId;
        Description = description;
        Fields = fields;
        Types = types;
        Bindings = bindings;
        Collections = collections;
        AllowRatioProgressBinding = allowRatioProgressBinding;
        AllowNonImageDimensions = allowNonImageDimensions;
    }

    public string TemplateId { get; }
    public string Description { get; }
    public IReadOnlySet<string> Fields { get; }
    public IReadOnlySet<PanelLayoutControlType> Types { get; }
    public IReadOnlySet<string> Bindings { get; }
    public IReadOnlySet<string> Collections { get; }
    public bool AllowRatioProgressBinding { get; }
    public bool AllowNonImageDimensions { get; }

    public static PanelLayoutControlJsonContext ForPanelTemplate(
        string templateId,
        IReadOnlySet<string> bindings,
        IReadOnlySet<string> collections)
        => new(
            templateId,
            $"panel template '{templateId}' layout control",
            PanelFields,
            PanelTypes,
            bindings,
            collections,
            allowRatioProgressBinding: false,
            allowNonImageDimensions: false);

    public static PanelLayoutControlJsonContext ForLayoutTemplate(
        string templateId,
        IReadOnlySet<string> bindings)
        => new(
            templateId,
            $"panel layout template '{templateId}' control",
            LayoutTemplateFields,
            LayoutTemplateTypes,
            bindings,
            new HashSet<string>(StringComparer.Ordinal),
            allowRatioProgressBinding: true,
            allowNonImageDimensions: true);

    public void ValidateBinding(string field, string? binding, PanelLayoutControlType type)
    {
        if (string.IsNullOrWhiteSpace(binding))
        {
            return;
        }

        IReadOnlySet<string> allowed = field == "bind" && type == PanelLayoutControlType.List
            ? Collections
            : Bindings;
        if (!allowed.Contains(binding))
        {
            throw new InvalidOperationException(
                $"{Description} references undeclared {field} binding '{binding}'.");
        }
    }
}

internal static class PanelLayoutControlJsonParser
{
    private static readonly HashSet<string> AggregateFields = new(StringComparer.Ordinal) { "count" };
    private static readonly HashSet<string> AggregateCountFields = new(StringComparer.Ordinal) { "from", "prefix" };

    public static PanelLayoutControl Parse(JsonNode? node, PanelLayoutControlJsonContext context)
    {
        if (node is not JsonObject control)
        {
            throw new InvalidOperationException($"{context.Description} must be an object.");
        }

        RejectUnknownFields(control, context.Fields, context.Description);
        string typeText = RequireString(control, "type", context);
        PanelLayoutControlType type = ParseType(typeText, context);

        string? bind = OptionalString(control, "bind", context);
        string? current = OptionalString(control, "current", context);
        string? max = OptionalString(control, "max", context);
        string? widthBind = OptionalString(control, "widthBind", context);
        string? heightBind = OptionalString(control, "heightBind", context);
        string? textRunsBind = OptionalString(control, "textRunsBind", context);
        string? visibleWhenNotEmpty = OptionalString(control, "visibleWhenNotEmpty", context);
        string? classBind = OptionalString(control, "classBind", context);
        string? colorBind = OptionalString(control, "colorBind", context);
        string? backgroundBind = OptionalString(control, "backgroundBind", context);

        context.ValidateBinding("bind", bind, type);
        context.ValidateBinding("current", current, type);
        context.ValidateBinding("max", max, type);
        context.ValidateBinding("widthBind", widthBind, type);
        context.ValidateBinding("heightBind", heightBind, type);
        context.ValidateBinding("textRunsBind", textRunsBind, type);
        context.ValidateBinding("visibleWhenNotEmpty", visibleWhenNotEmpty, type);
        context.ValidateBinding("classBind", classBind, type);
        context.ValidateBinding("colorBind", colorBind, type);
        context.ValidateBinding("backgroundBind", backgroundBind, type);

        IReadOnlyList<PanelLayoutControl> children = ParseChildren(control, context);
        ValidateChildren(type, typeText, children, context);

        string? text = OptionalString(control, "text", context);
        string? src = OptionalString(control, "src", context);
        float? width = OptionalPositiveFloat(control, "width", context);
        float? height = OptionalPositiveFloat(control, "height", context);
        ValidateRequiredBindings(
            type,
            text,
            bind,
            current,
            max,
            textRunsBind,
            src,
            width,
            height,
            widthBind,
            heightBind,
            context);

        float? viewportHeight = OptionalPositiveFloat(control, "viewportHeight", context);
        float? itemExtent = OptionalPositiveFloat(control, "itemExtent", context);
        bool virtualize = OptionalBool(control, "virtualize", context) ?? false;
        int overscan = OptionalNonNegativeInt(control, "overscan", context) ?? 2;
        PanelPresentMode present = ParsePresent(control, type, bind, context);
        int? columns = ParseColumns(control, type, bind, context);
        PanelAggregateCountSpec? aggregate = ParseAggregate(control, type, bind, context);
        ValidateListOptions(
            control,
            type,
            bind,
            virtualize,
            ref itemExtent,
            viewportHeight,
            present,
            columns,
            aggregate,
            context);

        return new PanelLayoutControl(
            type,
            OptionalString(control, "class", context),
            text,
            bind,
            OptionalString(control, "prefix", context),
            current,
            max,
            OptionalBool(control, "showWhen", context),
            viewportHeight,
            itemExtent,
            virtualize,
            overscan,
            present,
            columns,
            aggregate,
            src,
            width,
            height,
            children,
            OptionalNonNegativeFloat(control, "gap", context),
            OptionalString(control, "align", context),
            OptionalString(control, "justify", context),
            widthBind,
            heightBind,
            OptionalPositiveFloat(control, "fontSize", context),
            OptionalBool(control, "bold", context) ?? false,
            textRunsBind,
            OptionalString(control, "objectFit", context),
            visibleWhenNotEmpty,
            classBind,
            colorBind,
            backgroundBind);
    }

    private static PanelLayoutControlType ParseType(
        string typeText,
        PanelLayoutControlJsonContext context)
    {
        PanelLayoutControlType type = typeText switch
        {
            "label" => PanelLayoutControlType.Label,
            "progressBar" => PanelLayoutControlType.ProgressBar,
            "badge" => PanelLayoutControlType.Badge,
            "list" => PanelLayoutControlType.List,
            "image" => PanelLayoutControlType.Image,
            "row" => PanelLayoutControlType.Row,
            "column" => PanelLayoutControlType.Column,
            "richText" => PanelLayoutControlType.RichText,
            "repeater" => PanelLayoutControlType.Repeater,
            _ => throw new InvalidOperationException(
                $"{context.Description} type '{typeText}' is unknown.")
        };

        if (!context.Types.Contains(type))
        {
            throw new InvalidOperationException(
                $"{context.Description} does not allow type '{typeText}'.");
        }

        return type;
    }

    private static IReadOnlyList<PanelLayoutControl> ParseChildren(
        JsonObject control,
        PanelLayoutControlJsonContext context)
    {
        if (control["children"] is null)
        {
            return Array.Empty<PanelLayoutControl>();
        }

        if (control["children"] is not JsonArray childrenNode)
        {
            throw new InvalidOperationException($"{context.Description} children must be an array.");
        }

        var children = new List<PanelLayoutControl>(childrenNode.Count);
        foreach (JsonNode? child in childrenNode)
        {
            children.Add(Parse(child, context));
        }

        return children;
    }

    private static void ValidateChildren(
        PanelLayoutControlType type,
        string typeText,
        IReadOnlyList<PanelLayoutControl> children,
        PanelLayoutControlJsonContext context)
    {
        if (type is PanelLayoutControlType.Row or PanelLayoutControlType.Column or PanelLayoutControlType.Repeater)
        {
            if (children.Count == 0)
            {
                throw new InvalidOperationException(
                    $"{context.Description} {typeText} control requires children.");
            }
        }
        else if (children.Count > 0)
        {
            throw new InvalidOperationException(
                $"{context.Description} {typeText} control cannot declare children.");
        }
    }

    private static void ValidateRequiredBindings(
        PanelLayoutControlType type,
        string? text,
        string? bind,
        string? current,
        string? max,
        string? textRunsBind,
        string? src,
        float? width,
        float? height,
        string? widthBind,
        string? heightBind,
        PanelLayoutControlJsonContext context)
    {
        if (type == PanelLayoutControlType.Badge &&
            (string.IsNullOrWhiteSpace(bind) || string.IsNullOrWhiteSpace(text)))
        {
            throw new InvalidOperationException(
                $"{context.Description} badge control requires bind and text.");
        }

        if (type == PanelLayoutControlType.RichText &&
            (string.IsNullOrWhiteSpace(bind) || string.IsNullOrWhiteSpace(textRunsBind)))
        {
            throw new InvalidOperationException(
                $"{context.Description} richText control requires bind and textRunsBind.");
        }

        if (type == PanelLayoutControlType.Repeater && string.IsNullOrWhiteSpace(bind))
        {
            throw new InvalidOperationException(
                $"{context.Description} repeater control requires bind.");
        }

        if (type == PanelLayoutControlType.ProgressBar)
        {
            bool hasProgressBind = !string.IsNullOrWhiteSpace(bind);
            bool hasPair = !string.IsNullOrWhiteSpace(current) && !string.IsNullOrWhiteSpace(max);
            if ((!context.AllowRatioProgressBinding && hasProgressBind) || hasProgressBind == hasPair)
            {
                string requirement = context.AllowRatioProgressBinding
                    ? "exactly one of bind or current/max"
                    : "current/max";
                throw new InvalidOperationException(
                    $"{context.Description} progressBar requires {requirement}.");
            }
        }

        if (type != PanelLayoutControlType.Image)
        {
            if (!string.IsNullOrWhiteSpace(src) ||
                (!context.AllowNonImageDimensions &&
                    (width.HasValue ||
                     height.HasValue ||
                     !string.IsNullOrWhiteSpace(widthBind) ||
                     !string.IsNullOrWhiteSpace(heightBind))))
            {
                throw new InvalidOperationException(
                    $"{context.Description} image source and dimensions are only valid on image controls.");
            }

            return;
        }

        bool hasSrc = !string.IsNullOrWhiteSpace(src);
        bool hasBind = !string.IsNullOrWhiteSpace(bind);
        if (hasSrc == hasBind)
        {
            throw new InvalidOperationException(
                $"{context.Description} image control requires exactly one of src or bind.");
        }

        if (width.HasValue == !string.IsNullOrWhiteSpace(widthBind) ||
            height.HasValue == !string.IsNullOrWhiteSpace(heightBind))
        {
            throw new InvalidOperationException(
                $"{context.Description} image control requires exactly one width/widthBind and height/heightBind.");
        }
    }

    private static PanelPresentMode ParsePresent(
        JsonObject control,
        PanelLayoutControlType type,
        string? bind,
        PanelLayoutControlJsonContext context)
    {
        if (control["present"] is null)
        {
            return PanelPresentMode.List;
        }

        RequireListField(type, "present", context);
        return PanelPresentModes.Parse(
            RequireString(control, "present", context, $"{context.Description} list '{bind}'"),
            $"{context.Description} list '{bind}'");
    }

    private static int? ParseColumns(
        JsonObject control,
        PanelLayoutControlType type,
        string? bind,
        PanelLayoutControlJsonContext context)
    {
        if (control["columns"] is null)
        {
            return null;
        }

        RequireListField(type, "columns", context);
        if (control["columns"] is JsonValue value &&
            value.TryGetValue<int>(out int columns) &&
            columns >= 1)
        {
            return columns;
        }

        throw new InvalidOperationException(
            $"{context.Description} list '{bind}' columns must be an int >= 1.");
    }

    private static PanelAggregateCountSpec? ParseAggregate(
        JsonObject control,
        PanelLayoutControlType type,
        string? bind,
        PanelLayoutControlJsonContext context)
    {
        if (control["aggregate"] is null)
        {
            return null;
        }

        RequireListField(type, "aggregate", context);
        if (control["aggregate"] is not JsonObject aggregate)
        {
            throw new InvalidOperationException(
                $"{context.Description} list '{bind}' aggregate must be an object.");
        }

        RejectUnknownFields(aggregate, AggregateFields, $"{context.Description} list '{bind}' aggregate");
        if (aggregate["count"] is not JsonObject count)
        {
            throw new InvalidOperationException(
                $"{context.Description} list '{bind}' aggregate.count must be an object.");
        }

        RejectUnknownFields(count, AggregateCountFields, $"{context.Description} list '{bind}' aggregate.count");
        string from = RequireString(
            count,
            "from",
            context,
            $"{context.Description} list '{bind}' aggregate.count");
        if (!string.Equals(from, "totalCount", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{context.Description} list '{bind}' aggregate.count.from '{from}' is unknown (allowed: totalCount).");
        }

        if (count["prefix"] is not JsonValue prefixNode ||
            !prefixNode.TryGetValue<string>(out string? prefix) ||
            prefix == null)
        {
            throw new InvalidOperationException(
                $"{context.Description} list '{bind}' aggregate.count.prefix must be a string (empty allowed).");
        }

        return new PanelAggregateCountSpec(from, prefix);
    }

    private static void ValidateListOptions(
        JsonObject control,
        PanelLayoutControlType type,
        string? bind,
        bool virtualize,
        ref float? itemExtent,
        float? viewportHeight,
        PanelPresentMode present,
        int? columns,
        PanelAggregateCountSpec? aggregate,
        PanelLayoutControlJsonContext context)
    {
        bool hasListOption = viewportHeight.HasValue ||
            itemExtent.HasValue ||
            virtualize ||
            control["overscan"] is not null ||
            control["present"] is not null ||
            control["columns"] is not null ||
            control["aggregate"] is not null;
        if (type != PanelLayoutControlType.List)
        {
            if (hasListOption)
            {
                throw new InvalidOperationException(
                    $"{context.Description} list fields are only valid on list controls.");
            }

            return;
        }

        if (string.IsNullOrWhiteSpace(bind))
        {
            throw new InvalidOperationException($"{context.Description} list control requires bind.");
        }

        if (virtualize && !viewportHeight.HasValue)
        {
            throw new InvalidOperationException(
                $"{context.Description} list '{bind}' virtualize requires viewportHeight.");
        }

        if (virtualize && !itemExtent.HasValue)
        {
            itemExtent = 56f;
        }

        if (present == PanelPresentMode.Grid)
        {
            if (!columns.HasValue)
            {
                throw new InvalidOperationException(
                    $"{context.Description} list '{bind}' present=grid requires columns.");
            }

            if (virtualize)
            {
                throw new InvalidOperationException(
                    $"{context.Description} list '{bind}' cannot combine present=grid with virtualize.");
            }
        }
        else if (columns.HasValue)
        {
            throw new InvalidOperationException(
                $"{context.Description} list '{bind}' columns is only valid when present=grid.");
        }

        if (present == PanelPresentMode.Column && virtualize)
        {
            throw new InvalidOperationException(
                $"{context.Description} list '{bind}' cannot combine present=column with virtualize.");
        }

        if (present == PanelPresentMode.Aggregate)
        {
            if (virtualize)
            {
                throw new InvalidOperationException(
                    $"{context.Description} list '{bind}' cannot combine present=aggregate with virtualize.");
            }

            if (aggregate == null)
            {
                throw new InvalidOperationException(
                    $"{context.Description} list '{bind}' present=aggregate requires aggregate.count.");
            }
        }
        else if (aggregate != null)
        {
            throw new InvalidOperationException(
                $"{context.Description} list '{bind}' aggregate is only valid when present=aggregate.");
        }
    }

    private static void RequireListField(
        PanelLayoutControlType type,
        string field,
        PanelLayoutControlJsonContext context)
    {
        if (type != PanelLayoutControlType.List)
        {
            throw new InvalidOperationException(
                $"{context.Description} {field} is only valid on list controls.");
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

    private static string RequireString(
        JsonObject node,
        string field,
        PanelLayoutControlJsonContext context,
        string? errorContext = null)
    {
        string? value = OptionalString(node, field, context);
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException(
                $"{errorContext ?? context.Description} requires non-empty '{field}'.");
    }

    private static string? OptionalString(
        JsonObject node,
        string field,
        PanelLayoutControlJsonContext context)
    {
        if (node[field] is null)
        {
            return null;
        }

        if (node[field] is JsonValue value && value.TryGetValue<string>(out string? text))
        {
            return text?.Trim();
        }

        throw new InvalidOperationException($"{context.Description} field '{field}' must be a string.");
    }

    private static bool? OptionalBool(
        JsonObject node,
        string field,
        PanelLayoutControlJsonContext context)
    {
        if (node[field] is null)
        {
            return null;
        }

        if (node[field] is JsonValue value && value.TryGetValue<bool>(out bool result))
        {
            return result;
        }

        throw new InvalidOperationException($"{context.Description} field '{field}' must be a bool.");
    }

    private static float? OptionalPositiveFloat(
        JsonObject node,
        string field,
        PanelLayoutControlJsonContext context)
        => OptionalFloat(node, field, context, positive: true);

    private static float? OptionalNonNegativeFloat(
        JsonObject node,
        string field,
        PanelLayoutControlJsonContext context)
        => OptionalFloat(node, field, context, positive: false);

    private static float? OptionalFloat(
        JsonObject node,
        string field,
        PanelLayoutControlJsonContext context,
        bool positive)
    {
        if (node[field] is null)
        {
            return null;
        }

        if (node[field] is JsonValue value &&
            value.TryGetValue<double>(out double raw) &&
            (positive ? raw > 0d : raw >= 0d))
        {
            return (float)raw;
        }

        string requirement = positive ? "positive" : "non-negative";
        throw new InvalidOperationException(
            $"{context.Description} field '{field}' must be {requirement}.");
    }

    private static int? OptionalNonNegativeInt(
        JsonObject node,
        string field,
        PanelLayoutControlJsonContext context)
    {
        if (node[field] is null)
        {
            return null;
        }

        if (node[field] is JsonValue value &&
            value.TryGetValue<int>(out int result) &&
            result >= 0)
        {
            return result;
        }

        throw new InvalidOperationException(
            $"{context.Description} field '{field}' must be a non-negative int.");
    }
}
