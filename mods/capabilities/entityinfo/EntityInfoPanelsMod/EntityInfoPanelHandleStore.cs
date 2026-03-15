using System;
using System.Collections.Generic;

namespace EntityInfoPanelsMod;

public sealed class EntityInfoPanelHandleStore
{
    private readonly Dictionary<string, EntityInfoPanelHandle> _handles =
        new(StringComparer.Ordinal);

    public void Set(string key, EntityInfoPanelHandle handle)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        _handles[key] = handle;
    }

    public bool TryGet(string key, out EntityInfoPanelHandle handle)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            handle = EntityInfoPanelHandle.Invalid;
            return false;
        }

        return _handles.TryGetValue(key, out handle);
    }

    public bool Remove(string key)
    {
        return !string.IsNullOrWhiteSpace(key) && _handles.Remove(key);
    }
}
