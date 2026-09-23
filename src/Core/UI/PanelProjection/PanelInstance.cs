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

            var values = new Dictionary<string, PanelProjectionValue>(Template.Pins.Count, StringComparer.Ordinal);
            uint revision = 0;
            foreach (PanelPin pin in Template.Pins)
            {
                PanelProjectionValue value = reader.Resolve(Scope, pin);
                values[pin.Name] = value;
                revision ^= value.Revision;
            }

            return new PanelVariableSet(Template.Id, values, revision);
        }
    }

    /// <summary>
    /// Evaluated pin values for one instance, each carrying its kind plus the typed
    /// value. Reads of unknown names fail loudly; missing graph outputs failed
    /// already at the reader, so every value here is graph-sourced.
    /// </summary>
    public sealed class PanelVariableSet
    {
        public PanelVariableSet(string templateId, Dictionary<string, PanelProjectionValue> values, uint revision)
        {
            TemplateId = templateId;
            Values = values ?? throw new ArgumentNullException(nameof(values));
            Revision = revision;
        }

        public string TemplateId { get; }
        public Dictionary<string, PanelProjectionValue> Values { get; }
        public uint Revision { get; }

        public PanelProjectionValue Get(string pinName)
        {
            return Values.TryGetValue(pinName, out PanelProjectionValue value)
                ? value
                : throw new InvalidOperationException($"Panel '{TemplateId}' has no pin '{pinName}'.");
        }

        public bool TryGet(string pinName, out PanelProjectionValue value)
        {
            return Values.TryGetValue(pinName, out value);
        }
    }
}
