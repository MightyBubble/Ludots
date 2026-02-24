using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.GAS
{
    /// <summary>
    /// Utility for building merged ConfigParams from template + caller overrides.
    /// Used by Effect processing systems before executing phase graphs/handlers.
    /// </summary>
    public static class ConfigParamsMerger
    {
        /// <summary>
        /// Build merged ConfigParams for an effect entity.
        /// If the entity carries a pre-merged <see cref="EffectConfigParams"/> component
        /// (attached at creation time), returns that directly.
        /// Otherwise falls back to the shared template params.
        /// </summary>
        public static EffectConfigParams BuildMergedConfig(
            World world,
            Entity effectEntity,
            in EffectConfigParams templateParams)
        {
            if (world.IsAlive(effectEntity) && world.Has<EffectConfigParams>(effectEntity))
            {
                return world.Get<EffectConfigParams>(effectEntity);
            }

            return templateParams;
        }

        /// <summary>
        /// Build merged ConfigParams from template + request-level CallerParams.
        /// Used for instant effects processed directly from EffectRequest (no entity).
        /// </summary>
        public static EffectConfigParams BuildMergedConfig(
            in EffectConfigParams templateParams,
            in EffectRequest request)
        {
            var merged = templateParams;

            if (request.HasCallerParams)
            {
                merged.MergeFrom(in request.CallerParams);
            }

            return merged;
        }
    }
}
