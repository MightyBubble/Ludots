using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Scripting;

namespace Ludots.Core.Input.Selection
{
    public static class SelectionContextRuntime
    {
        public static bool TryGetCurrentPrimary(World world, Dictionary<string, object> globals, out Entity primary)
        {
            primary = default;
            return TryGetRuntime(globals, out SelectionRuntime selection) &&
                   SelectionViewRuntime.TryGetViewedPrimary(world, globals, selection, out primary);
        }

        public static int GetCurrentCount(World world, Dictionary<string, object> globals)
        {
            return TryGetRuntime(globals, out SelectionRuntime selection)
                ? SelectionViewRuntime.GetViewedSelectionCount(world, globals, selection)
                : 0;
        }

        public static int CopyCurrentSelection(World world, Dictionary<string, object> globals, Span<Entity> destination)
        {
            return TryGetRuntime(globals, out SelectionRuntime selection)
                ? SelectionViewRuntime.CopyViewedSelection(world, globals, selection, destination)
                : 0;
        }

        public static Entity[] SnapshotCurrentSelection(World world, Dictionary<string, object> globals)
        {
            if (!TryGetRuntime(globals, out SelectionRuntime selection) ||
                !SelectionViewRuntime.TryResolveViewedSelection(world, globals, selection, out _, out _, out Entity container))
            {
                return Array.Empty<Entity>();
            }

            int count = selection.GetSelectionCount(container);
            if (count <= 0)
            {
                return Array.Empty<Entity>();
            }

            var selected = new Entity[count];
            int written = selection.CopySelection(container, selected);
            if (written <= 0)
            {
                return Array.Empty<Entity>();
            }

            if (written != selected.Length)
            {
                Array.Resize(ref selected, written);
            }

            return selected;
        }

        public static bool ContainsCurrentSelection(World world, Dictionary<string, object> globals, Entity entity)
        {
            if (!world.IsAlive(entity) ||
                !TryGetRuntime(globals, out SelectionRuntime selection) ||
                !SelectionViewRuntime.TryResolveViewedSelection(world, globals, selection, out _, out _, out Entity container))
            {
                return false;
            }

            int count = selection.GetSelectionCount(container);
            if (count <= 0)
            {
                return false;
            }

            var selected = new Entity[count];
            int written = selection.CopySelection(container, selected);
            for (int i = 0; i < written; i++)
            {
                if (selected[i] == entity)
                {
                    return true;
                }
            }

            return false;
        }

        public static bool TryGetCurrentContainer(World world, Dictionary<string, object> globals, out Entity container)
        {
            container = default;
            return TryGetRuntime(globals, out SelectionRuntime selection) &&
                   SelectionViewRuntime.TryResolveViewedSelection(world, globals, selection, out _, out _, out container);
        }

        public static bool TryDescribeCurrentView(World world, Dictionary<string, object> globals, out SelectionViewDescriptor descriptor)
        {
            descriptor = default;
            return TryGetRuntime(globals, out SelectionRuntime selection) &&
                   SelectionViewRuntime.TryResolveViewedSelection(world, globals, selection, out Entity viewer, out string viewKey, out _) &&
                   selection.TryDescribeView(viewer, viewKey, out descriptor);
        }

        public static bool TryGetRuntime(Dictionary<string, object> globals, out SelectionRuntime selection)
        {
            selection = default!;
            return globals.TryGetValue(CoreServiceKeys.SelectionRuntime.Name, out var selectionObj) &&
                   selectionObj is SelectionRuntime runtime &&
                   (selection = runtime) != null;
        }
    }
}
