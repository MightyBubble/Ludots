using System;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using Ludots.Core.Presentation.Terrain;
using Ludots.Platform.Abstractions;

internal static class Sweep
{
    public static void Run(string assetPath)
    {
        using FileStream fs = File.OpenRead(assetPath);
        ContinuousHeightmapAsset asset = ContinuousHeightmapBinary.Read(fs);
        var rt = new ContinuousHeightmapRuntime(asset, ContinuousHeightmapRenderProfile.CreateDefault());
        const int n = 10000;
        var rng = new Random(7);
        var pts = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            float x = ((float)rng.NextDouble() * 2f - 1f) * 120f;
            float z = ((float)rng.NextDouble() * 2f - 1f) * 120f;
            float h = rt.TrySampleHeightCm(x * 100f, z * 100f, out float hc) ? hc / 100f : 0f;
            pts[i] = new Vector3(x, h + 2.2f, z);
        }
        float yaw = 45f * MathF.PI / 180f, pitch = 42f * MathF.PI / 180f;
        Console.WriteLine("camDistM,perFrameMs,perRayUs,allocPerFrame,gen0");
        foreach (float distM in new[] { 60f, 140f, 300f, 800f, 2000f })
        {
            var cam = new Vector3(MathF.Cos(pitch) * MathF.Sin(yaw), MathF.Sin(pitch), MathF.Cos(pitch) * MathF.Cos(yaw)) * distM;
            int hits = 0; long chk = 0;
            for (int i = 0; i < n; i++) Ray(rt, cam, pts[i], ref hits, ref chk);
            long a0 = GC.GetAllocatedBytesForCurrentThread(); int g0 = GC.CollectionCount(0);
            const int reps = 20;
            var sw = Stopwatch.StartNew();
            for (int f = 0; f < reps; f++) for (int i = 0; i < n; i++) Ray(rt, cam, pts[i], ref hits, ref chk);
            sw.Stop();
            long a1 = GC.GetAllocatedBytesForCurrentThread(); int g1 = GC.CollectionCount(0);
            double ms = sw.Elapsed.TotalMilliseconds / reps;
            Console.WriteLine($"{distM},{ms:F3},{ms * 1000d / n:F3},{(a1 - a0) / (double)reps:F1},{g1 - g0}");
        }
    }

    private static void Ray(ContinuousHeightmapRuntime rt, Vector3 o, Vector3 t, ref int hits, ref long chk)
    {
        Vector3 d = t - o; float len = d.Length();
        if (rt.TryRaycastGround(new ScreenRay(o, d / len), out VisualGroundHit h)) { hits++; chk += (long)h.HeightCm; }
    }
}
