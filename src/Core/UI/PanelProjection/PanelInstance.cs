using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Arch.Core;

namespace Ludots.Core.UI.PanelProjection
{
    /// <summary>
    /// One live panel: a template bound to a scope. Evaluation goes exclusively
    /// through <see cref="PanelProjectionReader"/> — reading the graph output store;
    /// graph execution is scheduled elsewhere (panel host / writer adapter).
    /// </summary>
    public sealed class PanelInstance
    {
        public PanelInstance(PanelTemplate template, Entity scope)
        {
            Template = template ?? throw new ArgumentNullException(nameof(template));
            Scope = scope;
        }

        public PanelTemplate Template { get; }
        public Entity Scope { get; }

        public PanelVariableSet Evaluate(PanelProjectionReader reader)
        {
            ArgumentNullException.ThrowIfNull(reader);

            var values = new Dictionary<string, float>(Template.Pins.Count, StringComparer.Ordinal);
            var nodes = new Dictionary<string, JsonNode>(StringComparer.Ordinal);
            uint revision = 0;
            foreach (PanelPin pin in Template.Pins)
            {
                PanelProjectionValue value = reader.Resolve(Scope, pin);
                if (value.Node != null)
                {
                    nodes[pin.Name] = value.Node;
                    if (!value.FromData || value.Node is JsonValue)
                    {
                        values[pin.Name] = value.FloatValue;
                    }
                }
                else
                {
                    values[pin.Name] = value.FloatValue;
                }
                revision ^= value.Revision;
            }

            return new PanelVariableSet(Template.Id, values, revision, nodes);
        }
    }

    /// <summary>
    /// Evaluated pin values for one instance. Reads of unknown names fail loudly;
    /// missing graph outputs already resolved to pin defaults by the reader.
    /// </summary>
    public sealed class PanelVariableSet
    {
        public PanelVariableSet(
            string templateId,
            Dictionary<string, float> values,
            uint revision,
            Dictionary<string, JsonNode>? nodes = null)
        {
            TemplateId = templateId;
            Values = values ?? throw new ArgumentNullException(nameof(values));
            Nodes = nodes ?? new Dictionary<string, JsonNode>(StringComparer.Ordinal);
            Revision = revision;
        }

        public string TemplateId { get; }
        public Dictionary<string, float> Values { get; }
        public Dictionary<string, JsonNode> Nodes { get; }
        public uint Revision { get; }

        public float Get(string pinName)
        {
            return Values.TryGetValue(pinName, out float value)
                ? value
                : throw new InvalidOperationException($"Panel '{TemplateId}' has no pin '{pinName}'.");
        }

        public bool TryGet(string pinName, out float value)
        {
            return Values.TryGetValue(pinName, out value);
        }

        public JsonNode GetNode(string pinName)
        {
            return Nodes.TryGetValue(pinName, out JsonNode? value)
                ? value
                : throw new InvalidOperationException($"Panel '{TemplateId}' has no structured pin '{pinName}'.");
        }

        public bool TryGetNode(string pinName, out JsonNode? value)
        {
            return Nodes.TryGetValue(pinName, out value);
        }

        public object GetValue(string pinName)
        {
            if (Nodes.TryGetValue(pinName, out JsonNode? node))
            {
                return node;
            }

            return Get(pinName);
        }

        public string GetDisplayText(string pinName)
        {
            if (Nodes.TryGetValue(pinName, out JsonNode? node))
            {
                if (node is JsonValue jsonValue && jsonValue.TryGetValue<string>(out string? text))
                {
                    return text;
                }

                return node.ToJsonString();
            }

            return Get(pinName).ToString("F0", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
