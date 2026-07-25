using System.Collections.Generic;
using System.Threading.Tasks;
using Arch.Core;
using CoreInputMod;
using CoreInputMod.Triggers;
using CoreInputMod.ViewMode;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Mathematics;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Scripting;
using MobaDemoMod.GAS;
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
            _ctx.Log("[MobaDemoMod] MobaConfig loaded from assets/Configs/moba_config.json");

            var config = engine.GetService(CoreServiceKeys.GameConfig);
            _ = config.Constants.OrderTypeIds["stop"];

            if (engine.GlobalContext.TryGetValue(CoreServiceKeys.OrderQueue.Name, out var ordersObj) &&
                ordersObj is OrderQueue orders)
            {
                _ctx.Log("[MobaDemoMod] OrderQueue ready, registering local order source.");
                engine.RegisterSystem(new MobaLocalOrderSourceSystem(engine.World, engine.GlobalContext, orders, _ctx), SystemGroup.LocalInput);
            }

            ViewModeRegistrar.RegisterFromVfs(_ctx, engine.GlobalContext, "Moba");

            if (engine.GlobalContext.TryGetValue(CoreServiceKeys.OrderTypeRegistry.Name, out var registryObj) &&
                registryObj is OrderTypeRegistry orderTypeRegistry)
            {
                int navMoveAbilityTagId = TagRegistry.Register(MobaDemoAbilityDefinitions.Move);
                engine.RegisterSystem(new StopOrderNavMoveCleanupSystem(engine.World, config.Constants.OrderTypeIds["stop"], navMoveAbilityTagId), SystemGroup.AbilityActivation);
                engine.RegisterSystem(new Ludots.Core.Gameplay.GAS.Systems.StopOrderSystem(engine.World, orderTypeRegistry, config.Constants.OrderTypeIds["stop"]), SystemGroup.AbilityActivation);
                engine.RegisterSystem(new Ludots.Core.Gameplay.GAS.Systems.AbilityMoveWorldCmSystem(engine.World, engine.EventBus, mobaConfig.Movement.SpeedCmPerSec, mobaConfig.Movement.StopRadiusCm), SystemGroup.AbilityActivation);
            }

            // Command-source acquisition feedback hooks are provided by CoreInputMod; MOBA injects only visual callbacks here.
            PerformerCommandBuffer cmdBuffer = null;
            if (engine.GlobalContext.TryGetValue(CoreServiceKeys.PerformerCommandBuffer.Name, out var cmdObj) && cmdObj is PerformerCommandBuffer pcb)
                cmdBuffer = pcb;

            if (CoreInputRuntimeServices.TryGetCommandSourceAcquiredCallbacks(engine, out List<System.Action<WorldCmInt2, Entity>> commandSourceAcquiredCallbacks))
            {
                var capturedCmdBuffer = cmdBuffer;
                var perfReg = context.Get(CoreServiceKeys.PerformerDefinitionRegistry) as PerformerDefinitionRegistry;
                int commandSourceIndicatorDefId = perfReg?.GetId(mobaConfig.Presentation.CommandSourceIndicatorDefKey) ?? 0;
                commandSourceAcquiredCallbacks.Add((worldCm, entity) =>
                {
                    if (capturedCmdBuffer == null) return;
                    capturedCmdBuffer.TryAdd(new PerformerCommand
                    {
                        CommandKind = PerformerCommandKind.DestroyPerformerScope,
                        ScopeTag = mobaConfig.Presentation.CommandSourceScopeId
                    });
                    if (engine.World.IsAlive(entity))
                    {
                        capturedCmdBuffer.TryAdd(new PerformerCommand
                        {
                            CommandKind = PerformerCommandKind.CreatePerformer,
                            PerformerDefinitionId = commandSourceIndicatorDefId,
                            ScopeTag = mobaConfig.Presentation.CommandSourceScopeId,
                            Source = entity
                        });
                    }
                });
            }

            // Unit rendering is defined by performers.json and entity-scoped Marker3D rules.
            // Colors come from EntityColor instead of trigger-owned presentation logic.
            return Task.CompletedTask;
        }
    }
}
