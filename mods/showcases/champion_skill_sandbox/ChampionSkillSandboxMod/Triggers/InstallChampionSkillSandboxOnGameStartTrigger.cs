using System.Threading.Tasks;
using ChampionSkillSandboxMod.Runtime;
using ChampionSkillSandboxMod.Systems;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Input.Selection;
using Ludots.Core.Modding;
using Ludots.Core.Physics2D.Ticking;
using Ludots.Core.Scripting;

namespace ChampionSkillSandboxMod.Triggers
{
    internal sealed class InstallChampionSkillSandboxOnGameStartTrigger : Trigger
    {
        private const string InstalledKey = "ChampionSkillSandboxMod.Installed";
        private readonly IModContext _context;
        private readonly ChampionSkillSandboxRuntime _runtime;
        private readonly ChampionSkillCastModeToolbarProvider _toolbarProvider;

        public InstallChampionSkillSandboxOnGameStartTrigger(
            IModContext context,
            ChampionSkillSandboxRuntime runtime,
            ChampionSkillCastModeToolbarProvider toolbarProvider)
        {
            _context = context;
            _runtime = runtime;
            _toolbarProvider = toolbarProvider;
            EventKey = GameEvents.GameStart;
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            GameEngine? engine = context.GetEngine();
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
            TeamManager.SetRelationshipSymmetric(1, 2, TeamRelationship.Hostile);
            var stressControl = new ChampionSkillStressControlState();
            var stressTelemetry = new ChampionSkillStressTelemetry();
            engine.GlobalContext[ChampionSkillStressControlState.GlobalKey] = stressControl;
            engine.GlobalContext[ChampionSkillStressTelemetry.GlobalKey] = stressTelemetry;
            engine.InsertSystemBeforeRequired<Physics2DSimulationSystem>(
                new ChampionSkillPhysicsAuthoringSystem(engine),
                SystemGroup.InputCollection);

            if (engine.GlobalContext.TryGetValue(CoreServiceKeys.OrderQueue.Name, out var ordersObj) &&
                ordersObj is OrderQueue orders)
            {
                engine.RegisterSystem(
                    new ChampionSkillSandboxLocalOrderSourceSystem(engine.World, engine.GlobalContext, orders, _context),
                    SystemGroup.InputCollection);
                engine.RegisterSystem(
                    new ChampionSkillCommandSnapshotCaptureSystem(engine, _runtime, orders),
                    SystemGroup.InputCollection);

                if (engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue) is RuntimeEntitySpawnQueue spawnQueue)
                {
                    engine.RegisterSystem(
                        new ChampionSkillStressSpawnSystem(engine, spawnQueue, stressControl, stressTelemetry),
                        SystemGroup.InputCollection);
                    engine.RegisterSystem(
                        new ChampionSkillStressCombatSystem(engine, orders, stressTelemetry),
                        SystemGroup.InputCollection);
                }
            }

            _toolbarProvider.Bind(engine);
            engine.SetService(CoreServiceKeys.EntityCommandPanelToolbarProvider, _toolbarProvider);
            engine.InsertSystemBeforeRequired<CurrentSelectionApplySystem>(
                new ChampionSkillSandboxInputPrepareSystem(engine, _runtime),
                SystemGroup.InputCollection);
            engine.RegisterPresentationSystem(new ChampionSkillSandboxPresentationSystem(engine, _runtime));

            _context.Log("[ChampionSkillSandboxMod] Local order source, command panel focus runtime, and cast mode toolbar registered.");
            return Task.CompletedTask;
        }
    }
}
