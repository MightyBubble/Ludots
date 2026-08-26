using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Presentation.Hud;
using Ludots.Platform.Abstractions;

namespace CapabilityStandardAbilityFeatureGalleryMod.Runtime;

public sealed class AbilityFeatureGalleryPresentationSystem : Arch.System.ISystem<float>
{
    private readonly AbilityFeatureGalleryRuntime _runtime;
    private readonly DebugDrawCommandBuffer _debugDraw;
    private readonly ScreenOverlayBuffer _overlay;

    public AbilityFeatureGalleryPresentationSystem(
        AbilityFeatureGalleryRuntime runtime,
        DebugDrawCommandBuffer debugDraw,
        ScreenOverlayBuffer overlay)
    {
        _runtime = runtime;
        _debugDraw = debugDraw;
        _overlay = overlay;
    }

    public void Initialize() { }
    public void BeforeUpdate(in float t) { }
    public void AfterUpdate(in float t) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        _debugDraw.Clear();
        GraphShowcaseStagePresenter.DrawPlayerCaption(_overlay, _runtime.Title, _runtime.Metrics.Detail);
    }
}
