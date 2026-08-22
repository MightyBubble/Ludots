using System;
using System.Collections.Generic;
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
            uint revision = 0;
            foreach (PanelPin pin in Template.Pins)
            {
                PanelProjectionValue value = reader.Resolve(Scope, pin);
                values[pin.Name] = value.FloatValue;
                revision ^= value.Revision;
            }

            return new PanelVariableSet(Template.Id, values, revision);
        }
    }

    /// <summary>
    /// Evaluated pin values for one instance. Reads of unknown names fail loudly;
    /// missing graph outputs already resolved to pin defaults by the reader.
    /// </summary>
    public sealed class PanelVariableSet
    {
        public PanelVariableSet(string templateId, Dictionary<string, float> values, uint revision)
        {
            TemplateId = templateId;
            Values = values ?? throw new ArgumentNullException(nameof(values));
            Revision = revision;
        }

        public string TemplateId { get; }
        public Dictionary<string, float> Values { get; }
        public uint Revision { get; }

        public float Get(string pinName)
        {
            return Values.TryGetValue(pinName, out float value)
                ? value
                : throw new InvalidOperationException($"Panel '{TemplateId}' has no pin '{pinName}'.");
        }
    }
}
