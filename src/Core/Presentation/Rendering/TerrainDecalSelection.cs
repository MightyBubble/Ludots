using System;

namespace Ludots.Core.Presentation.Rendering
{
    public static class TerrainDecalSelection
    {
        public static bool TrySelect(
            GlobalFieldVisualBuffer buffer,
            bool seatScopeResolved,
            int scopeKeyId,
            out GlobalFieldVisualDescriptor decal)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            decal = default;
            GlobalFieldVisualDescriptor? fog = null;
            foreach (GlobalFieldVisualRecord record in buffer.GetRecords())
            {
                if (!record.IsActive)
                {
                    continue;
                }

                GlobalFieldVisualKind kind = record.Descriptor.Id.Kind;
                if (kind is GlobalFieldVisualKind.Flow or GlobalFieldVisualKind.Walkable)
                {
                    decal = record.Descriptor;
                    return true;
                }

                if (seatScopeResolved &&
                    fog == null &&
                    kind == GlobalFieldVisualKind.Fog &&
                    record.Descriptor.Id.ScopeKeyId == scopeKeyId)
                {
                    fog = record.Descriptor;
                }
            }

            if (fog is not { } chosen)
            {
                return false;
            }

            decal = chosen;
            return true;
        }
    }
}
