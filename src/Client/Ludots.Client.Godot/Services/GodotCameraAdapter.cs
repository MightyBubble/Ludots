using Ludots.Core.Presentation.Camera;

namespace Ludots.Client.Godot.Services
{
    /// <summary>
    /// Godot implementation of ICameraAdapter. Updates Godot Camera3D from CameraRenderState3D.
    /// </summary>
    public sealed class GodotCameraAdapter : ICameraAdapter
    {
        private readonly global::Godot.Camera3D _camera;

        public GodotCameraAdapter(global::Godot.Camera3D camera)
        {
            _camera = camera;
        }

        public void UpdateCamera(in CameraRenderState3D state)
        {
            _camera.GlobalPosition = new global::Godot.Vector3(state.Position.X, state.Position.Y, state.Position.Z);
            _camera.LookAt(new global::Godot.Vector3(state.Target.X, state.Target.Y, state.Target.Z), new global::Godot.Vector3(state.Up.X, state.Up.Y, state.Up.Z));
            _camera.Fov = state.FovYDeg;
        }
    }
}
