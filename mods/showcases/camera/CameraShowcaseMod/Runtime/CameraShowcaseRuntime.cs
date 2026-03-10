using System;
using System.Threading.Tasks;
using CameraShowcaseMod.Input;
using CameraShowcaseMod.UI;
using CoreInputMod.ViewMode;
using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;
using Ludots.Core.Modding;
using Ludots.UI;

namespace CameraShowcaseMod.Runtime
{
    internal sealed class CameraShowcaseRuntime
    {
        private readonly IModContext _context;
        private readonly CameraShowcasePanelController _panelController;
        private bool _inputContextActive;

        public CameraShowcaseRuntime(IModContext context)
        {
            _context = context;
            _panelController = new CameraShowcasePanelController();
        }

        public Task HandleMapFocusedAsync(ScriptContext context)
        {
            var engine = context.GetEngine();
            if (engine == null)
            {
                return Task.CompletedTask;
            }

            string? activeMapId = engine.CurrentMapSession?.MapId.Value;
            bool showcaseActive = CameraShowcaseIds.IsShowcaseMap(activeMapId);
            var viewModeManager = ResolveViewModeManager(engine);

            var input = context.Get(CoreServiceKeys.InputHandler);
            if (showcaseActive)
            {
                ActivateInputContext(input);
                MountPanel(context, engine, activeMapId!, viewModeManager);
            }
            else
            {
                ClearSelectionModeIfOwned(viewModeManager);
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
                !CameraShowcaseIds.IsShowcaseMap(mapId.Value))
            {
                return Task.CompletedTask;
            }

            ClearSelectionModeIfOwned(ResolveViewModeManager(engine));
            DeactivateInputContext(context.Get(CoreServiceKeys.InputHandler));
            ClearPanelIfOwned(context);
            return Task.CompletedTask;
        }

        private void ActivateInputContext(PlayerInputHandler? input)
        {
            if (input == null || _inputContextActive)
            {
                return;
            }

            EnsureShowcaseInputSchema(input);
            input.PushContext(CameraShowcaseInputContexts.Showcase);
            _inputContextActive = true;
        }

        private void DeactivateInputContext(PlayerInputHandler? input)
        {
            if (input == null || !_inputContextActive)
            {
                return;
            }

            input.PopContext(CameraShowcaseInputContexts.Showcase);
            _inputContextActive = false;
        }

        private void MountPanel(ScriptContext context, GameEngine engine, string activeMapId, ViewModeManager? viewModeManager)
        {
            if (context.Get(CoreServiceKeys.UIRoot) is not UIRoot root)
            {
                return;
            }

            root.MountScene(_panelController.BuildScene(engine, activeMapId, viewModeManager));
            root.IsDirty = true;
        }

        private void ClearPanelIfOwned(ScriptContext context)
        {
            if (context.Get(CoreServiceKeys.UIRoot) is not UIRoot root)
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

        private static void ClearSelectionModeIfOwned(ViewModeManager? viewModeManager)
        {
            if (viewModeManager != null &&
                string.Equals(viewModeManager.ActiveMode?.Id, CameraShowcaseIds.SelectionModeId, StringComparison.OrdinalIgnoreCase))
            {
                viewModeManager.ClearActiveMode();
            }
        }

        private static void EnsureShowcaseInputSchema(PlayerInputHandler input)
        {
            if (!input.HasContext(CameraShowcaseInputContexts.Showcase))
            {
                throw new InvalidOperationException($"Missing input context: {CameraShowcaseInputContexts.Showcase}");
            }

            if (!input.HasAction(CameraShowcaseIds.SelectionModeActionId))
            {
                throw new InvalidOperationException($"Missing input action: {CameraShowcaseIds.SelectionModeActionId}");
            }
        }
    }
}
