using System;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.Selection;
using Ludots.Core.Mathematics;
using Ludots.Core.Navigation2D.Config;
using Ludots.Core.Presentation.Utils;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using Navigation2DPlaygroundMod.Input;
using Navigation2DPlaygroundMod.Runtime;

namespace Navigation2DPlaygroundMod.Systems
{
    internal sealed class Navigation2DPlaygroundCommandSystem : ISystem<float>
    {
        private static readonly InteractionActionBindings DefaultBindings = new();

        private readonly GameEngine _engine;
        private readonly World _world;

        public Navigation2DPlaygroundCommandSystem(GameEngine engine)
        {
            _engine = engine;
            _world = engine.World;
        }

        public void Initialize() { }
        public void BeforeUpdate(in float dt) { }
        public void AfterUpdate(in float dt) { }
        public void Dispose() { }

        public void Update(in float dt)
        {
            if (!Navigation2DPlaygroundState.Enabled)
            {
                return;
            }

            if (_engine.GetService(CoreServiceKeys.AuthoritativeInput) is not IInputActionReader input)
            {
                return;
            }

            HandlePressed(input, Navigation2DPlaygroundInputActions.ToolMove, () => Navigation2DPlaygroundState.ToolMode = Navigation2DPlaygroundToolMode.Move);
            HandlePressed(input, Navigation2DPlaygroundInputActions.ToolSpawnTeam0, () => Navigation2DPlaygroundState.ToolMode = Navigation2DPlaygroundToolMode.SpawnTeam0);
            HandlePressed(input, Navigation2DPlaygroundInputActions.ToolSpawnTeam1, () => Navigation2DPlaygroundState.ToolMode = Navigation2DPlaygroundToolMode.SpawnTeam1);
            HandlePressed(input, Navigation2DPlaygroundInputActions.ToolSpawnBlocker, () => Navigation2DPlaygroundState.ToolMode = Navigation2DPlaygroundToolMode.SpawnBlocker);

            var playgroundConfig = Navigation2DPlaygroundControlSystem.GetPlaygroundConfig(_engine);
            HandlePressed(input, Navigation2DPlaygroundInputActions.IncreaseSpawnBatch, () => Navigation2DPlaygroundControlSystem.AdjustSpawnBatch(_engine, playgroundConfig.SpawnBatchStep));
            HandlePressed(input, Navigation2DPlaygroundInputActions.DecreaseSpawnBatch, () => Navigation2DPlaygroundControlSystem.AdjustSpawnBatch(_engine, -playgroundConfig.SpawnBatchStep));

            bool uiCaptured = _engine.GetService(CoreServiceKeys.UiCaptured);
            var bindings = ResolveBindings();
            if (!uiCaptured && input.PressedThisFrame(bindings.CommandActionId) && TryResolveGroundPointer(input, bindings, out var worldCm))
            {
                ExecuteCommand(playgroundConfig, worldCm);
            }
        }

        private void ExecuteCommand(Navigation2DPlaygroundConfig playgroundConfig, in WorldCmInt2 worldCm)
        {
            Vector2 pointCm = new(worldCm.X, worldCm.Y);
            switch (Navigation2DPlaygroundState.ToolMode)
            {
                case Navigation2DPlaygroundToolMode.Move:
                    IssueMoveCommand(playgroundConfig, pointCm);
                    break;
                case Navigation2DPlaygroundToolMode.SpawnTeam0:
                    Navigation2DPlaygroundScenarioSpawner.SpawnDynamicBatch(
                        _world,
                        teamId: 0,
                        centerCm: pointCm,
                        count: Navigation2DPlaygroundState.SpawnBatch,
                        spacingCm: playgroundConfig.DynamicSpawnSpacingCm,
                        goalRadiusCm: playgroundConfig.CommandGoalRadiusCm);
                    Navigation2DPlaygroundControlSystem.UpdateLiveCounts(_engine);
                    break;
                case Navigation2DPlaygroundToolMode.SpawnTeam1:
                    Navigation2DPlaygroundScenarioSpawner.SpawnDynamicBatch(
                        _world,
                        teamId: 1,
                        centerCm: pointCm,
                        count: Navigation2DPlaygroundState.SpawnBatch,
                        spacingCm: playgroundConfig.DynamicSpawnSpacingCm,
                        goalRadiusCm: playgroundConfig.CommandGoalRadiusCm);
                    Navigation2DPlaygroundControlSystem.UpdateLiveCounts(_engine);
                    break;
                case Navigation2DPlaygroundToolMode.SpawnBlocker:
                    Navigation2DPlaygroundScenarioSpawner.SpawnBlockerBatch(
                        _world,
                        centerCm: pointCm,
                        count: Navigation2DPlaygroundState.SpawnBatch,
                        spacingCm: playgroundConfig.DynamicSpawnSpacingCm,
                        radiusCm: playgroundConfig.DynamicBlockerRadiusCm);
                    Navigation2DPlaygroundControlSystem.UpdateLiveCounts(_engine);
                    break;
            }
        }

        private void IssueMoveCommand(Navigation2DPlaygroundConfig playgroundConfig, Vector2 pointCm)
        {
            Span<Entity> selected = stackalloc Entity[SelectionBuffer.CAPACITY];
            int count = Navigation2DPlaygroundSelectionView.CopySelectedEntities(_world, _engine.GlobalContext, selected);
            if (count <= 0)
            {
                return;
            }

            Navigation2DPlaygroundScenarioSpawner.ApplyMoveFormation(
                _world,
                selected.Slice(0, count),
                pointCm,
                playgroundConfig.CommandFormationSpacingCm,
                playgroundConfig.CommandGoalRadiusCm);
        }

        private bool TryResolveGroundPointer(IInputActionReader input, InteractionActionBindings bindings, out WorldCmInt2 worldCm)
        {
            worldCm = default;
            if (!_engine.GlobalContext.TryGetValue(CoreServiceKeys.ScreenRayProvider.Name, out var rayObj) ||
                rayObj is not IScreenRayProvider rayProvider)
            {
                return false;
            }

            Vector2 pointer = input.ReadAction<Vector2>(bindings.PointerPositionActionId);
            var ray = rayProvider.GetRay(pointer);
            return GroundRaycastUtil.TryGetGroundWorldCmBounded(in ray, _engine.WorldSizeSpec, out worldCm);
        }

        private InteractionActionBindings ResolveBindings()
        {
            if (_engine.GlobalContext.TryGetValue(CoreServiceKeys.InteractionActionBindings.Name, out var obj) &&
                obj is InteractionActionBindings bindings)
            {
                return bindings;
            }

            return DefaultBindings;
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
