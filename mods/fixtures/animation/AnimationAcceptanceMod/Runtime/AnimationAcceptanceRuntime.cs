using System.Threading.Tasks;
using AnimationAcceptanceMod.UI;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Runtime;

namespace AnimationAcceptanceMod.Runtime
{
    internal sealed class AnimationAcceptanceRuntime
    {
        private AnimationAcceptancePanelController? _panelController;

        public Task HandleMapFocusedAsync(ScriptContext context)
        {
            var engine = context.GetEngine();
            if (engine == null)
            {
                return Task.CompletedTask;
            }

            ConfigureRenderDefaults(engine);
            RefreshPanel(engine);
            return Task.CompletedTask;
        }

        public Task HandleMapUnloadedAsync(ScriptContext context)
        {
            var engine = context.GetEngine();
            if (engine != null)
            {
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

            if (!string.Equals(engine.CurrentMapSession?.MapId.Value, AnimationAcceptanceIds.StartupMapId, System.StringComparison.OrdinalIgnoreCase))
            {
                ClearPanelIfOwned(engine);
                return;
            }

            if (engine.GetService(CoreServiceKeys.RenderDebugState) is RenderDebugState renderDebug &&
                !renderDebug.DrawSkiaUi)
            {
                ClearPanelIfOwned(engine);
                return;
            }

            EnsurePanelController(engine).MountOrSync(root, engine);
        }

        private void ConfigureRenderDefaults(GameEngine engine)
        {
            if (!string.Equals(engine.CurrentMapSession?.MapId.Value, AnimationAcceptanceIds.StartupMapId, System.StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (engine.GetService(CoreServiceKeys.RenderDebugState) is not RenderDebugState renderDebug)
            {
                return;
            }

            renderDebug.DrawSkiaUi = true;
            renderDebug.DrawPrimitives = true;
        }

        private AnimationAcceptancePanelController EnsurePanelController(GameEngine engine)
        {
            if (_panelController != null)
            {
                return _panelController;
            }

            var textMeasurer = (IUiTextMeasurer)engine.GetService(CoreServiceKeys.UiTextMeasurer);
            var imageSizeProvider = (IUiImageSizeProvider)engine.GetService(CoreServiceKeys.UiImageSizeProvider);
            _panelController = new AnimationAcceptancePanelController(textMeasurer, imageSizeProvider);
            return _panelController;
        }

        private void ClearPanelIfOwned(GameEngine engine)
        {
            if (engine.GetService(CoreServiceKeys.UIRoot) is not UIRoot root)
            {
                return;
            }

            _panelController?.ClearIfOwned(root);
        }
    }
}
