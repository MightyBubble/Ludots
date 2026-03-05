using System;
using System.IO;
using Godot;
using Ludots.Adapter.Godot;
using Ludots.Adapter.Godot.Services;
using Ludots.Client.Godot.Input;
using Ludots.Client.Godot.Rendering;
using Ludots.Client.Godot.Services;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.DebugDraw;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Scripting;

namespace Ludots.App.Godot;

public partial class Main : Node3D
{
    private GodotInputBackend? _inputBackend;
    private GodotHostLoop? _hostLoop;
    private Node3D? _primitiveContainer;
    private GodotPrimitiveRenderer? _primitiveRenderer;
    private GodotDebugDrawRenderer? _debugDrawRenderer;
    private GodotScreenHudDrawer? _hudDrawer;

    public override void _Ready()
    {
        var baseDir = ProjectSettings.GlobalizePath("res://");

        _inputBackend = new GodotInputBackend();
        AddChild(_inputBackend);

        var camera = GetNode<global::Godot.Camera3D>("Camera3D");
        var viewController = new GodotViewController(camera);
        var cameraAdapter = new GodotCameraAdapter(camera);
        var screenRayProvider = new GodotScreenRayProvider(camera);

        var setup = GodotHostComposer.Compose(baseDir, "game.json", _inputBackend);
        var context = new GodotHostContext(viewController, cameraAdapter, screenRayProvider);
        _hostLoop = new GodotHostLoop(setup, context);

        _primitiveContainer = new Node3D();
        AddChild(_primitiveContainer);
        _primitiveRenderer = new GodotPrimitiveRenderer(_primitiveContainer);
        _debugDrawRenderer = new GodotDebugDrawRenderer { PlaneY = 0.35f };

        var hudLayer = new CanvasLayer();
        AddChild(hudLayer);
        _hudDrawer = new GodotScreenHudDrawer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _hudDrawer.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        hudLayer.AddChild(_hudDrawer);
        _hudDrawer.SetEngine(() => _hostLoop?.Engine);

        _hostLoop.Initialize();
    }

    public override void _Process(double delta)
    {
        _hostLoop?.Tick((float)delta);

        var engine = _hostLoop?.Engine;
        if (engine == null || _primitiveRenderer == null || _debugDrawRenderer == null) return;

        if (engine.GlobalContext.TryGetValue(ContextKeys.PresentationPrimitiveDrawBuffer, out var drawObj) &&
            engine.GlobalContext.TryGetValue(ContextKeys.PresentationMeshAssetRegistry, out var meshObj) &&
            drawObj is PrimitiveDrawBuffer draw &&
            meshObj is MeshAssetRegistry meshes)
        {
            _primitiveRenderer.Draw(draw, meshes);
        }

        if (engine.GlobalContext.TryGetValue(ContextKeys.DebugDrawCommandBuffer, out var ddObj) &&
            ddObj is DebugDrawCommandBuffer dd &&
            _primitiveContainer != null)
        {
            _debugDrawRenderer.Draw(dd, _primitiveContainer);
        }
    }

    public override void _ExitTree()
    {
        _hostLoop?.Stop();
    }
}
