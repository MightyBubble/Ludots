using System.Numerics;
using Ludots.Core.Presentation.Camera;

namespace Ludots.Adapter.UE5
{
    /// <summary>
    /// UE5 平台视口信息提供者（对标 RaylibViewController）。
    ///
    /// Ludots 视锥裁剪、WorldHud 投影等系统通过 <see cref="IViewController"/>
    /// 获取屏幕分辨率、FOV、宽高比，UE5 侧每帧将 Viewport 尺寸
    /// 写入 <see cref="UE5SharedCameraState"/> 后即自动生效。
    /// </summary>
    public sealed class UE5ViewController : IViewController
    {
        private readonly UE5SharedCameraState _state;

        public UE5ViewController(UE5SharedCameraState state) => _state = state;

        public Vector2 Resolution  => new(_state.ViewportWidth, _state.ViewportHeight);
        public float   Fov         => _state.ReadCameraFovYDeg();
        public float   AspectRatio => _state.ViewportWidth / MathF.Max(1f, _state.ViewportHeight);
    }
}
