using Ludots.Platform.Abstractions;

namespace CapabilityStandardGraphOpsNodeGalleryMod.Runtime.Drivers;

/// <summary>
/// Driver for OfferActivity-style dispatch vignettes: each think wave runs the featured
/// graph (map roll-call → OfferActivity) and captions the wave count. World truth —
/// how many activity instances exist — belongs to ActivityRuntimeService; the driver
/// only projects the caption.
/// </summary>
public sealed class ActivityNodeDriver : IGraphOpsNodeDriver
{
    public void Seed(GraphOpsNodeDriverContext ctx)
    {
        GraphOpsNodeActorBinding.RequireMapActors(ctx);
        _ = GraphOpsNodeActorBinding.RequireRole(ctx, "caster");
        GraphOpsNodeActorBinding.BindHud(ctx);
    }

    public void Tick(GraphOpsNodeDriverContext ctx)
    {
        _ = ctx.ExecuteFeaturedGraph();
        ctx.CaptionValues["count"] = ctx.Wave.ToString();
        ctx.Metrics.Detail = GraphOpsNodeActorBinding.FormatDetail(ctx.Vignette.DetailTemplate, ctx.CaptionValues);
        GraphOpsNodeActorBinding.SyncHud(ctx);
    }

    public void DrawOverlay(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw)
    {
    }
}
