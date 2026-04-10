using System;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.Selection;
using Ludots.Core.Map;
using Ludots.Core.Navigation2D.Runtime;
using Ludots.Core.Scripting;
using Ludots.UI;
using MassFlowNavPlaygroundMod.Components;
using MassFlowNavPlaygroundMod.UI;

namespace MassFlowNavPlaygroundMod.Runtime
{
    internal sealed class MassFlowNavPlaygroundRuntime
    {
        private readonly MassFlowNavPlaygroundPanelController _panelController;
        private Entity[] _selectedScratch = Array.Empty<Entity>();
        private bool _inputContextActive;

        public MassFlowNavPlaygroundRuntime()
        {
            _panelController = new MassFlowNavPlaygroundPanelController(this);
        }

        public Task HandleMapFocusedAsync(ScriptContext context)
        {
            if (context.GetEngine() is not GameEngine engine ||
                engine.GetService(MassFlowNavPlaygroundServiceKeys.State) is not MassFlowNavPlaygroundState state)
            {
                return Task.CompletedTask;
            }

            string? activeMapId = engine.CurrentMapSession?.MapId.Value;
            bool isPlaygroundMap = string.Equals(activeMapId, MassFlowNavPlaygroundIds.MapId, StringComparison.OrdinalIgnoreCase);
            if (isPlaygroundMap)
            {
                state.Activate(activeMapId!);
                if (engine.GetService(CoreServiceKeys.Navigation2DRuntime) is Navigation2DRuntime navRuntime)
                {
                    navRuntime.FlowEnabled = true;
                }

                ActivateInputContext(context.Get(CoreServiceKeys.InputHandler));
                EnsureScene(engine, state);
                EnsureSelectionView(engine, state);
                RefreshPanel(engine, 0f);
            }
            else
            {
                state.Deactivate();
                DeactivateInputContext(context.Get(CoreServiceKeys.InputHandler));
                ClearPanelIfOwned(context);
            }

            return Task.CompletedTask;
        }

        public Task HandleMapUnloadedAsync(ScriptContext context)
        {
            if (context.GetEngine() is not GameEngine engine ||
                engine.GetService(MassFlowNavPlaygroundServiceKeys.State) is not MassFlowNavPlaygroundState state)
            {
                return Task.CompletedTask;
            }

            MapId mapId = context.Get(CoreServiceKeys.MapId);
            if (!string.Equals(mapId.Value, MassFlowNavPlaygroundIds.MapId, StringComparison.OrdinalIgnoreCase))
            {
                return Task.CompletedTask;
            }

            state.Deactivate();
            DeactivateInputContext(context.Get(CoreServiceKeys.InputHandler));
            ClearPanelIfOwned(context);
            return Task.CompletedTask;
        }

        public void RefreshPanel(GameEngine engine, float dt)
        {
            if (engine.GetService(MassFlowNavPlaygroundServiceKeys.State) is not MassFlowNavPlaygroundState state ||
                !state.IsActive ||
                !string.Equals(engine.CurrentMapSession?.MapId.Value, MassFlowNavPlaygroundIds.MapId, StringComparison.OrdinalIgnoreCase))
            {
                ClearPanelIfOwned(engine);
                return;
            }

            if (engine.GetService(CoreServiceKeys.UIRoot) is not UIRoot root)
            {
                return;
            }

            _panelController.MountOrRefresh(root, engine, state, dt);
        }

        public void Respawn(GameEngine engine)
        {
            if (engine.GetService(MassFlowNavPlaygroundServiceKeys.State) is not MassFlowNavPlaygroundState state ||
                !state.IsActive)
            {
                return;
            }

            MassFlowNavScenarioSpawner.Respawn(engine, state);
            EnsureSelectionView(engine, state);
        }

        public void SetDesiredUnitCount(GameEngine engine, int unitCount)
        {
            if (engine.GetService(MassFlowNavPlaygroundServiceKeys.State) is not MassFlowNavPlaygroundState state)
            {
                return;
            }

            state.DesiredUnitCount = Math.Clamp(unitCount, 1000, 20000);
            state.MarkPanelDirty();
            Respawn(engine);
        }

        public void SetSelectedTeamFlow(GameEngine engine, int flowId)
        {
            if (engine.GetService(MassFlowNavPlaygroundServiceKeys.State) is MassFlowNavPlaygroundState state)
            {
                state.SelectedTeamFlowId = flowId <= 0 ? 0 : 1;
                state.MarkPanelDirty();
            }
        }

        public void SetFormationMode(GameEngine engine, MassFlowFormationMode mode)
        {
            if (engine.GetService(MassFlowNavPlaygroundServiceKeys.State) is MassFlowNavPlaygroundState state)
            {
                state.FormationMode = mode;
                state.MarkPanelDirty();
            }
        }

        public void ClearSelection(GameEngine engine)
        {
            if (!TryResolveSelectionContext(engine, out SelectionRuntime selection, out Entity owner))
            {
                return;
            }

            selection.ClearSelection(owner, SelectionSetKeys.Ambient);
            if (engine.GetService(MassFlowNavPlaygroundServiceKeys.State) is MassFlowNavPlaygroundState state)
            {
                state.MarkPanelDirty();
            }
        }

        public float GetSelectedFormationRotationDeg(GameEngine engine)
        {
            if (engine.GetService(MassFlowNavPlaygroundServiceKeys.State) is not MassFlowNavPlaygroundState state ||
                !TryResolveSelectionContext(engine, out SelectionRuntime selection, out Entity owner))
            {
                return 0f;
            }

            int count = selection.GetSelectionCount(owner, SelectionSetKeys.Ambient);
            if (count <= 0)
            {
                return 0f;
            }

            EnsureSelectedCapacity(count);
            int copied = selection.CopySelection(owner, SelectionSetKeys.Ambient, _selectedScratch);
            for (int i = 0; i < copied; i++)
            {
                Entity entity = _selectedScratch[i];
                if (!engine.World.IsAlive(entity) || !engine.World.TryGet(entity, out MassFlowNavFormationMember formationMember))
                {
                    continue;
                }

                if (state.TryGetGroup(formationMember.GroupId, out MassFlowFormationGroup group))
                {
                    return NormalizeDegrees(group.RotationRad * (180f / MathF.PI));
                }
            }

            return 0f;
        }

        private void EnsureScene(GameEngine engine, MassFlowNavPlaygroundState state)
        {
            if (engine.World.IsAlive(state.SceneRootEntity))
            {
                return;
            }

            MassFlowNavScenarioSpawner.Respawn(engine, state);
        }

        private void EnsureSelectionView(GameEngine engine, MassFlowNavPlaygroundState state)
        {
            if (engine.GetService(CoreServiceKeys.SelectionRuntime) is not SelectionRuntime selection ||
                !engine.World.IsAlive(state.ControllerEntity))
            {
                return;
            }

            selection.TryGetOrCreateSelectionEntity(state.ControllerEntity, SelectionSetKeys.Ambient, out _);
            selection.TryBindView(state.ControllerEntity, SelectionViewKeys.Primary, state.ControllerEntity, SelectionSetKeys.Ambient);
            engine.GlobalContext[CoreServiceKeys.SelectionViewViewerEntity.Name] = state.ControllerEntity;
            engine.GlobalContext[CoreServiceKeys.SelectionViewKey.Name] = SelectionViewKeys.Primary;
            engine.SetService(CoreServiceKeys.LocalPlayerEntity, state.ControllerEntity);
        }

        private void ActivateInputContext(PlayerInputHandler? input)
        {
            if (input == null || _inputContextActive)
            {
                return;
            }

            EnsureInputSchema(input);
            input.PushContext(MassFlowNavPlaygroundIds.InputContextId);
            _inputContextActive = true;
        }

        private void DeactivateInputContext(PlayerInputHandler? input)
        {
            if (input == null || !_inputContextActive)
            {
                return;
            }

            input.PopContext(MassFlowNavPlaygroundIds.InputContextId);
            _inputContextActive = false;
        }

        private void EnsureSelectedCapacity(int required)
        {
            if (required <= _selectedScratch.Length)
            {
                return;
            }

            int nextSize = _selectedScratch.Length == 0 ? 16 : _selectedScratch.Length;
            while (nextSize < required)
            {
                nextSize *= 2;
            }

            Array.Resize(ref _selectedScratch, nextSize);
        }

        private void ClearPanelIfOwned(ScriptContext context)
        {
            if (context.Get(CoreServiceKeys.UIRoot) is UIRoot root)
            {
                _panelController.ClearIfOwned(root);
            }
        }

        private void ClearPanelIfOwned(GameEngine engine)
        {
            if (engine.GetService(CoreServiceKeys.UIRoot) is UIRoot root)
            {
                _panelController.ClearIfOwned(root);
            }
        }

        private static bool TryResolveSelectionContext(GameEngine engine, out SelectionRuntime selection, out Entity owner)
        {
            selection = default!;
            owner = Entity.Null;
            if (engine.GetService(CoreServiceKeys.SelectionRuntime) is not SelectionRuntime runtime)
            {
                return false;
            }

            if (!engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out object? ownerObj) ||
                ownerObj is not Entity entity ||
                !engine.World.IsAlive(entity))
            {
                return false;
            }

            selection = runtime;
            owner = entity;
            return true;
        }

        private static void EnsureInputSchema(PlayerInputHandler input)
        {
            if (!input.HasContext(MassFlowNavPlaygroundIds.InputContextId))
            {
                throw new InvalidOperationException($"Missing input context: {MassFlowNavPlaygroundIds.InputContextId}");
            }

            if (!input.HasAction(MassFlowNavPlaygroundIds.RotateFormationLeftActionId) ||
                !input.HasAction(MassFlowNavPlaygroundIds.RotateFormationRightActionId))
            {
                throw new InvalidOperationException("MassFlowNavPlaygroundMod input actions were not loaded.");
            }
        }

        private static float NormalizeDegrees(float degrees)
        {
            float normalized = degrees % 360f;
            return normalized < 0f ? normalized + 360f : normalized;
        }
    }
}
