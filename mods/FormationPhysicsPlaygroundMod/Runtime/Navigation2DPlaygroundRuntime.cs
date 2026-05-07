using System;
using System.Numerics;
using System.Threading.Tasks;
using Arch.Core;
using CoreInputMod;
using CoreInputMod.ViewMode;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.Selection;
using Ludots.Core.Modding;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Navigation2D.Runtime;
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
        private static readonly QueryDescription ScenarioCameraQuery = new QueryDescription()
            .WithAll<NavPlaygroundTeam, WorldPositionCm>();
        private static readonly QueryDescription ControllableCameraQuery = new QueryDescription()
            .WithAll<NavPlaygroundControllable, NavPlaygroundTeam, WorldPositionCm>()
            .WithNone<NavPlaygroundBlocker>();

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
                navRuntime.FlowEnabled = false;
                navRuntime.FlowDebugEnabled = false;
                navRuntime.FlowIterationsPerTick = 0;
            }
            else
            {
                throw new InvalidOperationException("FormationPhysicsPlaygroundMod requires Navigation2DRuntime to be available.");
            }

            var debugDrawBuffer = engine.GetService(CoreServiceKeys.DebugDrawCommandBuffer) ?? new DebugDrawCommandBuffer();
            engine.SetService(CoreServiceKeys.DebugDrawCommandBuffer, debugDrawBuffer);

            engine.RegisterSystem(new Navigation2DPlaygroundControlSystem(engine), SystemGroup.InputCollection);
            engine.RegisterSystem(new Navigation2DPlaygroundSelectionFilterSystem(engine), SystemGroup.InputCollection);
            engine.RegisterSystem(new Navigation2DPlaygroundCommandSystem(engine), SystemGroup.InputCollection);

            engine.RegisterPresentationSystem(new Navigation2DPlaygroundPresentationSystem(engine, debugDrawBuffer));
            engine.RegisterPresentationSystem(new Navigation2DPlaygroundSelectionOverlaySystem(engine));
            engine.RegisterPresentationSystem(new Navigation2DPlaygroundPanelPresentationSystem(engine, this));

            _systemsInstalled = true;
            _context.Log("[FormationPhysicsPlaygroundMod] Installed playable runtime systems.");
        }

        public Task HandleMapFocusedAsync(ScriptContext context)
        {
            var engine = context.GetEngine();
            if (engine == null)
            {
                _context.Log("[FormationPhysicsPlaygroundMod] HandleMapFocusedAsync skipped because engine was null.");
                return Task.CompletedTask;
            }

            string? mapId = context.Get(CoreServiceKeys.MapId).Value;
            _context.Log($"[FormationPhysicsPlaygroundMod] HandleMapFocusedAsync mapId='{mapId ?? "<null>"}'.");
            if (!Navigation2DPlaygroundIds.IsPlaygroundMap(mapId))
            {
                Navigation2DPlaygroundState.Enabled = false;
                TryPopInputContext(engine);
                ClearOwnedViewMode(engine);
                ClearPanelIfOwned(engine);
                _context.Log("[FormationPhysicsPlaygroundMod] MapFocused ignored because current map is not the playground map.");
                return Task.CompletedTask;
            }

            EnsureSystemsInstalled(engine);
            EnsureInitialState(engine);
            EnsureLocalPlayerEntity(engine);
            Navigation2DPlaygroundState.Enabled = true;
            EnsurePlaygroundInputContext(engine);
            EnsureOwnedViewMode(engine);
            Navigation2DPlaygroundControlSystem.EnsureScenarioLoaded(engine);
            ResetCamera(engine);
            RefreshPanel(engine);
            _context.Log($"[FormationPhysicsPlaygroundMod] Playground map focused. Enabled={Navigation2DPlaygroundState.Enabled}, ScenarioIndex={Navigation2DPlaygroundState.CurrentScenarioIndex}, AgentsPerTeam={Navigation2DPlaygroundState.AgentsPerTeam}.");
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

        public static bool ResetCamera(GameEngine engine)
        {
            ArgumentNullException.ThrowIfNull(engine);

            if (!Navigation2DPlaygroundIds.IsPlaygroundMap(engine.CurrentMapSession?.MapId.Value) ||
                !TryResolveScenarioCameraTarget(engine.World, out Vector2 targetCm))
            {
                return false;
            }

            if (CoreInputRuntimeServices.TryGetViewModeManager(engine, out var manager))
            {
                manager.SwitchTo(Navigation2DPlaygroundIds.CommandModeId);
            }

            engine.SetService(CoreServiceKeys.VirtualCameraRequest, new VirtualCameraRequest
            {
                Id = Navigation2DPlaygroundIds.CommandCameraId,
                BlendDurationSeconds = 0f,
                SnapToFollowTargetWhenAvailable = true,
                ResetRuntimeState = true
            });

            engine.SetService(CoreServiceKeys.CameraPoseRequest, new CameraPoseRequest
            {
                VirtualCameraId = Navigation2DPlaygroundIds.CommandCameraId,
                TargetCm = targetCm
            });

            return true;
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
            if (!CoreInputRuntimeServices.TryGetViewModeManager(engine, out var manager))
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
            if (!CoreInputRuntimeServices.TryGetViewModeManager(engine, out var manager))
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
            SelectionRuntime selection = engine.GetService(CoreServiceKeys.SelectionRuntime)
                ?? throw new InvalidOperationException("Navigation2DPlaygroundMod requires SelectionRuntime.");

            if (engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out var localObj) &&
                localObj is Entity local &&
                engine.World.IsAlive(local))
            {
                EnsureSelectionComponents(engine.World, local, selection, engine.GlobalContext);
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
                    default(SelectionDragState));
            }
            else
            {
                EnsureSelectionComponents(engine.World, owner, selection, engine.GlobalContext);
            }

            engine.GlobalContext[CoreServiceKeys.LocalPlayerEntity.Name] = owner;
            EnsureSelectionComponents(engine.World, owner, selection, engine.GlobalContext);
        }

        private static void EnsureSelectionComponents(World world, Entity owner, SelectionRuntime selection, System.Collections.Generic.Dictionary<string, object> globals)
        {
            if (!world.Has<SelectionDragState>(owner))
            {
                world.Add(owner, default(SelectionDragState));
            }

            selection.TryGetOrCreateSelectionEntity(owner, SelectionSetKeys.LivePrimary, out _);
            selection.TryBindView(owner, SelectionViewKeys.Primary, owner, SelectionSetKeys.LivePrimary);

            globals[CoreServiceKeys.SelectionViewViewerEntity.Name] = owner;
            globals[CoreServiceKeys.SelectionViewKey.Name] = SelectionViewKeys.Primary;
        }

        private static bool TryResolveScenarioCameraTarget(World world, out Vector2 targetCm)
        {
            if (!TryResolveBoundsCenter(world, ScenarioCameraQuery, out Vector2 scenarioCenterCm))
            {
                targetCm = default;
                return false;
            }

            if (!TryResolveBoundsCenter(world, ControllableCameraQuery, out Vector2 controllableCenterCm))
            {
                targetCm = scenarioCenterCm;
                return true;
            }

            targetCm = Vector2.Lerp(scenarioCenterCm, controllableCenterCm, 0.65f);
            return true;
        }

        private static bool TryResolveBoundsCenter(World world, QueryDescription query, out Vector2 centerCm)
        {
            bool hasAny = false;
            Vector2 min = default;
            Vector2 max = default;

            world.Query(in query, (Entity entity, ref NavPlaygroundTeam team, ref WorldPositionCm position) =>
            {
                Vector2 worldCm = position.Value.ToVector2();
                if (!hasAny)
                {
                    min = worldCm;
                    max = worldCm;
                    hasAny = true;
                    return;
                }

                min = Vector2.Min(min, worldCm);
                max = Vector2.Max(max, worldCm);
            });

            centerCm = hasAny
                ? (min + max) * 0.5f
                : default;
            return hasAny;
        }
    }
}
