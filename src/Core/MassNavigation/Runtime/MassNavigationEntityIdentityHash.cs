using System.Runtime.CompilerServices;
using Arch.Core;

namespace Ludots.Core.MassNavigation.Runtime;

internal static class MassNavigationEntityIdentityHash
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long Mix(long hash, Entity entity)
    {
        hash = Mix(hash, entity.Id);
        hash = Mix(hash, entity.WorldId);
        hash = Mix(hash, entity.Version);
        return hash;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long Mix(long hash, int value)
    {
        unchecked
        {
            hash ^= value;
            hash *= 1099511628211L;
            return hash;
        }
    }
}
