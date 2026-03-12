using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Navigation2D.Config;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Navigation2D.Runtime;
using Ludots.Core.Scripting;
using Navigation2DPlaygroundMod.Input;

namespace Navigation2DPlaygroundMod.Systems
{
    public sealed class Navigation2DPlaygroundControlSystem : ISystem<float>
    {
        private static readonly QueryDescription ScenarioQuery = new QueryDescription()
            .WithAll<NavPlaygroundTeam>();

        private static readonly QueryDescription FlowGoalQuery = new QueryDescription()
            .WithAll<NavFlowGoal2D>();

        private static readonly QueryDescription DynamicAgentsQuery = new QueryDescription()
            .WithAll<NavPlaygroundTeam>()
            .WithNone<NavPlaygroundBlocker>();

        private static readonly QueryDescription BlockerQuery = new QueryDescription()
            .WithAll<NavPlaygroundBlocker>();

        private readonly GameEngine _engine;
        private readonly World _world;

        public Navigation2DPlaygroundControlSystem(GameEngine engine)
        {
            _engine = engine;
            _world = engine.World;
        }

        public void Initialize()
        {
        }

        public void BeforeUpdate(in float t)
        {
        }

        public void Update(in float deltaTime)
        {
            if (!Navigation2DPlaygroundState.Enabled)
            {
                return;
            }

            if (_engine.GetService(CoreServiceKeys.AuthoritativeInput) is not IInputActionReader input)
            {
                return;
            }

            if (_engine.GlobalContext.TryGetValue(CoreServiceKeys.Navigation2DRuntime.Name, out var runtimeObj) &&
                runtimeObj is Navigation2DRuntime navRuntime)
            {
                HandlePressed(input, Navigation2DPlaygroundInputActions.ToggleFlowEnabled, () => navRuntime.FlowEnabled = !navRuntime.FlowEnabled);
                HandlePressed(input, Navigation2DPlaygroundInputActions.ToggleFlowDebug, () => navRuntime.FlowDebugEnabled = !navRuntime.FlowDebugEnabled);
                HandlePressed(input, Navigation2DPlaygroundInputActions.CycleFlowDebugMode, () => navRuntime.FlowDebugMode = (navRuntime.FlowDebugMode + 1) % 3);
                HandlePressed(input, Navigation2DPlaygroundInputActions.IncreaseFlowIterations, () => navRuntime.FlowIterationsPerTick = Math.Clamp(navRuntime.FlowIterationsPerTick + 512, 0, 131072));
                HandlePressed(input, Navigation2DPlaygroundInputActions.DecreaseFlowIterations, () => navRuntime.FlowIterationsPerTick = Math.Clamp(navRuntime.FlowIterationsPerTick - 512, 0, 131072));
            }

            HandlePressed(input, Navigation2DPlaygroundInputActions.IncreaseAgentsPerTeam, () => AdjustAgentsPerTeam(_engine, GetPlaygroundConfig(_engine).AgentsPerTeamStep));
            HandlePressed(input, Navigation2DPlaygroundInputActions.DecreaseAgentsPerTeam, () => AdjustAgentsPerTeam(_engine, -GetPlaygroundConfig(_engine).AgentsPerTeamStep));
            HandlePressed(input, Navigation2DPlaygroundInputActions.PreviousScenario, () => PreviousScenario(_engine));
            HandlePressed(input, Navigation2DPlaygroundInputActions.NextScenario, () => NextScenario(_engine));
            HandlePressed(input, Navigation2DPlaygroundInputActions.ResetScenario, () => RespawnScenario(_engine));
        }

        public void AfterUpdate(in float t)
        {
        }

        public void Dispose()
        {
        }

        public static void PublishScenarioServices(
            GameEngine engine,
            Navigation2DPlaygroundConfig playgroundConfig,
            Navigation2DPlaygroundSpawnSummary summary,
            int agentsPerTeam,
            int scenarioIndex)
        {
            engine.SetService(Navigation2DPlaygroundKeys.AgentsPerTeam, agentsPerTeam);
            engine.SetService(Navigation2DPlaygroundKeys.LiveAgentsTotal, summary.DynamicAgents);
            engine.SetService(Navigation2DPlaygroundKeys.BlockerCount, summary.BlockerCount);
            engine.SetService(Navigation2DPlaygroundKeys.ScenarioIndex, scenarioIndex);
            engine.SetService(Navigation2DPlaygroundKeys.ScenarioCount, playgroundConfig.Scenarios.Count);
            engine.SetService(Navigation2DPlaygroundKeys.ScenarioTeamCount, summary.TeamCount);
            engine.SetService(Navigation2DPlaygroundKeys.ScenarioId, summary.ScenarioId);
            engine.SetService(Navigation2DPlaygroundKeys.ScenarioName, summary.ScenarioName);
            engine.SetService(Navigation2DPlaygroundKeys.SpawnBatch, Navigation2DPlaygroundState.SpawnBatch);
            engine.SetService(Navigation2DPlaygroundKeys.FlowDebugLines, 0);
        }

        public static Navigation2DPlaygroundConfig GetPlaygroundConfig(GameEngine engine)
        {
            GameConfig? gameConfig = engine.GetService(CoreServiceKeys.GameConfig);
            return Navigation2DPlaygroundScenarioSpawner.GetPlaygroundConfig(gameConfig);
        }

        public static bool HasScenarioEntities(World world)
        {
            return world.CountEntities(in ScenarioQuery) > 0;
        }

        public static void EnsureScenarioLoaded(GameEngine engine)
        {
            if (HasScenarioEntities(engine.World))
            {
                UpdateLiveCounts(engine);
                return;
            }

            RespawnScenario(engine);
        }

        public static void AdjustAgentsPerTeam(GameEngine engine, int delta)
        {
            var playgroundConfig = GetPlaygroundConfig(engine);
            var scenario = Navigation2DPlaygroundScenarioSpawner.GetScenario(playgroundConfig, Navigation2DPlaygroundState.CurrentScenarioIndex);
            int maxAgentsPerTeam = GetMaxAgentsPerTeam(engine, scenario.TeamCount);
            int next = Navigation2DPlaygroundState.AgentsPerTeam + delta;
            if (next < 0)
            {
                next = 0;
            }

            if (next > maxAgentsPerTeam)
            {
                next = maxAgentsPerTeam;
            }

            if (next == Navigation2DPlaygroundState.AgentsPerTeam)
            {
                return;
            }

            Navigation2DPlaygroundState.AgentsPerTeam = next;
            RespawnScenario(engine);
        }

        public static void PreviousScenario(GameEngine engine)
        {
            var playgroundConfig = GetPlaygroundConfig(engine);
            Navigation2DPlaygroundState.CurrentScenarioIndex = Navigation2DPlaygroundScenarioSpawner.ClampScenarioIndex(
                playgroundConfig,
                Navigation2DPlaygroundState.CurrentScenarioIndex - 1);
            ClampAgentsPerTeamForCurrentScenario(engine, playgroundConfig);
            RespawnScenario(engine);
        }

        public static void NextScenario(GameEngine engine)
        {
            var playgroundConfig = GetPlaygroundConfig(engine);
            Navigation2DPlaygroundState.CurrentScenarioIndex = Navigation2DPlaygroundScenarioSpawner.ClampScenarioIndex(
                playgroundConfig,
                Navigation2DPlaygroundState.CurrentScenarioIndex + 1);
            ClampAgentsPerTeamForCurrentScenario(engine, playgroundConfig);
            RespawnScenario(engine);
        }

        public static void RespawnScenario(GameEngine engine)
        {
            World world = engine.World;
            world.Destroy(in ScenarioQuery);
            world.Destroy(in FlowGoalQuery);

            var playgroundConfig = GetPlaygroundConfig(engine);
            Navigation2DPlaygroundState.CurrentScenarioIndex = Navigation2DPlaygroundScenarioSpawner.ClampScenarioIndex(
                playgroundConfig,
                Navigation2DPlaygroundState.CurrentScenarioIndex);
            ClampAgentsPerTeamForCurrentScenario(engine, playgroundConfig);
            Navigation2DPlaygroundState.SpawnBatch = ClampSpawnBatch(playgroundConfig, Navigation2DPlaygroundState.SpawnBatch);

            var scenario = Navigation2DPlaygroundScenarioSpawner.GetScenario(playgroundConfig, Navigation2DPlaygroundState.CurrentScenarioIndex);
            var summary = Navigation2DPlaygroundScenarioSpawner.SpawnScenario(world, scenario, Navigation2DPlaygroundState.AgentsPerTeam);
            PublishScenarioServices(
                engine,
                playgroundConfig,
                summary,
                Navigation2DPlaygroundState.AgentsPerTeam,
                Navigation2DPlaygroundState.CurrentScenarioIndex);
        }

        public static void UpdateLiveCounts(GameEngine engine)
        {
            int liveAgents = engine.World.CountEntities(in DynamicAgentsQuery);
            int blockers = engine.World.CountEntities(in BlockerQuery);
            engine.SetService(Navigation2DPlaygroundKeys.LiveAgentsTotal, liveAgents);
            engine.SetService(Navigation2DPlaygroundKeys.BlockerCount, blockers);
            engine.SetService(Navigation2DPlaygroundKeys.SpawnBatch, Navigation2DPlaygroundState.SpawnBatch);
        }

        public static int ClampSpawnBatch(Navigation2DPlaygroundConfig playgroundConfig, int spawnBatch)
        {
            int max = Math.Max(1, playgroundConfig.DefaultAgentsPerTeam);
            if (spawnBatch < 1)
            {
                return Math.Min(max, Math.Max(1, playgroundConfig.DefaultSpawnBatch));
            }

            return Math.Min(max, spawnBatch);
        }

        public static void AdjustSpawnBatch(GameEngine engine, int delta)
        {
            var playgroundConfig = GetPlaygroundConfig(engine);
            int next = Navigation2DPlaygroundState.SpawnBatch + delta;
            Navigation2DPlaygroundState.SpawnBatch = ClampSpawnBatch(playgroundConfig, next);
            engine.SetService(Navigation2DPlaygroundKeys.SpawnBatch, Navigation2DPlaygroundState.SpawnBatch);
        }

        private static void ClampAgentsPerTeamForCurrentScenario(GameEngine engine, Navigation2DPlaygroundConfig playgroundConfig)
        {
            var scenario = Navigation2DPlaygroundScenarioSpawner.GetScenario(playgroundConfig, Navigation2DPlaygroundState.CurrentScenarioIndex);
            int maxAgentsPerTeam = GetMaxAgentsPerTeam(engine, scenario.TeamCount);
            if (Navigation2DPlaygroundState.AgentsPerTeam > maxAgentsPerTeam)
            {
                Navigation2DPlaygroundState.AgentsPerTeam = maxAgentsPerTeam;
            }
        }

        private static int GetMaxAgentsPerTeam(GameEngine engine, int teamCount)
        {
            GameConfig? gameConfig = engine.GetService(CoreServiceKeys.GameConfig);
            int maxAgents = (gameConfig?.Navigation2D ?? new Navigation2DConfig()).CloneValidated().MaxAgents;
            return Math.Max(0, maxAgents / Math.Max(1, teamCount));
        }

        private static void HandlePressed(IInputActionReader input, string actionId, Action onPressed)
        {
            if (input.PressedThisFrame(actionId))
            {
                onPressed();
            }
        }
    }
}
