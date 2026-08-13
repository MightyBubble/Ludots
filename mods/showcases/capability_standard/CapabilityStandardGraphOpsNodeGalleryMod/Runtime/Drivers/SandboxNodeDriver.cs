using Ludots.Core.Presentation.DebugDraw;

namespace CapabilityStandardGraphOpsNodeGalleryMod.Runtime.Drivers;

public sealed class SandboxNodeDriver : IGraphOpsNodeDriver
{
    public void Seed(GraphOpsNodeDriverContext ctx) =>
        throw new InvalidOperationException(
            "SandboxNodeDriver is a fail-close stub. Replace this file with seed+execute adapted from CapabilityStandardAbilityGraphSandboxMod. Featured op=" + ctx.Vignette.Op);

    public void Tick(GraphOpsNodeDriverContext ctx) =>
        throw new InvalidOperationException(
            "SandboxNodeDriver is a fail-close stub. Replace this file with seed+execute adapted from CapabilityStandardAbilityGraphSandboxMod. Featured op=" + ctx.Vignette.Op);

    public void DrawOverlay(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer debugDraw) { }
}
