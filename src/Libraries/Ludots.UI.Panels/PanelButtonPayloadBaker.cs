using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Ludots.Core.UI.PanelProjection;

namespace Ludots.UI.Panels;

/// <summary>
/// Bakes Button payload sources at compose time against the binding scope the label
/// resolved from (panel variables or repeater item fields). Numeric and bool kinds
/// resolve literal-first; the string kind resolves bind-first and falls back to the
/// literal text. Both directions fail loud and named — a payload that cannot be
/// produced is an authoring bug, not a silent empty field.
/// </summary>
public static class PanelButtonPayloadBaker
{
    public static JsonObject Bake(
        PanelTemplate template,
        PanelTemplateEvent declaration,
        PanelLayoutControl button,
        IPanelLayoutBindingScope scope)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(declaration);
        ArgumentNullException.ThrowIfNull(button);
        ArgumentNullException.ThrowIfNull(scope);

        var payload = new JsonObject();
        foreach (KeyValuePair<string, PanelEventPayloadKind> field in declaration.Payload)
        {
            if (!button.EventPayload.TryGetValue(field.Key, out string? source))
            {
                throw new InvalidOperationException(
                    $"Panel '{template.Id}' button '{button.ControlName}' has no payload source for " +
                    $"event '{declaration.EventId}' field '{field.Key}'.");
            }

            payload[field.Key] = field.Value switch
            {
                PanelEventPayloadKind.Int => BakeInt(template, declaration, button, field.Key, source, scope),
                PanelEventPayloadKind.Float => BakeFloat(template, declaration, button, field.Key, source, scope),
                PanelEventPayloadKind.Bool => BakeBool(template, declaration, button, field.Key, source, scope),
                PanelEventPayloadKind.String => BakeString(template, declaration, button, field.Key, source, scope),
                _ => throw new InvalidOperationException(
                    $"Panel '{template.Id}' event '{declaration.EventId}' field '{field.Key}' has unsupported kind '{field.Value}'.")
            };
        }

        return payload;
    }

    private static int BakeInt(PanelTemplate template, PanelTemplateEvent declaration, PanelLayoutControl button, string field, string source, IPanelLayoutBindingScope scope)
    {
        if (int.TryParse(source, out int literal))
        {
            return literal;
        }
        return int.TryParse(ReadBind(template, declaration, button, field, source, scope), out int value)
            ? value
            : throw PayloadParseError(template, declaration, button, field, source, "int");
    }

    private static float BakeFloat(PanelTemplate template, PanelTemplateEvent declaration, PanelLayoutControl button, string field, string source, IPanelLayoutBindingScope scope)
    {
        if (float.TryParse(source, out float literal))
        {
            return literal;
        }
        return float.TryParse(ReadBind(template, declaration, button, field, source, scope), out float value)
            ? value
            : throw PayloadParseError(template, declaration, button, field, source, "float");
    }

    private static bool BakeBool(PanelTemplate template, PanelTemplateEvent declaration, PanelLayoutControl button, string field, string source, IPanelLayoutBindingScope scope)
    {
        if (bool.TryParse(source, out bool literal))
        {
            return literal;
        }
        return bool.TryParse(ReadBind(template, declaration, button, field, source, scope), out bool value)
            ? value
            : throw PayloadParseError(template, declaration, button, field, source, "bool");
    }

    private static string BakeString(PanelTemplate template, PanelTemplateEvent declaration, PanelLayoutControl button, string field, string source, IPanelLayoutBindingScope scope)
    {
        try
        {
            return ReadBind(template, declaration, button, field, source, scope);
        }
        catch (InvalidOperationException)
        {
            return source;
        }
    }

    private static string ReadBind(PanelTemplate template, PanelTemplateEvent declaration, PanelLayoutControl button, string field, string source, IPanelLayoutBindingScope scope)
    {
        try
        {
            return scope.ReadText(source);
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException(
                $"Panel '{template.Id}' button '{button.ControlName}' payload field '{field}' source " +
                $"'{source}' is neither a resolvable binding nor a literal: {ex.Message}", ex);
        }
    }

    private static InvalidOperationException PayloadParseError(PanelTemplate template, PanelTemplateEvent declaration, PanelLayoutControl button, string field, string source, string kind)
        => new(
            $"Panel '{template.Id}' button '{button.ControlName}' payload field '{field}' source '{source}' " +
            $"does not resolve to a {kind} value for event '{declaration.EventId}'.");
}
