using System;
using Ludots.Core.Engine;
using Ludots.Core.Input.Orders;
using Ludots.Core.Scripting;

namespace ChampionSkillSandboxMod.Runtime
{
    /// <summary>
    /// Sandbox cast-mode switching over the engine's active input order mapping: the mode id
    /// (toolbar/keyboard vocabulary) maps to the mapping's interaction mode. Transitional home
    /// until the sandbox's order-mapping migration replaces the mapping system with graphs.
    /// </summary>
    internal static class ChampionSkillCastModes
    {
        public static bool TrySetActive(GameEngine engine, string? modeId)
        {
            if (!TryGetCastModeType(modeId, out var castMode))
            {
                return false;
            }

            if (engine.GlobalContext.TryGetValue(CoreServiceKeys.ActiveInputOrderMapping.Name, out var mappingObj) &&
                mappingObj is InputOrderMappingSystem mapping)
            {
                mapping.SetInteractionMode(castMode);
            }

            // All sandbox cast modes share the sandbox tactical camera; switching re-asserts it.
            engine.SetService(CoreServiceKeys.VirtualCameraRequest, new Ludots.Core.Gameplay.Camera.VirtualCameraRequest
            {
                Id = ChampionSkillSandboxIds.TacticalCameraId,
                ResetRuntimeState = true,
                ReplaceActiveStack = true
            });

            return true;
        }

        /// <summary>
        /// Current sandbox mode id, or null when no mapping is installed or the installed cast
        /// mode is foreign (another showcase's value) — the caller treats null as "reset to the
        /// sandbox default" instead of trusting a coincidental enum match.
        /// </summary>
        public static string? GetActive(GameEngine engine)
        {
            if (engine.GlobalContext.TryGetValue(CoreServiceKeys.ActiveInputOrderMapping.Name, out var mappingObj) &&
                mappingObj is InputOrderMappingSystem mapping &&
                TryGetModeId(mapping.InteractionMode, out string? modeId))
            {
                return modeId;
            }

            return null;
        }

        public static bool TryGetCastModeType(string? modeId, out CastModeType castMode)
        {
            switch (modeId)
            {
                case ChampionSkillSandboxIds.SmartCastModeId:
                    castMode = CastModeType.SmartCast;
                    return true;
                case ChampionSkillSandboxIds.IndicatorModeId:
                    castMode = CastModeType.SmartCastWithIndicator;
                    return true;
                case ChampionSkillSandboxIds.PressReleaseModeId:
                    castMode = CastModeType.PressReleaseAimCast;
                    return true;
                default:
                    castMode = default;
                    return false;
            }
        }

        private static bool TryGetModeId(CastModeType castMode, out string? modeId)
        {
            switch (castMode)
            {
                case CastModeType.SmartCast:
                    modeId = ChampionSkillSandboxIds.SmartCastModeId;
                    return true;
                case CastModeType.SmartCastWithIndicator:
                    modeId = ChampionSkillSandboxIds.IndicatorModeId;
                    return true;
                case CastModeType.PressReleaseAimCast:
                    modeId = ChampionSkillSandboxIds.PressReleaseModeId;
                    return true;
                default:
                    modeId = null;
                    return false;
            }
        }
    }
}
