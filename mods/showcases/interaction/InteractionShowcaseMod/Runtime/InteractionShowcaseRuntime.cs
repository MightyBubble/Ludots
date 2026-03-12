using System;
using System.Threading.Tasks;
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
                RefreshPanel(engine);
            }
            else
            {
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
