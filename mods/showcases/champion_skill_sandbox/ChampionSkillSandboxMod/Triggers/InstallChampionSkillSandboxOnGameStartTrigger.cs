using System.Threading.Tasks;
using ChampionSkillSandboxMod.Runtime;
using ChampionSkillSandboxMod.Systems;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Gameplay.Teams;
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

        public InstallChampionSkillSandboxOnGameStartTrigger(
            IModContext context,
            ChampionSkillSandboxRuntime runtime)
        {
            _context = context;
            _runtime = runtime;
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
            engine.InsertSystemBeforeRequired<Physics2DSimulationSystem>(
                new ChampionSkillPhysicsAuthoringSystem(engine),
                SystemGroup.InputCollection);

            if (engine.GlobalContext.TryGetValue(CoreServiceKeys.OrderQueue.Name, out var ordersObj) &&
                ordersObj is OrderQueue orders)
            {
                engine.RegisterSystem(
                    new ChampionSkillCommandSnapshotCaptureSystem(engine, _runtime),
                    SystemGroup.InputCollection);

                if (engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue) is RuntimeEntitySpawnQueue spawnQueue)
                {
                    engine.RegisterSystem(
                        new ChampionSkillStressSpawnSystem(engine, spawnQueue),
                        SystemGroup.InputCollection);
                    engine.RegisterSystem(
                        new ChampionSkillStressCombatSystem(engine, orders),
                        SystemGroup.InputCollection);
                }
            }

            engine.RegisterSystem(new ChampionSkillSandboxInputPrepareSystem(engine, _runtime), SystemGroup.InputCollection);
            engine.RegisterPresentationSystem(new ChampionSkillSandboxPresentationSystem(engine, _runtime));

            _context.Log("[ChampionSkillSandboxMod] Command panel focus runtime registered.");
            return Task.CompletedTask;
        }
    }
}
