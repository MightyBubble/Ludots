using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace RtsDemoMod.Triggers
{
    /// <summary>
    /// Registers RTS abilities and sets up 3-faction team relationships on game start.
    /// Covers: Ability Cost (multi-signal), PeriodicSearch aura, Search AOE, CreateUnit,
    ///         Buff with GrantedTags, StimPack (RequiredAll activation), 3-team asymmetric relations.
    /// </summary>
    public sealed class InstallRtsDemoOnGameStartTrigger : Trigger
    {
        private const string InstalledKey = "RtsDemoMod.Installed";
        private readonly IModContext _ctx;

        public InstallRtsDemoOnGameStartTrigger(IModContext ctx)
        {
            _ctx = ctx;
            EventKey = GameEvents.GameStart;
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            var engine = context.GetEngine();
            if (engine == null) return Task.CompletedTask;

            if (engine.GlobalContext.TryGetValue(InstalledKey, out var obj) && obj is bool b && b)
                return Task.CompletedTask;
            engine.GlobalContext[InstalledKey] = true;
            _ctx.Log("[RtsDemoMod] Ability definitions loaded via GAS/abilities.json");

            // ── Setup 3-faction team relationships ──
            // Team 1: Terran, Team 2: Zerg, Team 3: Protoss
            // All pairs are mutually hostile (asymmetric capable, but symmetric here).
            TeamManager.SetRelationshipSymmetric(1, 2, TeamRelationship.Hostile);
            TeamManager.SetRelationshipSymmetric(1, 3, TeamRelationship.Hostile);
            TeamManager.SetRelationshipSymmetric(2, 3, TeamRelationship.Hostile);

            return Task.CompletedTask;
        }
    }
}
