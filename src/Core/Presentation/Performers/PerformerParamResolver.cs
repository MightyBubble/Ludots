using System.Numerics;
using Arch.Core;

namespace Ludots.Core.Presentation.Performers
{
    public static class PerformerParamResolver
    {
        public static float ResolveFloat(World world, Entity performer, int paramKey, float defaultValue = 0f)
        {
            return TryResolveFloat(world, performer, paramKey, out float value) ? value : defaultValue;
        }

        public static bool TryResolveFloat(World world, Entity performer, int paramKey, out float value)
        {
            Entity current = performer;
            while (world.IsAlive(current))
            {
                if (world.Has<PerformerFloatParams>(current))
                {
                    ref var overrides = ref world.Get<PerformerFloatParams>(current);
                    if (overrides.TryGet(paramKey, out value)) return true;
                }
                if (world.Has<PerformerFloatDefaults>(current))
                {
                    ref var defaults = ref world.Get<PerformerFloatDefaults>(current);
                    if (defaults.TryGet(paramKey, out value)) return true;
                }
                if (!world.Has<PerformerParent>(current)) break;
                current = world.Get<PerformerParent>(current).Parent;
            }
            value = default;
            return false;
        }

        public static int ResolveInt(World world, Entity performer, int paramKey, int defaultValue = 0)
        {
            return TryResolveInt(world, performer, paramKey, out int value) ? value : defaultValue;
        }

        public static bool TryResolveInt(World world, Entity performer, int paramKey, out int value)
        {
            Entity current = performer;
            while (world.IsAlive(current))
            {
                if (world.Has<PerformerIntParams>(current))
                {
                    ref var overrides = ref world.Get<PerformerIntParams>(current);
                    if (overrides.TryGet(paramKey, out value)) return true;
                }
                if (world.Has<PerformerIntDefaults>(current))
                {
                    ref var defaults = ref world.Get<PerformerIntDefaults>(current);
                    if (defaults.TryGet(paramKey, out value)) return true;
                }
                if (!world.Has<PerformerParent>(current)) break;
                current = world.Get<PerformerParent>(current).Parent;
            }
            value = default;
            return false;
        }

        public static Vector4 ResolveVector(World world, Entity performer, int paramKey, Vector4 defaultValue)
        {
            return TryResolveVector(world, performer, paramKey, out Vector4 value) ? value : defaultValue;
        }

        public static bool TryResolveVector(World world, Entity performer, int paramKey, out Vector4 value)
        {
            Entity current = performer;
            while (world.IsAlive(current))
            {
                if (world.Has<PerformerVectorParams>(current))
                {
                    ref var overrides = ref world.Get<PerformerVectorParams>(current);
                    if (overrides.TryGet(paramKey, out value)) return true;
                }
                if (world.Has<PerformerVectorDefaults>(current))
                {
                    ref var defaults = ref world.Get<PerformerVectorDefaults>(current);
                    if (defaults.TryGet(paramKey, out value)) return true;
                }
                if (!world.Has<PerformerParent>(current)) break;
                current = world.Get<PerformerParent>(current).Parent;
            }
            value = default;
            return false;
        }
    }
}
