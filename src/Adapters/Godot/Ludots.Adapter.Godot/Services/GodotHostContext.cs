using Ludots.Core.Presentation.Camera;
using Ludots.Platform.Abstractions;

namespace Ludots.Adapter.Godot.Services;

/// <summary>
/// Platform-specific services for Godot. Created by the Godot app from Nodes.
/// </summary>
public sealed class GodotHostContext
{
    public IViewController ViewController { get; }
    public ICameraAdapter CameraAdapter { get; }
    public IScreenRayProvider ScreenRayProvider { get; }

    public GodotHostContext(
        IViewController viewController,
        ICameraAdapter cameraAdapter,
        IScreenRayProvider screenRayProvider)
    {
        ViewController = viewController;
        CameraAdapter = cameraAdapter;
        ScreenRayProvider = screenRayProvider;
    }
}
