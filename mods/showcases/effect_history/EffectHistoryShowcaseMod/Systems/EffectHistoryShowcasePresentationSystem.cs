using Arch.System;
using EffectHistoryShowcaseMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;

namespace EffectHistoryShowcaseMod.Systems;

internal sealed class EffectHistoryShowcasePresentationSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly EffectHistoryShowcaseRuntime _runtime;
    private readonly PrimitiveDrawBuffer? _primitives;
    private readonly ScreenOverlayBuffer? _overlay;
    private readonly int _sphereMeshId;
    private readonly int _cubeMeshId;
    public EffectHistoryShowcasePresentationSystem(GameEngine engine, EffectHistoryShowcaseRuntime runtime)
    {
        _engine = engine;
        _runtime = runtime;
        _primitives = engine.GetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer);
        _overlay = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer);
        MeshAssetRegistry? meshes = engine.GetService(CoreServiceKeys.PresentationMeshAssetRegistry);
        _sphereMeshId = meshes?.GetId(WellKnownMeshKeys.Sphere) ?? 2;
        _cubeMeshId = meshes?.GetId(WellKnownMeshKeys.Cube) ?? 1;
    }
    public void Initialize() { }
    public void BeforeUpdate(in float t) { }
    public void Update(in float t)
    {
        if (_primitives != null) _runtime.EmitPrimitives(_primitives, _sphereMeshId, _cubeMeshId);
        if (_overlay != null) _runtime.DrawOverlay(_overlay);
        _runtime.RefreshPanel(_engine);
    }
    public void AfterUpdate(in float t) { }
    public void Dispose() { }
}
