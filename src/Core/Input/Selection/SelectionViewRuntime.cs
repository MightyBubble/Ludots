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
        public static bool TryResolveViewedSelection(
            World world,
            Dictionary<string, object> globals,
            SelectionRuntime selection,
            out Entity viewer,
            out string viewKey,
            out Entity container)
        {
            viewer = default;
            viewKey = SelectionViewKeys.Primary;
            container = default;

            if (globals.TryGetValue(CoreServiceKeys.SelectionViewViewerEntity.Name, out var viewObj) &&
                viewObj is Entity viewed &&
                world.IsAlive(viewed))
            {
                viewer = viewed;
            }
            else if (globals.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out var localObj) &&
                     localObj is Entity local &&
                     world.IsAlive(local))
            {
                globals[CoreServiceKeys.SelectionViewViewerEntity.Name] = local;
                viewer = local;
            }
            else
            {
                globals.Remove(CoreServiceKeys.SelectionViewViewerEntity.Name);
                return false;
            }

            if (globals.TryGetValue(CoreServiceKeys.SelectionViewKey.Name, out var setObj) &&
                setObj is string configuredViewKey &&
                !string.IsNullOrWhiteSpace(configuredViewKey))
            {
                viewKey = configuredViewKey;
            }
            else
            {
                globals[CoreServiceKeys.SelectionViewKey.Name] = SelectionViewKeys.Primary;
            }

            if (selection.TryResolveViewContainer(viewer, viewKey, out container))
            {
                return true;
            }

            if (!selection.TryBindView(viewer, viewKey, viewer, SelectionSetKeys.LivePrimary))
            {
                return false;
            }

            return selection.TryResolveViewContainer(viewer, viewKey, out container);
        }

        public static int CopyViewedSelection(
            World world,
            Dictionary<string, object> globals,
            SelectionRuntime selection,
            Span<Entity> destination)
        {
            return TryResolveViewedSelection(world, globals, selection, out _, out _, out var container)
                ? selection.CopySelection(container, destination)
                : 0;
        }

        public static int GetViewedSelectionCount(
            World world,
            Dictionary<string, object> globals,
            SelectionRuntime selection)
        {
            return TryResolveViewedSelection(world, globals, selection, out _, out _, out var container)
                ? selection.GetSelectionCount(container)
                : 0;
        }

        public static bool TryGetViewedPrimary(
            World world,
            Dictionary<string, object> globals,
            SelectionRuntime selection,
            out Entity primary)
        {
            primary = default;
            return TryResolveViewedSelection(world, globals, selection, out _, out _, out var container) &&
                   selection.TryGetPrimary(container, out primary);
        }
    }
}
