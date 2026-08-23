using System.Collections.Generic;
using System.Threading.Tasks;
using Arch.Core;
using CoreInputMod;
using CoreInputMod.Triggers;
using CoreInputMod.ViewMode;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Mathematics;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Scripting;
using MobaDemoMod.Systems;
using Ludots.Platform.Abstractions;

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

            if (engine.GlobalContext.TryGetValue(CoreServiceKeys.OrderQueue.Name, out var ordersObj) &&
                ordersObj is OrderQueue orders)
            {
                _ctx.Log("[MobaDemoMod] OrderQueue ready, registering local order source.");
                engine.RegisterSystem(new MobaLocalOrderSourceSystem(engine.World, engine.GlobalContext, orders, _ctx), SystemGroup.InputCollection);
            }

            ViewModeRegistrar.RegisterFromVfs(_ctx, engine.GlobalContext, "Moba");

            // Command-source acquisition feedback hooks are provided by CoreInputMod; MOBA injects only visual callbacks here.
            PresenterCommandBuffer cmdBuffer = null;
            if (engine.GlobalContext.TryGetValue(CoreServiceKeys.PresenterCommandBuffer.Name, out var cmdObj) && cmdObj is PresenterCommandBuffer pcb)
                cmdBuffer = pcb;

            if (CoreInputRuntimeServices.TryGetCommandSourceAcquiredCallbacks(engine, out List<System.Action<WorldCmInt2, Entity>> commandSourceAcquiredCallbacks))
            {
                var capturedCmdBuffer = cmdBuffer;
                var perfReg = context.Get(CoreServiceKeys.PresenterDefinitionRegistry) as PresenterDefinitionRegistry;
                int commandSourceIndicatorDefId = perfReg?.GetId(mobaConfig.Presentation.CommandSourceIndicatorDefKey) ?? 0;
                commandSourceAcquiredCallbacks.Add((worldCm, entity) =>
                {
                    if (capturedCmdBuffer == null) return;
                    capturedCmdBuffer.TryAdd(new PresenterCommand
                    {
                        CommandKind = PresenterCommandKind.DestroyPresenterScope,
                        ScopeTag = mobaConfig.Presentation.CommandSourceScopeId
                    });
                    if (engine.World.IsAlive(entity))
                    {
                        capturedCmdBuffer.TryAdd(new PresenterCommand
                        {
                            CommandKind = PresenterCommandKind.CreatePresenter,
                            PresenterDefinitionId = commandSourceIndicatorDefId,
                            ScopeTag = mobaConfig.Presentation.CommandSourceScopeId,
                            Source = entity
                        });
                    }
                });
            }

            // Unit rendering is defined by presenters.json and entity-scoped Marker3D rules.
            // Colors come from EntityColor instead of trigger-owned presentation logic.
            return Task.CompletedTask;
        }
    }
}
