using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Input.CommandSources;

namespace CameraAcceptanceMod.Runtime
{
    internal static class CameraAcceptanceSelectionView
    {
        public static int CopySelectedEntities(World world, Dictionary<string, object> globals, Span<Entity> destination)
        {
            return EntityCollectionContextRuntime.CopyCurrent(world, globals, destination);
        }

        public static Entity[] SnapshotSelectedEntities(World world, Dictionary<string, object> globals)
        {
            return EntityCollectionContextRuntime.SnapshotCurrent(world, globals);
        }

        public static string FormatEntityId(Entity entity) => $"#{entity.Id}";
    }
}
