using System;
using System.Collections.Generic;
using Ludots.Core.UI.EntityCommandPanels;

namespace EntityCommandPanelMod.Runtime
{
    internal sealed class EntityCommandPanelSourceRegistry : IEntityCommandPanelSourceRegistry
    {
        private readonly Dictionary<string, IEntityCommandPanelSource> _sources = new(StringComparer.Ordinal);

        public void Register(string sourceId, IEntityCommandPanelSource source)
        {
            if (string.IsNullOrWhiteSpace(sourceId))
            {
                throw new ArgumentException("Entity command panel source id is required.", nameof(sourceId));
            }

            _sources[sourceId] = source ?? throw new ArgumentNullException(nameof(source));
        }

        public bool TryGet(string sourceId, out IEntityCommandPanelSource source)
        {
            if (!string.IsNullOrWhiteSpace(sourceId) && _sources.TryGetValue(sourceId, out source!))
            {
                return true;
            }

            source = null!;
            return false;
        }
    }
}
