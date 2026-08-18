using System;
using System.Collections.Generic;
using Arch.Core;

namespace Ludots.Core.UI.PanelProjection
{
    /// <summary>
    /// One live panel: a template bound to a scope (#1010 MVP: the owner entity).
    /// Evaluation goes exclusively through <see cref="PanelProjectionReader"/>.
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

            var values = new Dictionary<string, float>(Template.Variables.Count, StringComparer.Ordinal);
            uint revision = 0;
            foreach (PanelTemplateVariable variable in Template.Variables)
            {
                PanelProjectionValue value = reader.Resolve(Scope, variable.ToBinding());
                values[variable.Name] = value.FloatValue;
                revision ^= value.Revision;
            }

            return new PanelVariableSet(Template.Id, values, revision);
        }
    }

    /// <summary>
    /// Evaluated variables for one instance. Reads of unknown names fail loudly;
    /// there is no silent zero.
    /// </summary>
    public sealed class PanelVariableSet
    {
        private readonly IReadOnlyDictionary<string, float> _values;

        public PanelVariableSet(string templateId, IReadOnlyDictionary<string, float> values, uint revision)
        {
            TemplateId = templateId;
            _values = values;
            Revision = revision;
        }

        public string TemplateId { get; }
        public uint Revision { get; }
        public int Count => _values.Count;
        public IEnumerable<string> Names => _values.Keys;

        public float Get(string variableName)
        {
            if (!_values.TryGetValue(variableName, out float value))
            {
                throw new InvalidOperationException($"Panel '{TemplateId}' has no evaluated variable '{variableName}'.");
            }

            return value;
        }

        public bool TryGet(string variableName, out float value) => _values.TryGetValue(variableName, out value);
    }
}
