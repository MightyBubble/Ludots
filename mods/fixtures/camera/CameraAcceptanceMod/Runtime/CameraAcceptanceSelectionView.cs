using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Input.Selection;
using Ludots.Core.Scripting;

namespace CameraAcceptanceMod.Runtime
{
    internal static class CameraAcceptanceSelectionView
    {
        public static int CopySelectedEntities(World world, Dictionary<string, object> globals, Span<Entity> destination)
        {
            if (!globals.TryGetValue(CoreServiceKeys.SelectionRuntime.Name, out var runtimeObj) ||
                runtimeObj is not SelectionRuntime selection)
            {
                return 0;
            }

            return SelectionViewRuntime.CopyViewedSelection(world, globals, selection, destination);
        }

        public static string FormatEntityId(Entity entity) => $"#{entity.Id}";
    }
}
