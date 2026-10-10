using System;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Scripting;

namespace InteractionShowcaseMod.Runtime
{
    /// <summary>
    /// Showcase cast-mode switching: each mode is an interaction context mounted under the battle
    /// context on the hero player's possessed rep; the Action mode also swaps the local camera to the shared
    /// follow profile.
    /// </summary>
    internal static class InteractionShowCastModes
    {
        private const string BattleContextId = "interaction.context.interaction.battle";

        private static readonly (string ModeId, string ContextId)[] Modes =
        {
            (InteractionShowcaseIds.WowModeId, "interaction.context.interaction.target_first"),
            (InteractionShowcaseIds.LolModeId, "interaction.context.interaction.smart_cast"),
            (InteractionShowcaseIds.Sc2ModeId, "interaction.context.interaction.aim_cast"),
            (InteractionShowcaseIds.IndicatorModeId, "interaction.context.interaction.indicator_cast"),
            (InteractionShowcaseIds.ActionModeId, "interaction.context.interaction.context_scored"),
        };

        public static bool TrySetActive(GameEngine engine, string? modeId)
        {
            string? targetContextId = ResolveContextId(modeId);
            if (targetContextId == null)
            {
                return false;
            }

            InteractionContextInstanceRuntime runtime = engine.GetService(CoreServiceKeys.InteractionContextInstances)
                ?? throw new InvalidOperationException("Interaction showcase cast modes require InteractionContextInstances.");
            InteractionContextProfileRegistry profiles = engine.GetService(CoreServiceKeys.InteractionContextProfileRegistry)
                ?? throw new InvalidOperationException("Interaction showcase cast modes require InteractionContextProfileRegistry.");
            if (!InteractionShowcaseRuntime.TryGetShowcaseLocalPlayerRep(engine, out Entity rep))
            {
                throw new InvalidOperationException("Interaction showcase cast modes require a local seat possessing the hero player rep.");
            }

            bool alreadyActive = false;
            for (int i = 0; i < Modes.Length; i++)
            {
                string contextId = Modes[i].ContextId;
                if (!runtime.IsActive(rep, profiles.ProfileIdRegistry.GetId(contextId)))
                {
                    continue;
                }

                if (string.Equals(contextId, targetContextId, StringComparison.Ordinal))
                {
                    alreadyActive = true;
                    continue;
                }

                runtime.Deactivate(rep, ConfigKeyRegistry.Register(contextId));
            }

            if (!alreadyActive)
            {
                runtime.Activate(rep, ConfigKeyRegistry.Register(targetContextId), ConfigKeyRegistry.Register(BattleContextId));
            }

            // The mode owns the camera on switch: Tactical for the casting modes; the Action
            // mode follows the command-source collection (the selected heroes), not the rep.
            if (string.Equals(modeId, InteractionShowcaseIds.ActionModeId, StringComparison.Ordinal))
            {
                engine.SetService(CoreServiceKeys.VirtualCameraRequest, new VirtualCameraRequest
                {
                    Id = "Camera.Profile.Follow",
                    FollowTargetKindOverride = CameraFollowTargetKind.EntityCollectionPrimary,
                    FollowCollectionOwnerOverride = rep,
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

        /// <summary>Active showcase mode id, or null when the battle context carries no mode context.</summary>
        public static string? GetActive(GameEngine engine)
        {
            InteractionContextInstanceRuntime? runtime = engine.GetService(CoreServiceKeys.InteractionContextInstances);
            InteractionContextProfileRegistry? profiles = engine.GetService(CoreServiceKeys.InteractionContextProfileRegistry);
            if (runtime == null || profiles == null || !InteractionShowcaseRuntime.TryGetShowcaseLocalPlayerRep(engine, out Entity rep))
            {
                return null;
            }

            for (int i = 0; i < Modes.Length; i++)
            {
                if (runtime.IsActive(rep, profiles.ProfileIdRegistry.GetId(Modes[i].ContextId)))
                {
                    return Modes[i].ModeId;
                }
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

        private static string? ResolveContextId(string? modeId)
        {
            for (int i = 0; i < Modes.Length; i++)
            {
                if (string.Equals(Modes[i].ModeId, modeId, StringComparison.Ordinal))
                {
                    return Modes[i].ContextId;
                }
            }

            return null;
        }
    }
}
