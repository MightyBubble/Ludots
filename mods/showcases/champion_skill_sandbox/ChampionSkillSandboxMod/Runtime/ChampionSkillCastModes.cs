using System;
using Arch.Core;
using Ludots.Core.Client;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Scripting;

namespace ChampionSkillSandboxMod.Runtime
{
    /// <summary>
    /// Toolbar side of cast-mode switching: each sandbox mode is an interaction context mounted
    /// under the battle context on the possessed rep. Keyboard switching runs the same swap in
    /// the mode contexts' trigger graphs.
    /// </summary>
    internal static class ChampionSkillCastModes
    {
        private const string BattleContextId = "interaction.context.champion.battle";

        private static readonly (string ModeId, string ContextId)[] Modes =
        {
            (ChampionSkillSandboxIds.SmartCastModeId, "interaction.context.champion.smart_cast"),
            (ChampionSkillSandboxIds.IndicatorModeId, "interaction.context.champion.indicator_cast"),
            (ChampionSkillSandboxIds.PressReleaseModeId, "interaction.context.champion.press_release_cast"),
        };

        public static bool TrySetActive(GameEngine engine, string? modeId)
        {
            string? targetContextId = ResolveContextId(modeId);
            if (targetContextId == null)
            {
                return false;
            }

            InteractionContextInstanceRuntime runtime = engine.GetService(CoreServiceKeys.InteractionContextInstances)
                ?? throw new InvalidOperationException("ChampionSkillSandbox cast modes require InteractionContextInstances.");
            InteractionContextProfileRegistry profiles = engine.GetService(CoreServiceKeys.InteractionContextProfileRegistry)
                ?? throw new InvalidOperationException("ChampionSkillSandbox cast modes require InteractionContextProfileRegistry.");
            Entity rep = ClientLocalSeatAccess.RequireSolePossessedRep(engine);

            for (int i = 0; i < Modes.Length; i++)
            {
                string contextId = Modes[i].ContextId;
                if (!runtime.IsActive(rep, profiles.ProfileIdRegistry.GetId(contextId)))
                {
                    continue;
                }

                if (string.Equals(contextId, targetContextId, StringComparison.Ordinal))
                {
                    return true;
                }

                runtime.Deactivate(rep, ConfigKeyRegistry.Register(contextId));
            }

            runtime.Activate(rep, ConfigKeyRegistry.Register(targetContextId), ConfigKeyRegistry.Register(BattleContextId));
            return true;
        }

        /// <summary>Active sandbox mode id, or null when the battle context carries no mode context.</summary>
        public static string? GetActive(GameEngine engine)
        {
            InteractionContextInstanceRuntime? runtime = engine.GetService(CoreServiceKeys.InteractionContextInstances);
            InteractionContextProfileRegistry? profiles = engine.GetService(CoreServiceKeys.InteractionContextProfileRegistry);
            if (runtime == null || profiles == null || !ClientLocalSeatAccess.TryGetSolePossessedRep(engine, out Entity rep))
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
