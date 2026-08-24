using System;
using System.Collections.Generic;
using Ludots.Core.UI.PanelProjection;

namespace Ludots.Core.UI.PanelHosting
{
    /// <summary>
    /// Template id → <see cref="PanelTemplate"/>. Filled from the config pipeline
    /// (<see cref="PanelTemplateCatalogLoader"/>) or registered directly in code;
    /// both paths end at the same registry the host resolves against.
    /// </summary>
    public sealed class PanelTemplateRegistry
    {
        private readonly Dictionary<string, PanelTemplate> _templates = new(StringComparer.Ordinal);
        private bool _frozen;

        public int Count => _templates.Count;

        public IReadOnlyList<PanelTemplate> Snapshot()
        {
            var list = new List<PanelTemplate>(_templates.Count);
            list.AddRange(_templates.Values);
            return list;
        }

        public void Register(PanelTemplate template)
        {
            ArgumentNullException.ThrowIfNull(template);
            if (_frozen)
            {
                throw new InvalidOperationException($"Panel template registry is frozen; cannot register '{template.Id}'.");
            }

            if (!_templates.TryAdd(template.Id, template))
            {
                throw new InvalidOperationException($"Duplicate panel template id '{template.Id}'.");
            }
        }

        public PanelTemplate Require(string templateId)
        {
            if (string.IsNullOrWhiteSpace(templateId))
            {
                throw new ArgumentException("Panel template id is required.", nameof(templateId));
            }

            return _templates.TryGetValue(templateId.Trim(), out PanelTemplate? template)
                ? template
                : throw new InvalidOperationException($"Unknown panel template '{templateId}'.");
        }

        public bool TryGet(string templateId, out PanelTemplate? template)
        {
            template = null;
            return !string.IsNullOrWhiteSpace(templateId) && _templates.TryGetValue(templateId.Trim(), out template);
        }

        public void Freeze() => _frozen = true;
    }
}
