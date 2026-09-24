using System;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using Ludots.Core.Presentation.Terrain;
using Ludots.Platform.Abstractions;

internal static class Program
{
    private static int Main(string[] args)
    {
        string assetPath = args.Length > 0
            ? args[0]
            : "../../mods/capabilities/navigation/MassNavigationMod/assets/terrain/mass_navigation_large_world_relief.height";

        using FileStream fs = File.OpenRead(assetPath);
        ContinuousHeightmapAsset asset = ContinuousHeightmapBinary.Read(fs);
        var runtime = new ContinuousHeightmapRuntime(asset, ContinuousHeightmapRenderProfile.CreateDefault());

        Console.WriteLine($"asset cols={asset.SampleColumns} rows={asset.SampleRows} interp={asset.InterpolationMode} layout={asset.StorageLayout}");
        Console.WriteLine($"bounds L={asset.Bounds.Left} T={asset.Bounds.Top} W={asset.Bounds.Width} H={asset.Bounds.Height}");
        float cellWcm = asset.Bounds.Width / (float)(asset.SampleColumns - 1);
        Console.WriteLine($"cell={cellWcm:F1}cm ({cellWcm / 100f:F2}m)");

        // Live showcase camera: yaw 45, pitch 42, distance 140m, target world origin.
        const float distanceMeters = 140f;
        const float yawDeg = 45f;
        const float pitchDeg = 42f;
        float yaw = yawDeg * MathF.PI / 180f;
        float pitch = pitchDeg * MathF.PI / 180f;
        var camOffset = new Vector3(
            MathF.Cos(pitch) * MathF.Sin(yaw),
            MathF.Sin(pitch),
            MathF.Cos(pitch) * MathF.Cos(yaw)) * distanceMeters;

        // 10k agents scattered over the on-screen crowd footprint around the camera target.
        const int agentCount = 10000;
        var targets = new Vector3[agentCount];
        var rng = new Random(20260909);
        // Crowd spread: agents visible in a 140m-distance view span roughly +/-120m.
        const float spreadMeters = 120f;
        for (int i = 0; i < agentCount; i++)
        {
            float x = ((float)rng.NextDouble() * 2f - 1f) * spreadMeters;
            float z = ((float)rng.NextDouble() * 2f - 1f) * spreadMeters;
            float h = runtime.TrySampleHeightCm(x * 100f, z * 100f, out float hcm) ? hcm / 100f : 0f;
            // HUD anchors sit above the unit.
            targets[i] = new Vector3(x, h + 2.2f, z);
        }

        Vector3 camPos = new Vector3(0f, 0f, 0f) + camOffset;
        // Origin per WorldHudToScreenSystem = unprojected near-plane point, i.e. ~camera position.
        int hits = 0;
        long checksum = 0;

        // Warmup
        for (int i = 0; i < agentCount; i++)
        {
            RayOnce(runtime, camPos, targets[i], ref hits, ref checksum);
        }

        const int frames = 30;
        var sw = Stopwatch.StartNew();
        for (int f = 0; f < frames; f++)
        {
            for (int i = 0; i < agentCount; i++)
            {
                RayOnce(runtime, camPos, targets[i], ref hits, ref checksum);
            }
        }
        sw.Stop();
        double perFrameMs = sw.Elapsed.TotalMilliseconds / frames;
        Console.WriteLine($"RESULT agents={agentCount} frames={frames} perFrameMs={perFrameMs:F3} perRayUs={perFrameMs * 1000d / agentCount:F3} hits={hits} chk={checksum}");

        // Scale sweep to show it is linear in owner count, not a constant.
        foreach (int n in new[] { 1000, 5000, 10000, 20000 })
        {
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            int g0 = GC.CollectionCount(0);
            var s2 = Stopwatch.StartNew();
            const int reps = 20;
            for (int f = 0; f < reps; f++)
            {
                for (int i = 0; i < n; i++)
                {
                    RayOnce(runtime, camPos, targets[i % agentCount], ref hits, ref checksum);
                }
            }
            s2.Stop();
            long a1 = GC.GetAllocatedBytesForCurrentThread();
            int g1 = GC.CollectionCount(0);
            Console.WriteLine($"SWEEP n={n} perFrameMs={s2.Elapsed.TotalMilliseconds / reps:F3} allocBytesPerFrame={(a1 - a0) / (double)reps:F1} gen0={g1 - g0}");
        }

        Sweep.Run(assetPath);
        return 0;
    }

    private static void RayOnce(
        ContinuousHeightmapRuntime runtime,
        Vector3 origin,
        Vector3 target,
        ref int hits,
        ref long checksum)
    {
        Vector3 delta = target - origin;
        float dist = delta.Length();
        var ray = new ScreenRay(origin, delta / dist);
        if (runtime.TryRaycastGround(in ray, out VisualGroundHit hit))
        {
            hits++;
            checksum += (long)hit.HeightCm;
        }
    }
}
