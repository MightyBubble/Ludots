using Ludots.Core.Presentation.DebugDraw;

namespace CapabilityStandardGraphOpsNodeGalleryMod.Runtime.Drivers;

public sealed class RelNodeDriver : IGraphOpsNodeDriver
{
    public void Seed(GraphOpsNodeDriverContext ctx) =>
        throw new InvalidOperationException(
            "RelNodeDriver is a fail-close stub. Replace this file with seed+execute adapted from CapabilityStandardGraphOpsRelMod.Runtime.GraphOpsRelRuntime. Featured op=" + ctx.Vignette.Op);

    public void Tick(GraphOpsNodeDriverContext ctx) =>
        throw new InvalidOperationException(
            "RelNodeDriver is a fail-close stub. Replace this file with seed+execute adapted from CapabilityStandardGraphOpsRelMod.Runtime.GraphOpsRelRuntime. Featured op=" + ctx.Vignette.Op);

    public void DrawOverlay(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw) { }
}
