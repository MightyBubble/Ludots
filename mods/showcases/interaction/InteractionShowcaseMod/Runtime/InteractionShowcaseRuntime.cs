using System;
using System.Threading.Tasks;
using EntityInfoPanelsMod;
using EntityInfoPanelsMod.Commands;
using CoreInputMod.ViewMode;
using InteractionShowcaseMod.Input;
using InteractionShowcaseMod.UI;
using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;
using Ludots.UI;

namespace InteractionShowcaseMod.Runtime
{
    internal sealed class InteractionShowcaseRuntime
    {
        private readonly InteractionShowcasePanelController _panelController;
        private bool _inputContextActive;

        public InteractionShowcaseRuntime()
        {
            _panelController = new InteractionShowcasePanelController();
        }

        public Task HandleMapFocusedAsync(ScriptContext context)
        {
            var engine = context.GetEngine();
            if (engine == null)
            {
                return Task.CompletedTask;
            }

            string? activeMapId = engine.CurrentMapSession?.MapId.Value;
            bool showcaseActive = InteractionShowcaseIds.IsShowcaseMap(activeMapId);
            var viewModeManager = ResolveViewModeManager(engine);
            var input = context.Get(CoreServiceKeys.InputHandler);

            if (showcaseActive)
            {
                ActivateInputContext(input);
                EnsureDefaultShowcaseMode(viewModeManager);
                EnsureEntityInfoPanels(context, engine);
                RefreshPanel(engine);
            }
            else
            {
                CloseEntityInfoPanels(context);
                ClearShowcaseModeIfOwned(viewModeManager);
                DeactivateInputContext(input);
                ClearPanelIfOwned(context);
            }

            return Task.CompletedTask;
        }

        public Task HandleMapUnloadedAsync(ScriptContext context)
        {
            if (context.GetEngine() is not GameEngine engine)
            {
                return Task.CompletedTask;
            }

            var mapId = context.Get(CoreServiceKeys.MapId);
            if (string.IsNullOrWhiteSpace(mapId.Value) ||
                !InteractionShowcaseIds.IsShowcaseMap(mapId.Value))
            {
                return Task.CompletedTask;
            }

            ClearShowcaseModeIfOwned(ResolveViewModeManager(engine));
            CloseEntityInfoPanels(context);
            DeactivateInputContext(context.Get(CoreServiceKeys.InputHandler));
            ClearPanelIfOwned(context);
            return Task.CompletedTask;
        }

        public void RefreshPanel(GameEngine engine)
        {
            string? activeMapId = engine.CurrentMapSession?.MapId.Value;
            if (!InteractionShowcaseIds.IsShowcaseMap(activeMapId))
            {
                ClearPanelIfOwned(engine);
                return;
            }

            if (engine.GlobalContext.TryGetValue(InteractionShowcaseIds.SuppressUiPanelKey, out var suppressObj) &&
                suppressObj is bool suppress &&
                suppress)
            {
                ClearPanelIfOwned(engine);
                return;
            }

            if (engine.GetService(CoreServiceKeys.UIRoot) is not UIRoot root)
            {
                return;
            }

            _panelController.MountOrRefresh(root, engine, activeMapId!, ResolveViewModeManager(engine));
        }

        private void ActivateInputContext(PlayerInputHandler? input)
        {
            if (input == null || _inputContextActive)
            {
                return;
            }

            EnsureShowcaseInputSchema(input);
            input.PushContext(InteractionShowcaseInputContexts.Showcase);
            _inputContextActive = true;
        }

        private void DeactivateInputContext(PlayerInputHandler? input)
        {
            if (input == null || !_inputContextActive)
            {
                return;
            }

            input.PopContext(InteractionShowcaseInputContexts.Showcase);
            _inputContextActive = false;
        }

        private void ClearPanelIfOwned(ScriptContext context)
        {
            if (context.Get(CoreServiceKeys.UIRoot) is not UIRoot root)
            {
                return;
            }

            _panelController.ClearIfOwned(root);
        }

        private void ClearPanelIfOwned(GameEngine engine)
        {
            if (engine.GetService(CoreServiceKeys.UIRoot) is not UIRoot root)
            {
                return;
            }

            _panelController.ClearIfOwned(root);
        }

        private static void EnsureEntityInfoPanels(ScriptContext context, GameEngine engine)
        {
            if (engine.GetService(EntityInfoPanelServiceKeys.HandleStore) is not EntityInfoPanelHandleStore handles)
            {
                return;
            }

            EntityInfoPanelTarget? selectedTarget = TryResolveSelectedTarget(engine);
            if (!selectedTarget.HasValue)
            {
                CloseIfPresent(context, handles, InteractionShowcaseIds.SelectedComponentUiHandleKey);
                CloseIfPresent(context, handles, InteractionShowcaseIds.SelectedGasUiHandleKey);
                CloseIfPresent(context, handles, InteractionShowcaseIds.SelectedGasOverlayHandleKey);
                return;
            }

            OpenOrUpdate(
                context,
                handles,
                InteractionShowcaseIds.SelectedComponentUiHandleKey,
                new EntityInfoPanelRequest(
                    EntityInfoPanelKind.ComponentInspector,
                    EntityInfoPanelSurface.Ui,
                    selectedTarget.Value,
                    new EntityInfoPanelLayout(EntityInfoPanelAnchor.TopRight, 16f, 16f, 408f, 320f),
                    EntityInfoGasDetailFlags.None,
                    true));

            OpenOrUpdate(
                context,
                handles,
                InteractionShowcaseIds.SelectedGasUiHandleKey,
                new EntityInfoPanelRequest(
                    EntityInfoPanelKind.GasInspector,
                    EntityInfoPanelSurface.Ui,
                    selectedTarget.Value,
                    new EntityInfoPanelLayout(EntityInfoPanelAnchor.BottomRight, 16f, 16f, 408f, 264f),
                    EntityInfoGasDetailFlags.ShowAttributeAggregateSources | EntityInfoGasDetailFlags.ShowModifierState,
                    true));

            OpenOrUpdate(
                context,
                handles,
                InteractionShowcaseIds.SelectedGasOverlayHandleKey,
                new EntityInfoPanelRequest(
                    EntityInfoPanelKind.GasInspector,
                    EntityInfoPanelSurface.Overlay,
                    selectedTarget.Value,
                    new EntityInfoPanelLayout(EntityInfoPanelAnchor.TopCenter, 0f, 16f, 300f, 192f),
                    EntityInfoGasDetailFlags.ShowModifierState,
                    true));
        }

        private static EntityInfoPanelTarget? TryResolveSelectedTarget(GameEngine engine)
        {
            if (!engine.GlobalContext.TryGetValue(CoreServiceKeys.SelectedEntity.Name, out object? selectedObj) ||
                selectedObj is not Arch.Core.Entity selected ||
                selected == Arch.Core.Entity.Null ||
                !engine.World.IsAlive(selected))
            {
                return null;
            }

            return EntityInfoPanelTarget.Fixed(selected);
        }

        private static void OpenOrUpdate(
            ScriptContext context,
            EntityInfoPanelHandleStore handles,
            string handleKey,
            EntityInfoPanelRequest request)
        {
            if (handles.TryGet(handleKey, out _))
            {
                new UpdateEntityInfoPanelCommand
                {
                    HandleSlotKey = handleKey,
                    Visible = true,
                    Layout = request.Layout,
                    Target = request.Target,
                    GasDetailFlags = request.GasDetailFlags
                }.ExecuteAsync(context).GetAwaiter().GetResult();
                return;
            }

            new OpenEntityInfoPanelCommand
            {
                HandleSlotKey = handleKey,
                Request = request
            }.ExecuteAsync(context).GetAwaiter().GetResult();
        }

        private static void CloseEntityInfoPanels(ScriptContext context)
        {
            if (context.Get(EntityInfoPanelServiceKeys.HandleStore) is not EntityInfoPanelHandleStore handles)
            {
                return;
            }

            CloseIfPresent(context, handles, InteractionShowcaseIds.SelectedComponentUiHandleKey);
            CloseIfPresent(context, handles, InteractionShowcaseIds.SelectedGasUiHandleKey);
            CloseIfPresent(context, handles, InteractionShowcaseIds.SelectedGasOverlayHandleKey);
            CloseIfPresent(context, handles, InteractionShowcaseIds.ArcweaverOverlayHandleKey);
            CloseIfPresent(context, handles, InteractionShowcaseIds.VanguardOverlayHandleKey);
        }

        private static void CloseIfPresent(ScriptContext context, EntityInfoPanelHandleStore handles, string handleKey)
        {
            if (!handles.TryGet(handleKey, out _))
            {
                return;
            }

            new CloseEntityInfoPanelCommand
            {
                HandleSlotKey = handleKey
            }.ExecuteAsync(context).GetAwaiter().GetResult();
        }

        private static ViewModeManager? ResolveViewModeManager(GameEngine engine)
        {
            if (engine.GlobalContext.TryGetValue(ViewModeManager.GlobalKey, out var managerObj) &&
                managerObj is ViewModeManager manager)
            {
                return manager;
            }

            return null;
        }

        private static void EnsureDefaultShowcaseMode(ViewModeManager? viewModeManager)
        {
            if (viewModeManager == null)
            {
                return;
            }

            string? activeModeId = viewModeManager.ActiveMode?.Id;
            if (!InteractionShowcaseIds.IsShowcaseMode(activeModeId))
            {
                viewModeManager.SwitchTo(InteractionShowcaseIds.LolModeId);
            }
        }

        private static void ClearShowcaseModeIfOwned(ViewModeManager? viewModeManager)
        {
            if (viewModeManager != null &&
                InteractionShowcaseIds.IsShowcaseMode(viewModeManager.ActiveMode?.Id))
            {
                viewModeManager.ClearActiveMode();
            }
        }

        private static void EnsureShowcaseInputSchema(PlayerInputHandler input)
        {
            if (!input.HasContext(InteractionShowcaseInputContexts.Showcase))
            {
                throw new InvalidOperationException($"Missing input context: {InteractionShowcaseInputContexts.Showcase}");
            }

            string[] requiredActions =
            {
                "SkillQ",
                "SkillW",
                "SkillE",
                "SkillR",
                "SkillZ",
                "SkillF",
                "ActionAttack",
                "RuneBurst",
                InteractionShowcaseIds.WowModeActionId,
                InteractionShowcaseIds.LolModeActionId,
                InteractionShowcaseIds.Sc2ModeActionId,
                InteractionShowcaseIds.IndicatorModeActionId,
                InteractionShowcaseIds.ActionModeActionId
            };

            for (int i = 0; i < requiredActions.Length; i++)
            {
                if (!input.HasAction(requiredActions[i]))
                {
                    throw new InvalidOperationException($"Missing input action: {requiredActions[i]}");
                }
            }
        }
    }
}
