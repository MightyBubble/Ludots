using Ludots.UI;
using Ludots.UI.Runtime.Serialization;

namespace Ludots.Adapter.Web.Services
{
    public sealed class WebUiFrameSource
    {
        private readonly UIRoot _uiRoot;
        private readonly WebViewController _viewController;
        private readonly UiSceneDiffJsonSerializer _sceneDiffSerializer = new();
        private long _lastSceneVersion = -1;

        public WebUiFrameSource(UIRoot uiRoot, WebViewController viewController)
        {
            _uiRoot = uiRoot;
            _viewController = viewController;
        }

        public bool TryConsume(out string? sceneDiffJson)
        {
            sceneDiffJson = null;
            if (_uiRoot.Scene == null)
            {
                return false;
            }

            if (!_uiRoot.Scene.IsDirty && _uiRoot.Scene.Version == _lastSceneVersion)
            {
                return false;
            }

            var resolution = _viewController.Resolution;
            sceneDiffJson = _sceneDiffSerializer.Serialize(_uiRoot.Scene, resolution.X, resolution.Y);
            _lastSceneVersion = _uiRoot.Scene.Version;
            return true;
        }
    }
}
