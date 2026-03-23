using Ludots.Core.Presentation.Camera;

namespace Ludots.Adapter.UE5
{
    /// <summary>
    /// UE5 平台相机适配器（对标 RaylibCameraAdapter）。
    ///
    /// Ludots <see cref="CameraPresenter"/> 计算出平滑相机状态后，
    /// 调用 <see cref="UpdateCamera"/> 将结果推入 <see cref="UE5SharedCameraState"/>；
    /// UE5 C# 侧（LudotsRenderSystem）再每帧读取 SharedState 来驱动
    /// UE PlayerCameraManager 或直接用于矩阵计算。
    /// </summary>
    public sealed class UE5CameraAdapter : ICameraAdapter
    {
        private readonly UE5SharedCameraState _state;

        public UE5CameraAdapter(UE5SharedCameraState state) => _state = state;

        public void UpdateCamera(in CameraRenderState3D state)
        {
            _state.PushCameraState(state);
        }
    }
}
