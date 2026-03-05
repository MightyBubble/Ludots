using System.Numerics;
using Ludots.Platform.Abstractions;

namespace Ludots.Client.Godot.Services
{
    /// <summary>
    /// Godot implementation of IScreenRayProvider. Uses Camera3D.ProjectRayOrigin/ProjectRayNormal.
    /// </summary>
    public sealed class GodotScreenRayProvider : IScreenRayProvider
    {
        private readonly global::Godot.Camera3D _camera;

        public GodotScreenRayProvider(global::Godot.Camera3D camera)
        {
            _camera = camera;
        }

        public ScreenRay GetRay(Vector2 screenPosition)
        {
            var screen = new global::Godot.Vector2(screenPosition.X, screenPosition.Y);
            var origin = _camera.ProjectRayOrigin(screen);
            var normal = _camera.ProjectRayNormal(screen);
            return new ScreenRay(
                new Vector3((float)origin.X, (float)origin.Y, (float)origin.Z),
                new Vector3((float)normal.X, (float)normal.Y, (float)normal.Z));
        }
    }
}
