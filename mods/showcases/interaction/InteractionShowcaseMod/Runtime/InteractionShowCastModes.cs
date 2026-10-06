using System;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Input.Orders;
using Ludots.Core.Scripting;

namespace InteractionShowcaseMod.Runtime
{
    /// <summary>
    /// Showcase cast-mode switching over the engine's active input order mapping: the mode id
    /// (toolbar/keyboard vocabulary) maps to the mapping's interaction mode; the Action mode also
    /// swaps the local camera to the shared follow profile. Transitional home until the showcase's
    /// order-mapping migration replaces the mapping system with graphs.
    /// </summary>
    internal static class InteractionShowCastModes
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

            // The mode owns the camera on switch: Tactical for the casting modes; the Action
            // mode follows the command-source collection (the selected heroes), not the rep.
            if (string.Equals(modeId, InteractionShowcaseIds.ActionModeId, StringComparison.OrdinalIgnoreCase))
            {
                engine.SetService(CoreServiceKeys.VirtualCameraRequest, new VirtualCameraRequest
                {
                    Id = "Camera.Profile.Follow",
                    FollowTargetKindOverride = CameraFollowTargetKind.EntityCollectionPrimary,
                    FollowCollectionOwnerOverride = ResolveSolePossessedRep(engine, modeId),
                    FollowCollectionKeyOverride = "collection.command.source",
                    ResetRuntimeState = true,
                    ReplaceActiveStack = true
                });
            }
            else
            {
                engine.SetService(CoreServiceKeys.VirtualCameraRequest, new VirtualCameraRequest
                {
                    Id = "Camera.Profile.Tactical",
                    ResetRuntimeState = true,
                    ReplaceActiveStack = true
                });
            }

            return true;
        }

        /// <summary>
        /// Current showcase mode id, or null when no mapping is installed or the installed cast
        /// mode is foreign (another showcase's value) — the caller treats null as "reset to the
        /// showcase default" instead of trusting a coincidental enum match.
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

        public static string GetDisplayName(string? modeId)
        {
            return modeId switch
            {
                InteractionShowcaseIds.WowModeId => "WoW / Target First",
                InteractionShowcaseIds.LolModeId => "LoL / Smart Cast",
                InteractionShowcaseIds.Sc2ModeId => "SC2 / Aim Cast",
                InteractionShowcaseIds.IndicatorModeId => "LoL / Indicator Release",
                InteractionShowcaseIds.ActionModeId => "Action / Context Scored",
                _ => "Unassigned"
            };
        }

        private static Arch.Core.Entity ResolveSolePossessedRep(GameEngine engine, string modeId)
        {
            if (!Ludots.Core.Client.ClientLocalSeatAccess.TryGetSolePossessedRep(engine, out var owner) ||
                owner == Arch.Core.Entity.Null ||
                !engine.World.IsAlive(owner))
            {
                throw new InvalidOperationException(
                    $"Interaction showcase mode '{modeId}' requires a live sole ClientLocalSeat possession for its collection follow camera.");
            }

            return owner;
        }

        private static bool TryGetCastModeType(string? modeId, out CastModeType castMode)
        {
            switch (modeId)
            {
                case InteractionShowcaseIds.WowModeId:
                    castMode = CastModeType.TargetFirst;
                    return true;
                case InteractionShowcaseIds.LolModeId:
                    castMode = CastModeType.SmartCast;
                    return true;
                case InteractionShowcaseIds.Sc2ModeId:
                    castMode = CastModeType.AimCast;
                    return true;
                case InteractionShowcaseIds.IndicatorModeId:
                    castMode = CastModeType.SmartCastWithIndicator;
                    return true;
                case InteractionShowcaseIds.ActionModeId:
                    castMode = CastModeType.ContextScored;
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
                case CastModeType.TargetFirst:
                    modeId = InteractionShowcaseIds.WowModeId;
                    return true;
                case CastModeType.SmartCast:
                    modeId = InteractionShowcaseIds.LolModeId;
                    return true;
                case CastModeType.AimCast:
                    modeId = InteractionShowcaseIds.Sc2ModeId;
                    return true;
                case CastModeType.SmartCastWithIndicator:
                    modeId = InteractionShowcaseIds.IndicatorModeId;
                    return true;
                case CastModeType.ContextScored:
                    modeId = InteractionShowcaseIds.ActionModeId;
                    return true;
                default:
                    modeId = null;
                    return false;
            }
        }
    }
}
