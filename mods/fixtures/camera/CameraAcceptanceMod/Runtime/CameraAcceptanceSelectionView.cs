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
            if (!TryGetSelectionBuffer(world, globals, out var selection))
            {
                return 0;
            }

            int count = selection.Count;
            if (count > destination.Length)
            {
                count = destination.Length;
            }

            int written = 0;
            for (int i = 0; i < count; i++)
            {
                Entity entity = selection.Get(i);
                if (!world.IsAlive(entity))
                {
                    continue;
                }

                destination[written++] = entity;
            }

            return written;
        }

        public static string FormatEntityId(Entity entity) => $"#{entity.Id}";

        private static bool TryGetSelectionBuffer(World world, Dictionary<string, object> globals, out SelectionBuffer selection)
        {
            selection = default;
            if (!globals.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out var localObj) ||
                localObj is not Entity local ||
                !world.IsAlive(local) ||
                !world.Has<SelectionBuffer>(local))
            {
                return false;
            }

            selection = world.Get<SelectionBuffer>(local);
            return true;
        }
    }
}
