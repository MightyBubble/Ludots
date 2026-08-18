using System;
using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using DesertStrikeShowcaseMod.Runtime;
using DesertStrikeShowcaseMod.Systems;

namespace DesertStrikeShowcaseMod.Triggers
{
    public sealed class InstallDesertStrikeOnGameStartTrigger : Trigger
    {
        private const string InstalledKey = "DesertStrikeShowcaseMod.Installed";
        public const string StateKey = "DesertStrikeShowcaseMod.State";
        public const string ConfigKey = "DesertStrikeShowcaseMod.Config";
        public const string HudPanelRuntimeKey = "DesertStrikeShowcaseMod.HudPanelRuntime";

        private readonly IModContext _ctx;

        public InstallDesertStrikeOnGameStartTrigger(IModContext ctx)
        {
            _ctx = ctx;
            EventKey = GameEvents.GameStart;
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            var engine = context.Get(CoreServiceKeys.Engine);
            if (engine == null)
            {
                return Task.CompletedTask;
            }

            if (engine.GlobalContext.TryGetValue(InstalledKey, out var installed) && installed is bool flag && flag)
            {
                return Task.CompletedTask;
            }

            engine.GlobalContext[InstalledKey] = true;

            var config = DesertStrikeConfig.Load(_ctx);
            engine.GlobalContext[ConfigKey] = config;

            var state = new DesertStrikeState();
            engine.GlobalContext[StateKey] = state;
            state.WaveReceiptChannelId = engine.GetService(CoreServiceKeys.RuntimeEntitySpawnReceiptChannelRegistry)
                .Register("desert_strike_wave");

            TeamManager.SetRelationshipSymmetric(1, 2, TeamRelationship.Hostile);

            var hudPanelRuntime = new DesertStrikeHudPanelRuntime(engine, state, config, _ctx);
            engine.GlobalContext[HudPanelRuntimeKey] = hudPanelRuntime;

            // SystemCapability("desert-strike.showcase-systems")
            engine.RegisterSystem(new DesertStrikeWaveSystem(engine, state, config), SystemGroup.PostMovement);
            engine.RegisterSystem(new DesertStrikeAutoBattleSystem(engine, state), SystemGroup.PostMovement);
            engine.RegisterSystem(new DesertStrikeIncomeSystem(engine, state, config), SystemGroup.PostMovement);
            engine.RegisterSystem(new DesertStrikePurchaseSystem(engine, state, config), SystemGroup.EffectProcessing);
            engine.RegisterSystem(new DesertStrikeDeathSystem(engine, state), SystemGroup.PostMovement);
            engine.RegisterSystem(new DesertStrikeAiPlayerSystem(engine, state, config), SystemGroup.PostMovement);
            engine.RegisterPresentationSystem(new DesertStrikeHudSystem(engine, hudPanelRuntime));
            _ctx.Log("[DesertStrikeShowcaseMod] Desert Strike systems registered");
            return Task.CompletedTask;
        }
    }
}
