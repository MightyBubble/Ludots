using System;
using System.Threading.Tasks;
using Arch.Core;
using CoreInputMod.ViewMode;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.Selection;
using Ludots.Core.Modding;
using Ludots.Core.Navigation2D.Runtime;
using Ludots.Core.Physics2D.Systems;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.DebugDraw;
using Ludots.Core.Scripting;
using Ludots.UI;
using Navigation2DPlaygroundMod.Input;
using Navigation2DPlaygroundMod.Systems;
using Navigation2DPlaygroundMod.UI;

namespace Navigation2DPlaygroundMod.Runtime
{
    internal sealed class Navigation2DPlaygroundRuntime
    {
        private static readonly QueryDescription LocalPlayerQuery = new QueryDescription().WithAll<PlayerOwner>();

        private readonly IModContext _context;
        private readonly Navigation2DPlaygroundPanelController _panelController = new();
        private bool _systemsInstalled;
        private bool _inputContextActive;
        private bool _stateInitialized;

        public Navigation2DPlaygroundRuntime(IModContext context)
        {
            _context = context;
        }

        public void EnsureSystemsInstalled(GameEngine engine)
        {
            if (_systemsInstalled)
            {
                return;
            }

            if (engine.GlobalContext.TryGetValue(CoreServiceKeys.Navigation2DRuntime.Name, out var runtimeObj) &&
                runtimeObj is Navigation2DRuntime navRuntime)
            {
                navRuntime.FlowEnabled = true;
            }
            else
            {
                throw new InvalidOperationException("Navigation2DPlaygroundMod requires Navigation2DRuntime to be available.");
            }

            var debugDrawBuffer = engine.GetService(CoreServiceKeys.DebugDrawCommandBuffer) ?? new DebugDrawCommandBuffer();
            engine.SetService(CoreServiceKeys.DebugDrawCommandBuffer, debugDrawBuffer);

            engine.RegisterSystem(new Physics2DToWorldPositionSyncSystem(engine.World), SystemGroup.PostMovement);
            engine.RegisterSystem(new IntegrationSystem2D(engine.World), SystemGroup.InputCollection);
            engine.RegisterSystem(new Navigation2DPlaygroundControlSystem(engine), SystemGroup.InputCollection);
            engine.RegisterSystem(new Navigation2DPlaygroundSelectionFilterSystem(engine), SystemGroup.InputCollection);
            engine.RegisterSystem(new Navigation2DPlaygroundCommandSystem(engine), SystemGroup.InputCollection);

            var meshRegistry = engine.GetService(CoreServiceKeys.PresentationMeshAssetRegistry)
                ?? throw new InvalidOperationException("PresentationMeshAssetRegistry is required for Navigation2DPlaygroundMod.");
            engine.RegisterPresentationSystem(new Navigation2DPlaygroundPresentationSystem(engine, debugDrawBuffer, meshRegistry));
            engine.RegisterPresentationSystem(new Navigation2DPlaygroundSelectionOverlaySystem(engine));
            engine.RegisterPresentationSystem(new Navigation2DPlaygroundPanelPresentationSystem(engine, this));

            _systemsInstalled = true;
            _context.Log("[Navigation2DPlaygroundMod] Installed playable runtime systems.");
        }

        public Task HandleMapFocusedAsync(ScriptContext context)
        {
            var engine = context.GetEngine();
            if (engine == null)
            {
                return Task.CompletedTask;
            }

            string? mapId = context.Get(CoreServiceKeys.MapId).Value;
            if (!Navigation2DPlaygroundIds.IsPlaygroundMap(mapId))
            {
                Navigation2DPlaygroundState.Enabled = false;
                TryPopInputContext(engine);
                ClearOwnedViewMode(engine);
                ClearPanelIfOwned(engine);
                return Task.CompletedTask;
            }

            EnsureSystemsInstalled(engine);
            EnsureInitialState(engine);
            EnsureLocalPlayerEntity(engine);
            Navigation2DPlaygroundState.Enabled = true;
            EnsurePlaygroundInputContext(engine);
            EnsureOwnedViewMode(engine);
            Navigation2DPlaygroundControlSystem.EnsureScenarioLoaded(engine);
            RefreshPanel(engine);
            return Task.CompletedTask;
        }

        public Task HandleMapUnloadedAsync(ScriptContext context)
        {
            var mapId = context.Get(CoreServiceKeys.MapId);
            if (!Navigation2DPlaygroundIds.IsPlaygroundMap(mapId.Value))
            {
                return Task.CompletedTask;
            }

            var engine = context.GetEngine();
            if (engine != null)
            {
                Navigation2DPlaygroundState.Enabled = false;
                TryPopInputContext(engine);
                ClearOwnedViewMode(engine);
                ClearPanelIfOwned(engine);
            }

            return Task.CompletedTask;
        }

        public void RefreshPanel(GameEngine engine)
        {
            if (engine.GetService(CoreServiceKeys.UIRoot) is not UIRoot root)
            {
                return;
            }

            string? activeMapId = engine.CurrentMapSession?.MapId.Value;
            if (!Navigation2DPlaygroundIds.IsPlaygroundMap(activeMapId))
            {
                ClearPanelIfOwned(engine);
                return;
            }

            _panelController.MountOrSync(root, engine);
        }

        private void EnsureInitialState(GameEngine engine)
        {
            if (_stateInitialized)
            {
                Navigation2DPlaygroundState.SpawnBatch = Navigation2DPlaygroundControlSystem.ClampSpawnBatch(
                    Navigation2DPlaygroundControlSystem.GetPlaygroundConfig(engine),
                    Navigation2DPlaygroundState.SpawnBatch);
                return;
            }

            GameConfig? gameConfig = engine.GetService(CoreServiceKeys.GameConfig);
            var playgroundConfig = Navigation2DPlaygroundScenarioSpawner.GetPlaygroundConfig(gameConfig);
            Navigation2DPlaygroundState.AgentsPerTeam = playgroundConfig.DefaultAgentsPerTeam;
            Navigation2DPlaygroundState.CurrentScenarioIndex = playgroundConfig.DefaultScenarioIndex;
            Navigation2DPlaygroundState.SpawnBatch = playgroundConfig.DefaultSpawnBatch;
            Navigation2DPlaygroundState.ToolMode = Navigation2DPlaygroundToolMode.Move;
            _stateInitialized = true;
        }

        private void EnsurePlaygroundInputContext(GameEngine engine)
        {
            if (engine.GetService(CoreServiceKeys.InputHandler) is not PlayerInputHandler input)
            {
                return;
            }

            if (_inputContextActive)
            {
                return;
            }

            EnsurePlaygroundInputSchema(input);
            input.PushContext(Navigation2DPlaygroundInputContexts.Playground);
            _inputContextActive = true;
        }

        private void TryPopInputContext(GameEngine engine)
        {
            if (!_inputContextActive || engine.GetService(CoreServiceKeys.InputHandler) is not PlayerInputHandler input)
            {
                return;
            }

            input.PopContext(Navigation2DPlaygroundInputContexts.Playground);
            _inputContextActive = false;
        }

        private void EnsureOwnedViewMode(GameEngine engine)
        {
            if (!engine.GlobalContext.TryGetValue(ViewModeManager.GlobalKey, out var managerObj) ||
                managerObj is not ViewModeManager manager)
            {
                return;
            }

            if (!Navigation2DPlaygroundIds.IsOwnedViewMode(manager.ActiveMode?.Id))
            {
                manager.SwitchTo(Navigation2DPlaygroundIds.CommandModeId);
            }
        }

        private void ClearOwnedViewMode(GameEngine engine)
        {
            if (!engine.GlobalContext.TryGetValue(ViewModeManager.GlobalKey, out var managerObj) ||
                managerObj is not ViewModeManager manager)
            {
                return;
            }

            if (Navigation2DPlaygroundIds.IsOwnedViewMode(manager.ActiveMode?.Id))
            {
                manager.ClearActiveMode();
            }
        }

        private void ClearPanelIfOwned(GameEngine engine)
        {
            if (engine.GetService(CoreServiceKeys.UIRoot) is not UIRoot root)
            {
                return;
            }

            _panelController.ClearIfOwned(root);
        }

        private static void EnsurePlaygroundInputSchema(PlayerInputHandler input)
        {
            if (!input.HasContext(Navigation2DPlaygroundInputContexts.Playground)) throw new InvalidOperationException($"Missing input context: {Navigation2DPlaygroundInputContexts.Playground}");
            if (!input.HasAction(Navigation2DPlaygroundInputActions.ToggleFlowEnabled)) throw new InvalidOperationException($"Missing input action: {Navigation2DPlaygroundInputActions.ToggleFlowEnabled}");
            if (!input.HasAction(Navigation2DPlaygroundInputActions.ToggleFlowDebug)) throw new InvalidOperationException($"Missing input action: {Navigation2DPlaygroundInputActions.ToggleFlowDebug}");
            if (!input.HasAction(Navigation2DPlaygroundInputActions.CycleFlowDebugMode)) throw new InvalidOperationException($"Missing input action: {Navigation2DPlaygroundInputActions.CycleFlowDebugMode}");
            if (!input.HasAction(Navigation2DPlaygroundInputActions.IncreaseFlowIterations)) throw new InvalidOperationException($"Missing input action: {Navigation2DPlaygroundInputActions.IncreaseFlowIterations}");
            if (!input.HasAction(Navigation2DPlaygroundInputActions.DecreaseFlowIterations)) throw new InvalidOperationException($"Missing input action: {Navigation2DPlaygroundInputActions.DecreaseFlowIterations}");
            if (!input.HasAction(Navigation2DPlaygroundInputActions.IncreaseAgentsPerTeam)) throw new InvalidOperationException($"Missing input action: {Navigation2DPlaygroundInputActions.IncreaseAgentsPerTeam}");
            if (!input.HasAction(Navigation2DPlaygroundInputActions.DecreaseAgentsPerTeam)) throw new InvalidOperationException($"Missing input action: {Navigation2DPlaygroundInputActions.DecreaseAgentsPerTeam}");
            if (!input.HasAction(Navigation2DPlaygroundInputActions.PreviousScenario)) throw new InvalidOperationException($"Missing input action: {Navigation2DPlaygroundInputActions.PreviousScenario}");
            if (!input.HasAction(Navigation2DPlaygroundInputActions.NextScenario)) throw new InvalidOperationException($"Missing input action: {Navigation2DPlaygroundInputActions.NextScenario}");
            if (!input.HasAction(Navigation2DPlaygroundInputActions.ResetScenario)) throw new InvalidOperationException($"Missing input action: {Navigation2DPlaygroundInputActions.ResetScenario}");
            if (!input.HasAction(Navigation2DPlaygroundInputActions.ToolMove)) throw new InvalidOperationException($"Missing input action: {Navigation2DPlaygroundInputActions.ToolMove}");
            if (!input.HasAction(Navigation2DPlaygroundInputActions.ToolSpawnTeam0)) throw new InvalidOperationException($"Missing input action: {Navigation2DPlaygroundInputActions.ToolSpawnTeam0}");
            if (!input.HasAction(Navigation2DPlaygroundInputActions.ToolSpawnTeam1)) throw new InvalidOperationException($"Missing input action: {Navigation2DPlaygroundInputActions.ToolSpawnTeam1}");
            if (!input.HasAction(Navigation2DPlaygroundInputActions.ToolSpawnBlocker)) throw new InvalidOperationException($"Missing input action: {Navigation2DPlaygroundInputActions.ToolSpawnBlocker}");
            if (!input.HasAction(Navigation2DPlaygroundInputActions.IncreaseSpawnBatch)) throw new InvalidOperationException($"Missing input action: {Navigation2DPlaygroundInputActions.IncreaseSpawnBatch}");
            if (!input.HasAction(Navigation2DPlaygroundInputActions.DecreaseSpawnBatch)) throw new InvalidOperationException($"Missing input action: {Navigation2DPlaygroundInputActions.DecreaseSpawnBatch}");
            if (!input.HasAction(Navigation2DPlaygroundInputActions.ViewModeCommand)) throw new InvalidOperationException($"Missing input action: {Navigation2DPlaygroundInputActions.ViewModeCommand}");
            if (!input.HasAction(Navigation2DPlaygroundInputActions.ViewModeFollow)) throw new InvalidOperationException($"Missing input action: {Navigation2DPlaygroundInputActions.ViewModeFollow}");
        }

        private static void EnsureLocalPlayerEntity(GameEngine engine)
        {
            if (engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out var localObj) &&
                localObj is Entity local &&
                engine.World.IsAlive(local))
            {
                EnsureSelectionComponents(engine.World, local);
                return;
            }

            Entity owner = Entity.Null;
            engine.World.Query(in LocalPlayerQuery, (Entity entity, ref PlayerOwner playerOwner) =>
            {
                if (owner == Entity.Null && playerOwner.PlayerId == 1)
                {
                    owner = entity;
                }
            });

            if (owner == Entity.Null)
            {
                owner = engine.World.Create(
                    new PlayerOwner { PlayerId = 1 },
                    default(SelectionBuffer),
                    default(SelectionGroupBuffer),
                    default(SelectionDragState));
            }
            else
            {
                EnsureSelectionComponents(engine.World, owner);
            }

            engine.GlobalContext[CoreServiceKeys.LocalPlayerEntity.Name] = owner;
        }

        private static void EnsureSelectionComponents(World world, Entity owner)
        {
            if (!world.Has<SelectionBuffer>(owner))
            {
                world.Add(owner, default(SelectionBuffer));
            }

            if (!world.Has<SelectionGroupBuffer>(owner))
            {
                world.Add(owner, default(SelectionGroupBuffer));
            }

            if (!world.Has<SelectionDragState>(owner))
            {
                world.Add(owner, default(SelectionDragState));
            }
        }
    }
}
