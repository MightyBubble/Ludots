using System;
using Arch.Core;
using Ludots.Core.Client;
using Ludots.Core.Engine;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Registry;
using Ludots.Core.Scripting;

namespace ChampionSkillSandboxMod.Runtime
{
    /// <summary>
    /// Toolbar side of cast-mode switching. Each sandbox mode is a cast-mode id on the
    /// possessed rep's InteractionPref. Keyboard switching writes the same field from
    /// the battle context's trigger graphs.
    /// </summary>
    internal static class ChampionSkillCastModes
    {
        private static readonly (string ModeId, string CastModeId)[] Modes =
        {
            (ChampionSkillSandboxIds.SmartCastModeId, "castMode.smart"),
            (ChampionSkillSandboxIds.IndicatorModeId, "castMode.aim_release"),
            (ChampionSkillSandboxIds.PressReleaseModeId, "castMode.aim_click"),
        };

        public static bool TrySetActive(GameEngine engine, string? modeId)
        {
            string? castModeName = ResolveCastModeName(modeId);
            if (castModeName == null)
            {
                return false;
            }

            StringIntRegistry registry = engine.GetService(CoreServiceKeys.CastModeIdRegistry)
                ?? throw new InvalidOperationException("ChampionSkillSandbox cast modes require CastModeIdRegistry.");
            if (!registry.TryGetId(castModeName, out int castModeId))
            {
                throw new InvalidOperationException(
                    $"ChampionSkillSandbox cast mode '{castModeName}' is not installed.");
            }

            Entity rep = ClientLocalSeatAccess.RequireSolePossessedRep(engine);
            if (!engine.World.TryGet(rep, out InteractionPref pref))
            {
                throw new InvalidOperationException(
                    "ChampionSkillSandbox cast modes require InteractionPref on the possessed rep.");
            }

            pref.SetDefaultCastMode(castModeId);
            engine.World.Set(rep, pref);
            return true;
        }

        /// <summary>
        /// Active sandbox mode id. Null when no rep is possessed yet. A possessed rep
        /// with an unseeded or unknown cast mode fails fast.
        /// </summary>
        public static string? GetActive(GameEngine engine)
        {
            if (!ClientLocalSeatAccess.TryGetSolePossessedRep(engine, out Entity rep))
            {
                return null;
            }

            StringIntRegistry registry = engine.GetService(CoreServiceKeys.CastModeIdRegistry)
                ?? throw new InvalidOperationException("ChampionSkillSandbox cast modes require CastModeIdRegistry.");
            if (!engine.World.TryGet(rep, out InteractionPref pref))
            {
                throw new InvalidOperationException(
                    "ChampionSkillSandbox cast modes require InteractionPref on the possessed rep.");
            }

            if (pref.DefaultCastModeId <= 0)
            {
                throw new InvalidOperationException(
                    "ChampionSkillSandbox cast mode is not seeded on the possessed rep.");
            }

            string name = registry.GetName(pref.DefaultCastModeId);
            for (int i = 0; i < Modes.Length; i++)
            {
                if (string.Equals(Modes[i].CastModeId, name, StringComparison.Ordinal))
                {
                    return Modes[i].ModeId;
                }
            }

            throw new InvalidOperationException(
                $"ChampionSkillSandbox possessed rep cast mode '{name}' is not a sandbox mode.");
        }

        private static string? ResolveCastModeName(string? modeId)
        {
            for (int i = 0; i < Modes.Length; i++)
            {
                if (string.Equals(Modes[i].ModeId, modeId, StringComparison.Ordinal))
                {
                    return Modes[i].CastModeId;
                }
            }

            return null;
        }
    }
}
