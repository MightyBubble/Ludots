using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace FourXDemoMod.Triggers
{
    public sealed class InstallFourXDemoOnGameStartTrigger : Trigger
    {
        private const string InstalledKey = "FourXDemoMod.Installed";
        private readonly IModContext _ctx;

        public InstallFourXDemoOnGameStartTrigger(IModContext ctx)
        {
            _ctx = ctx;
            EventKey = GameEvents.GameStart;
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            var engine = context.GetEngine();
            if (engine == null) return Task.CompletedTask;

            if (engine.GlobalContext.TryGetValue(InstalledKey, out var installedObj) &&
                installedObj is bool installed &&
                installed)
            {
                return Task.CompletedTask;
            }
            engine.GlobalContext[InstalledKey] = true;
            _ctx.Log("[FourXDemoMod] Ability definitions loaded via GAS/abilities.json");

            // ── Setup 4-faction asymmetric team relationships ──
            // Team 1: Empire (player), Team 2: Federation, Team 3: Horde, Team 4: Nomads
            // Empire↔Federation: Friendly (alliance)
            // Empire↔Horde: Hostile (war)
            // Empire↔Nomads: Neutral
            // Federation↔Horde: Hostile
            // Federation↔Nomads: Neutral
            // Horde↔Nomads: Hostile
            TeamManager.SetRelationshipSymmetric(1, 2, TeamRelationship.Friendly);
            TeamManager.SetRelationshipSymmetric(1, 3, TeamRelationship.Hostile);
            TeamManager.SetRelationshipSymmetric(1, 4, TeamRelationship.Neutral);
            TeamManager.SetRelationshipSymmetric(2, 3, TeamRelationship.Hostile);
            TeamManager.SetRelationshipSymmetric(2, 4, TeamRelationship.Neutral);
            TeamManager.SetRelationshipSymmetric(3, 4, TeamRelationship.Hostile);

            return Task.CompletedTask;
        }
    }
}

