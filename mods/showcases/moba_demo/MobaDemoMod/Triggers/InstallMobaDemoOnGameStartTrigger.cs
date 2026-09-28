using System.Threading.Tasks;
using Arch.Core;
using CoreInputMod.ViewMode;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Mathematics;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Scripting;
using MobaDemoMod.Systems;

namespace MobaDemoMod.Triggers
{
    public sealed class InstallMobaDemoOnGameStartTrigger : Trigger
    {
        private const string InstalledKey = "MobaDemoMod.Installed";
        public const string MobaConfigKey = "MobaDemoMod.Config";

        private readonly IModContext _ctx;

        public InstallMobaDemoOnGameStartTrigger(IModContext ctx)
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

            var mobaConfig = MobaConfig.Load(_ctx);
            engine.GlobalContext[MobaConfigKey] = mobaConfig;
            _ctx.Log("[MobaDemoMod] MobaConfig loaded from assets/moba_config.json");

            engine.RegisterSystem(new MobaInputModeSystem(engine.World, engine.GlobalContext), SystemGroup.LocalInput);

            ViewModeRegistrar.RegisterFromVfs(_ctx, engine.GlobalContext, "Moba");

            // Unit rendering is defined by presenters.json and entity-scoped Marker3D rules.
            // Colors come from EntityColor instead of trigger-owned presentation logic.
            return Task.CompletedTask;
        }
    }
}
