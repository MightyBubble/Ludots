using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Arch.Core;
using CoreInputMod.Triggers;
using InteractionShowcaseMod.Runtime;
using InteractionShowcaseMod.Systems;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Mathematics;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Scripting;

namespace InteractionShowcaseMod.Triggers
{
    internal sealed class InstallInteractionShowcaseOnGameStartTrigger : Trigger
    {
        private const string InstalledKey = "InteractionShowcaseMod.Installed";
        private readonly IModContext _ctx;
        private readonly InteractionShowcaseRuntime _runtime;
        private readonly InteractionShowcaseStressTelemetry _stressTelemetry;

        internal InstallInteractionShowcaseOnGameStartTrigger(
            IModContext ctx,
            InteractionShowcaseRuntime runtime,
            InteractionShowcaseStressTelemetry stressTelemetry)
        {
            _ctx = ctx;
            _runtime = runtime;
            _stressTelemetry = stressTelemetry;
            EventKey = GameEvents.GameStart;
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            var engine = context.GetEngine();
            if (engine == null)
            {
                return Task.CompletedTask;
            }

            if (engine.GlobalContext.TryGetValue(InstalledKey, out var installedObj) &&
                installedObj is bool installed &&
                installed)
            {
                return Task.CompletedTask;
            }

            engine.GlobalContext[InstalledKey] = true;
            engine.GlobalContext[InteractionShowcaseStressTelemetry.GlobalKey] = _stressTelemetry;
            TeamManager.SetRelationshipSymmetric(1, 2, TeamRelationship.Hostile);

            if (engine.GlobalContext.TryGetValue(CoreServiceKeys.OrderQueue.Name, out var ordersObj) &&
                ordersObj is OrderQueue orders)
            {
                engine.RegisterSystem(
                    new InteractionShowcaseLocalOrderSourceSystem(engine.World, engine.GlobalContext, orders, _ctx),
                    SystemGroup.InputCollection);
            }

            if (engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue) is not RuntimeEntitySpawnQueue spawnQueue)
            {
                throw new InvalidOperationException("InteractionShowcaseMod requires RuntimeEntitySpawnQueue for stress validation.");
            }

            if (engine.GetService(CoreServiceKeys.OrderQueue) is not OrderQueue stressOrders)
            {
                throw new InvalidOperationException("InteractionShowcaseMod requires OrderQueue for stress validation.");
            }

            engine.RegisterSystem(
                new InteractionShowcaseStressSystem(engine, spawnQueue, stressOrders, _stressTelemetry),
                SystemGroup.InputCollection);
            engine.RegisterPresentationSystem(new InteractionShowcasePanelPresentationSystem(engine, _runtime));

            if (engine.GlobalContext.TryGetValue(CoreServiceKeys.OrderTypeRegistry.Name, out var orderTypesObj) &&
                orderTypesObj is OrderTypeRegistry orderTypes)
            {
                var config = engine.GetService(CoreServiceKeys.GameConfig);
                engine.RegisterSystem(
                    new StopOrderSystem(engine.World, orderTypes, config.Constants.OrderTypeIds["stop"]),
                    SystemGroup.AbilityActivation);
                engine.RegisterSystem(
                    new AbilityMoveWorldCmSystem(engine.World, engine.EventBus, speedCmPerSec: 900f, stopRadiusCm: 40f),
                    SystemGroup.AbilityActivation);
            }

            WireSelectionFeedback(context, engine);
            _ctx.Log("[InteractionShowcaseMod] Local order source, move/stop runtime, and selection feedback registered.");
            return Task.CompletedTask;
        }

        private static void WireSelectionFeedback(ScriptContext context, GameEngine engine)
        {
            if (!engine.GlobalContext.TryGetValue(InstallCoreInputOnGameStartTrigger.EntitySelectionCallbacksKey, out var callbacksObj) ||
                callbacksObj is not List<Action<WorldCmInt2, Entity>> selectionCallbacks)
            {
                return;
            }

            if (context.Get(CoreServiceKeys.PresentationCommandBuffer) is not PresentationCommandBuffer commands)
            {
                return;
            }

            if (context.Get(CoreServiceKeys.PerformerDefinitionRegistry) is not PerformerDefinitionRegistry definitions)
            {
                return;
            }

            int selectionDefId = definitions.GetId(InteractionShowcaseIds.SelectionIndicatorDefId);
            if (selectionDefId <= 0)
            {
                return;
            }

            selectionCallbacks.Add((_, entity) =>
            {
                commands.TryAdd(new PresentationCommand
                {
                    Kind = PresentationCommandKind.DestroyPerformerScope,
                    IdA = InteractionShowcaseIds.SelectionScopeId
                });

                if (!engine.World.IsAlive(entity))
                {
                    return;
                }

                commands.TryAdd(new PresentationCommand
                {
                    Kind = PresentationCommandKind.CreatePerformer,
                    IdA = selectionDefId,
                    IdB = InteractionShowcaseIds.SelectionScopeId,
                    Source = entity
                });
            });
        }
    }
}
