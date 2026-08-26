using System;
using System.Collections.Generic;
using Ludots.Core.UI.PanelProjection;

namespace Ludots.Core.UI.PanelHosting
{
    public sealed class PanelItemTemplateRegistry
    {
        private readonly Dictionary<string, PanelItemTemplate> _templates = new(StringComparer.Ordinal);
        private bool _frozen;

        public int Count => _templates.Count;

        public IReadOnlyList<PanelItemTemplate> Snapshot()
        {
            var list = new List<PanelItemTemplate>(_templates.Count);
            list.AddRange(_templates.Values);
            return list;
        }

        public void Register(PanelItemTemplate template)
        {
            ArgumentNullException.ThrowIfNull(template);
            if (_frozen)
            {
                throw new InvalidOperationException($"Item template registry is frozen; cannot register '{template.Id}'.");
            }

            if (!_templates.TryAdd(template.Id, template))
            {
                throw new InvalidOperationException($"Duplicate item template id '{template.Id}'.");
            }
        }

        public PanelItemTemplate Require(string templateId)
        {
            if (string.IsNullOrWhiteSpace(templateId))
            {
                throw new ArgumentException("Item template id is required.", nameof(templateId));
            }

            return _templates.TryGetValue(templateId.Trim(), out PanelItemTemplate? template)
                ? template
                : throw new InvalidOperationException($"Unknown item template '{templateId}'.");
        }

        public bool TryGet(string templateId, out PanelItemTemplate? template)
        {
            template = null;
            return !string.IsNullOrWhiteSpace(templateId) && _templates.TryGetValue(templateId.Trim(), out template);
        }

        public void Freeze() => _frozen = true;
    }

    public static class PanelItemTemplateBinder
    {
        public static void Bind(PanelTemplate panel, PanelItemTemplateRegistry items)
        {
            ArgumentNullException.ThrowIfNull(panel);
            ArgumentNullException.ThrowIfNull(items);

            foreach (PanelCollectionBinding collection in panel.Collections)
            {
                collection.Item = items.Require(collection.ItemTemplateId);
            }

            PanelListProjector.BindSymbols(panel);
        }

        public static void BindAll(IEnumerable<PanelTemplate> panels, PanelItemTemplateRegistry items)
        {
            ArgumentNullException.ThrowIfNull(panels);
            ArgumentNullException.ThrowIfNull(items);
            foreach (PanelTemplate panel in panels)
            {
                Bind(panel, items);
            }
        }
    }
}
