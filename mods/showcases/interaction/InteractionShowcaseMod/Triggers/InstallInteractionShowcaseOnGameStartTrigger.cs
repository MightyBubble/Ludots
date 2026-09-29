using System;
using System.Threading.Tasks;
using Arch.Core;
using InteractionShowcaseMod.Runtime;
using InteractionShowcaseMod.Systems;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Mathematics;
using Ludots.Core.Modding;
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
            engine.RegisterSystem(
                new InteractionShowcaseSelectionDockSystem(engine, _runtime),
                SystemGroup.InputCollection);
            engine.RegisterPresentationSystem(new InteractionShowcasePanelPresentationSystem(engine, _runtime));

            _ctx.Log("[InteractionShowcaseMod] Stress runtime registered.");
            return Task.CompletedTask;
        }
    }
}
