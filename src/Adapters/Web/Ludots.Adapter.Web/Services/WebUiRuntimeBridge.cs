using System;
using Ludots.UI;
using Ludots.UI.Input;
using Ludots.UI.Runtime;
using Ludots.UI.Runtime.Serialization;

namespace Ludots.Adapter.Web.Services
{
    public sealed class WebUiRuntimeBridge
    {
        private readonly UIRoot _uiRoot;
        private readonly WebInputBackend _inputBackend;
        private readonly WebViewController _viewController;
        private readonly UiSceneDiffJsonSerializer _serializer = new();
        private readonly UiScene _emptyScene = new();
        private bool _pointerCaptured;
        private bool _hadMountedScene;

        public WebUiRuntimeBridge(UIRoot uiRoot, WebInputBackend inputBackend, WebViewController viewController)
        {
            _uiRoot = uiRoot;
            _inputBackend = inputBackend;
            _viewController = viewController;
        }

        public bool Update(float deltaSeconds)
        {
            SyncResolution();
            _uiRoot.Update(deltaSeconds);

            bool blockThisTick = false;
            while (_inputBackend.TryDequeuePointerEvent(out PointerEvent? pointerEvent))
            {
                if (pointerEvent == null)
                {
                    continue;
                }

                bool handled = _uiRoot.HandleInput(pointerEvent);
                switch (pointerEvent.Action)
                {
                    case PointerAction.Down:
                        if (handled)
                        {
                            _pointerCaptured = true;
                            blockThisTick = true;
                        }
                        break;
                    case PointerAction.Up:
                    case PointerAction.Cancel:
                        if (_pointerCaptured)
                        {
                            blockThisTick = true;
                        }
                        _pointerCaptured = false;
                        break;
                    case PointerAction.Scroll:
                        blockThisTick |= handled;
                        break;
                }
            }

            return _pointerCaptured || blockThisTick;
        }

        public bool TryConsumeScene(out string? sceneJson)
        {
            SyncResolution();

            float width = Math.Max(1f, _uiRoot.Width);
            float height = Math.Max(1f, _uiRoot.Height);
            UiScene? scene = _uiRoot.Scene;

            if (scene == null)
            {
                if (!_hadMountedScene)
                {
                    sceneJson = null;
                    return false;
                }

                sceneJson = _serializer.Serialize(_emptyScene, width, height);
                _hadMountedScene = false;
                _uiRoot.IsDirty = false;
                return true;
            }

            if (!_uiRoot.IsDirty)
            {
                sceneJson = null;
                return false;
            }

            sceneJson = _serializer.Serialize(scene, width, height);
            _hadMountedScene = true;
            _uiRoot.IsDirty = false;
            return true;
        }

        private void SyncResolution()
        {
            var resolution = _viewController.Resolution;
            if (Math.Abs(_uiRoot.Width - resolution.X) > 0.01f ||
                Math.Abs(_uiRoot.Height - resolution.Y) > 0.01f)
            {
                _uiRoot.Resize(resolution.X, resolution.Y);
            }
        }
    }
}
