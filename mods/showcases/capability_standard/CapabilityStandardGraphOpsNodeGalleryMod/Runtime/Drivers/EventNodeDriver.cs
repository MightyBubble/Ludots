using Ludots.Core.Presentation.DebugDraw;

namespace CapabilityStandardGraphOpsNodeGalleryMod.Runtime.Drivers;

public sealed class EventNodeDriver : IGraphOpsNodeDriver
{
    public void Seed(GraphOpsNodeDriverContext ctx) =>
        throw new InvalidOperationException(
            "EventNodeDriver is a fail-close stub. Replace this file with seed+execute adapted from CapabilityStandardGraphOpsEventMod.Runtime.GraphOpsEventRuntime. Featured op=" + ctx.Vignette.Op);

    public void Tick(GraphOpsNodeDriverContext ctx) =>
        throw new InvalidOperationException(
            "EventNodeDriver is a fail-close stub. Replace this file with seed+execute adapted from CapabilityStandardGraphOpsEventMod.Runtime.GraphOpsEventRuntime. Featured op=" + ctx.Vignette.Op);

    public void DrawOverlay(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw) { }
}
