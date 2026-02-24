using System.Runtime.CompilerServices;
using Arch.Core;

namespace Ludots.Core.Gameplay.GAS
{
    /// <summary>
    /// Utility for reconstructing an Entity from its raw int components.
    /// Centralizes the Unsafe.As layout assumption so that if Arch changes Entity's layout,
    /// only this one location needs to be updated.
    /// </summary>
    public static class EntityUtil
    {
        /// <summary>
        /// Reconstruct an Entity value from its raw Id, WorldId, and Version fields.
        /// WARNING: This assumes Entity memory layout is {int Id, int WorldId, int Version} with no padding.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Entity Reconstruct(int id, int worldId, int version)
        {
            Entity e = default;
            ref int ptr = ref Unsafe.As<Entity, int>(ref e);
            ptr = id;
            Unsafe.Add(ref ptr, 1) = worldId;
            Unsafe.Add(ref ptr, 2) = version;
            return e;
        }
    }
}
