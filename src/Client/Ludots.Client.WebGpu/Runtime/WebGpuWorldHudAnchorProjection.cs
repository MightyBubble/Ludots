namespace Ludots.Client.WebGpu.Runtime;

/// <summary>
/// CPU-side contract mirroring world-HUD WGSL anchor projection gates.
/// Behind-camera or invisible anchors must be suppressed; never divide by a near-zero or negative clip.w.
/// </summary>
public static class WebGpuWorldHudAnchorProjection
{
    public const float ClipWEpsilon = 1e-4f;

    public static bool ShouldSuppress(float visibility, float clipW) =>
        visibility <= 0f || clipW <= ClipWEpsilon;
}
