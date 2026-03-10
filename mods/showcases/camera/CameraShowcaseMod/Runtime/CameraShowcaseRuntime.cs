using System;
using System.Threading.Tasks;
using CameraShowcaseMod.UI;
using CameraShowcaseMod.Input;
using CoreInputMod.ViewMode;
using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.UI;

namespace CameraShowcaseMod.Runtime
{
    internal sealed class CameraShowcaseRuntime
    {
        private readonly CameraShowcasePanelController _panelController;
        private bool _inputContextActive;

        public CameraShowcaseRuntime(IModContext context)
        {
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
                MountPanel(engine, activeMapId!, viewModeManager);
            }
            else
            {
                ClearTrackModeIfOwned(viewModeManager);
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

            ClearTrackModeIfOwned(ResolveViewModeManager(engine));
            DeactivateInputContext(context.Get(CoreServiceKeys.InputHandler));
            ClearPanelIfOwned(context);
            return Task.CompletedTask;
        }

        public void RefreshPanel(GameEngine engine)
        {
            string? activeMapId = engine.CurrentMapSession?.MapId.Value;
            if (CameraShowcaseIds.IsShowcaseMap(activeMapId))
            {
                MountPanel(engine, activeMapId!, ResolveViewModeManager(engine));
            }
            else
            {
                ClearPanelIfOwned(engine);
            }
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

        private void MountPanel(GameEngine engine, string activeMapId, ViewModeManager? viewModeManager)
        {
            if (engine.GetService(CoreServiceKeys.UIRoot) is not UIRoot root)
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

        private static void ClearTrackModeIfOwned(ViewModeManager? viewModeManager)
        {
            if (viewModeManager != null &&
                string.Equals(viewModeManager.ActiveMode?.Id, CameraShowcaseIds.TrackModeId, StringComparison.OrdinalIgnoreCase))
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

            if (!input.HasAction(CameraShowcaseIds.TrackModeActionId))
            {
                throw new InvalidOperationException($"Missing input action: {CameraShowcaseIds.TrackModeActionId}");
            }
        }
    }
}
