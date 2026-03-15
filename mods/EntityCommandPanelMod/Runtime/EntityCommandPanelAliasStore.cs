using System;
using System.Collections.Generic;
using Ludots.Core.UI.EntityCommandPanels;

namespace EntityCommandPanelMod.Runtime
{
    internal sealed class EntityCommandPanelAliasStore : IEntityCommandPanelHandleStore
    {
        private readonly Dictionary<string, EntityCommandPanelHandle> _handles = new(StringComparer.Ordinal);

        public bool TryBind(string alias, EntityCommandPanelHandle handle)
        {
            if (string.IsNullOrWhiteSpace(alias) || !handle.IsValid)
            {
                return false;
            }

            _handles[alias] = handle;
            return true;
        }

        public bool TryGet(string alias, out EntityCommandPanelHandle handle)
        {
            if (!string.IsNullOrWhiteSpace(alias) && _handles.TryGetValue(alias, out handle))
            {
                return handle.IsValid;
            }

            handle = EntityCommandPanelHandle.Invalid;
            return false;
        }

        public bool Remove(string alias)
        {
            return !string.IsNullOrWhiteSpace(alias) && _handles.Remove(alias);
        }

        internal void RemoveHandle(EntityCommandPanelHandle handle)
        {
            while (true)
            {
                string? matchedAlias = null;
                foreach (var pair in _handles)
                {
                    if (pair.Value.Equals(handle))
                    {
                        matchedAlias = pair.Key;
                        break;
                    }
                }

                if (matchedAlias == null)
                {
                    return;
                }

                _handles.Remove(matchedAlias);
            }
        }
    }
}
