using System.Numerics;
using Arch.Core;

namespace Ludots.Core.Presentation.Performers
{
    public static class PerformerParamResolver
    {
        public static float ResolveFloat(World world, Entity performer, int paramKey, float defaultValue = 0f)
        {
            Entity current = performer;
            while (world.IsAlive(current))
            {
                if (world.Has<PerformerFloatParams>(current))
                {
                    ref var overrides = ref world.Get<PerformerFloatParams>(current);
                    if (overrides.TryGet(paramKey, out float val)) return val;
                }
                if (world.Has<PerformerFloatDefaults>(current))
                {
                    ref var defaults = ref world.Get<PerformerFloatDefaults>(current);
                    if (defaults.TryGet(paramKey, out float val)) return val;
                }
                if (!world.Has<PerformerParent>(current)) break;
                current = world.Get<PerformerParent>(current).Parent;
            }
            return defaultValue;
        }

        public static int ResolveInt(World world, Entity performer, int paramKey, int defaultValue = 0)
        {
            Entity current = performer;
            while (world.IsAlive(current))
            {
                if (world.Has<PerformerIntParams>(current))
                {
                    ref var overrides = ref world.Get<PerformerIntParams>(current);
                    if (overrides.TryGet(paramKey, out int val)) return val;
                }
                if (world.Has<PerformerIntDefaults>(current))
                {
                    ref var defaults = ref world.Get<PerformerIntDefaults>(current);
                    if (defaults.TryGet(paramKey, out int val)) return val;
                }
                if (!world.Has<PerformerParent>(current)) break;
                current = world.Get<PerformerParent>(current).Parent;
            }
            return defaultValue;
        }

        public static Vector4 ResolveVector(World world, Entity performer, int paramKey, Vector4 defaultValue)
        {
            Entity current = performer;
            while (world.IsAlive(current))
            {
                if (world.Has<PerformerVectorParams>(current))
                {
                    ref var overrides = ref world.Get<PerformerVectorParams>(current);
                    if (overrides.TryGet(paramKey, out Vector4 val)) return val;
                }
                if (world.Has<PerformerVectorDefaults>(current))
                {
                    ref var defaults = ref world.Get<PerformerVectorDefaults>(current);
                    if (defaults.TryGet(paramKey, out Vector4 val)) return val;
                }
                if (!world.Has<PerformerParent>(current)) break;
                current = world.Get<PerformerParent>(current).Parent;
            }
            return defaultValue;
        }
    }
}
