using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Scripting;

namespace Ludots.Core.Input.Selection
{
    /// <summary>
    /// Resolves the currently viewed selection owner + set from global context.
    /// This keeps presentation/debug readers decoupled from any specific selector type.
    /// </summary>
    public static class SelectionViewRuntime
    {
        public static bool TryResolveViewedSet(
            World world,
            Dictionary<string, object> globals,
            out Entity owner,
            out string setKey)
        {
            owner = default;
            setKey = SelectionSetKeys.Ambient;

            if (globals.TryGetValue(CoreServiceKeys.SelectionViewOwnerEntity.Name, out var viewObj) &&
                viewObj is Entity viewed &&
                world.IsAlive(viewed))
            {
                owner = viewed;
            }
            else if (globals.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out var localObj) &&
                     localObj is Entity local &&
                     world.IsAlive(local))
            {
                globals[CoreServiceKeys.SelectionViewOwnerEntity.Name] = local;
                owner = local;
            }
            else
            {
                globals.Remove(CoreServiceKeys.SelectionViewOwnerEntity.Name);
                return false;
            }

            if (globals.TryGetValue(CoreServiceKeys.SelectionViewSetKey.Name, out var setObj) &&
                setObj is string configuredSetKey &&
                !string.IsNullOrWhiteSpace(configuredSetKey))
            {
                setKey = configuredSetKey;
                return true;
            }

            globals[CoreServiceKeys.SelectionViewSetKey.Name] = SelectionSetKeys.Ambient;
            return true;
        }

        public static int CopyViewedSelection(
            World world,
            Dictionary<string, object> globals,
            SelectionRuntime selection,
            Span<Entity> destination)
        {
            return TryResolveViewedSet(world, globals, out var owner, out var setKey)
                ? selection.CopySelection(owner, setKey, destination)
                : 0;
        }

        public static bool TryGetViewedPrimary(
            World world,
            Dictionary<string, object> globals,
            SelectionRuntime selection,
            out Entity primary)
        {
            primary = default;
            return TryResolveViewedSet(world, globals, out var owner, out var setKey) &&
                   selection.TryGetPrimary(owner, setKey, out primary);
        }
    }
}
