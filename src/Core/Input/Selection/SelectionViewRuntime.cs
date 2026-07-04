using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Input.EntityView;
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
            viewKey = string.Empty;
            container = default;

            if (!EntityViewRuntime.TryGetCurrentViewer(world, globals, out viewer))
            {
                return false;
            }

            if (!EntityViewRuntime.TryGetCurrentViewKey(globals, out viewKey))
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
