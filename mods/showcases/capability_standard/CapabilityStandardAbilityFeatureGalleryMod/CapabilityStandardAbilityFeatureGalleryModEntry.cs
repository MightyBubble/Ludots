using System.Threading.Tasks;
using CapabilityStandardAbilityFeatureGalleryMod.Runtime;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace CapabilityStandardAbilityFeatureGalleryMod;

public sealed class CapabilityStandardAbilityFeatureGalleryModEntry : IMod
{
    public static readonly ServiceKey<AbilityFeatureMetrics> MetricsKey =
        new("CapabilityStandardAbilityFeatureGallery.Metrics");

    public void OnLoad(IModContext context)
    {
        context.Log("[CapabilityStandardAbilityFeatureGalleryMod] Loaded (per-feature Ability gallery host)");
        var runtime = new AbilityFeatureGalleryRuntime();
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            GameEngine engine = ctx.GetEngine()
                ?? throw new InvalidOperationException(
                    "CapabilityStandardAbilityFeatureGalleryMod GameStart requires GameEngine.");

            string? startupMapId = engine.MergedConfig?.StartupMapId;
            if (!AbilityFeatureIds.TryParseFeatureFromMapId(startupMapId, out _))
            {
                return Task.CompletedTask;
            }

            runtime.BindFromStartupMapId(startupMapId);
            runtime.AttachEngine(engine);
            engine.SetService(MetricsKey, runtime.Metrics);
            var debugDraw = new DebugDrawCommandBuffer();
            engine.SetService(CoreServiceKeys.DebugDrawCommandBuffer, debugDraw);
            ScreenOverlayBuffer overlay = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer)
                ?? throw new InvalidOperationException("Ability feature gallery requires ScreenOverlayBuffer.");
            engine.RegisterSystem(new AbilityFeatureGallerySimulationSystem(engine, runtime), SystemGroup.PostMovement);
            engine.RegisterPresentationSystem(new AbilityFeatureGalleryPresentationSystem(runtime, debugDraw, overlay));
            return Task.CompletedTask;
        });
        context.OnEvent(GameEvents.MapLoaded, _ =>
        {
            if (runtime.IsBound)
            {
                runtime.EnsureActors();
            }

            return Task.CompletedTask;
        });
    }

    public void OnUnload() { }
}
