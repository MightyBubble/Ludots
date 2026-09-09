using System.Numerics;
using Arch.Core;

namespace Ludots.Core.Presentation.Presenters
{
    public static class PresenterParamResolver
    {
        public static float ResolveFloat(World world, Entity presenter, int paramKey, float defaultValue = 0f)
        {
            return TryResolveFloat(world, presenter, paramKey, out float value) ? value : defaultValue;
        }

        public static bool TryResolveFloat(World world, Entity presenter, int paramKey, out float value)
        {
            Entity current = presenter;
            while (world.IsAlive(current))
            {
                if (world.Has<PresenterFloatParams>(current))
                {
                    ref var overrides = ref world.Get<PresenterFloatParams>(current);
                    if (overrides.TryGet(paramKey, out value)) return true;
                }
                if (world.Has<PresenterFloatDefaults>(current))
                {
                    ref var defaults = ref world.Get<PresenterFloatDefaults>(current);
                    if (defaults.TryGet(paramKey, out value)) return true;
                }
                if (!world.Has<PresenterParent>(current)) break;
                current = world.Get<PresenterParent>(current).Parent;
            }
            value = default;
            return false;
        }

        public static int ResolveInt(World world, Entity presenter, int paramKey, int defaultValue = 0)
        {
            return TryResolveInt(world, presenter, paramKey, out int value) ? value : defaultValue;
        }

        public static bool TryResolveInt(World world, Entity presenter, int paramKey, out int value)
        {
            Entity current = presenter;
            while (world.IsAlive(current))
            {
                if (world.Has<PresenterIntParams>(current))
                {
                    ref var overrides = ref world.Get<PresenterIntParams>(current);
                    if (overrides.TryGet(paramKey, out value)) return true;
                }
                if (world.Has<PresenterIntDefaults>(current))
                {
                    ref var defaults = ref world.Get<PresenterIntDefaults>(current);
                    if (defaults.TryGet(paramKey, out value)) return true;
                }
                if (!world.Has<PresenterParent>(current)) break;
                current = world.Get<PresenterParent>(current).Parent;
            }
            value = default;
            return false;
        }

        public static Vector4 ResolveVector(World world, Entity presenter, int paramKey, Vector4 defaultValue)
        {
            return TryResolveVector(world, presenter, paramKey, out Vector4 value) ? value : defaultValue;
        }

        public static bool TryResolveVector(World world, Entity presenter, int paramKey, out Vector4 value)
        {
            return TryResolveVector(world, presenter, paramKey, out value, default, false);
        }

        internal static bool TryResolveVector(World world, Entity presenter, int paramKey, out Vector4 value,
            in PresenterParamMutation mutation, bool requireVectorLane)
        {
            Entity current = presenter;
            while (world.IsAlive(current))
            {
                bool changed = mutation.AppliesTo(current, paramKey);
                if (requireVectorLane &&
                    ((changed && !mutation.Clear && mutation.Lane != ParamLane.Vector) ||
                     (!(changed && mutation.Clear && mutation.Lane == ParamLane.Float) &&
                      world.Has<PresenterFloatParams>(current) && world.Get<PresenterFloatParams>(current).TryGet(paramKey, out _)) ||
                     (!(changed && mutation.Clear && mutation.Lane == ParamLane.Int) &&
                      world.Has<PresenterIntParams>(current) && world.Get<PresenterIntParams>(current).TryGet(paramKey, out _))))
                    throw new System.InvalidOperationException($"PRESENTATION.ATTACHMENT.ERR.ParamLane: presenter={presenter.Id}, source={current.Id}, key={paramKey} requires Vector.");
                if (changed && mutation.Lane == ParamLane.Vector && !mutation.Clear)
                {
                    value = mutation.Vector;
                    return true;
                }
                if (!(changed && mutation.Clear && mutation.Lane == ParamLane.Vector) && world.Has<PresenterVectorParams>(current))
                {
                    ref var overrides = ref world.Get<PresenterVectorParams>(current);
                    if (overrides.TryGet(paramKey, out value)) return true;
                }
                if (requireVectorLane &&
                    ((world.Has<PresenterFloatDefaults>(current) && world.Get<PresenterFloatDefaults>(current).TryGet(paramKey, out _)) ||
                     (world.Has<PresenterIntDefaults>(current) && world.Get<PresenterIntDefaults>(current).TryGet(paramKey, out _))))
                    throw new System.InvalidOperationException($"PRESENTATION.ATTACHMENT.ERR.ParamLane: presenter={presenter.Id}, source={current.Id}, key={paramKey} requires Vector.");
                if (world.Has<PresenterVectorDefaults>(current))
                {
                    ref var defaults = ref world.Get<PresenterVectorDefaults>(current);
                    if (defaults.TryGet(paramKey, out value)) return true;
                }
                if (!world.Has<PresenterParent>(current)) break;
                current = world.Get<PresenterParent>(current).Parent;
            }
            value = default;
            return false;
        }
    }
}
