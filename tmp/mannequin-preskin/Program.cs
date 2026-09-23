using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Ludots.Platform.Abstractions;
using Ludots.Raylib.Render;
using Raylib_cs;
using SkiaSharp;
using Rl = Raylib_cs.Raylib;

internal static unsafe class Program
{
    [DllImport("opengl32.dll", EntryPoint = "glGetString")] private static extern IntPtr GetGlString(uint name);
    [DllImport("opengl32.dll", EntryPoint = "glGetIntegerv")] private static extern void GetGlIntegers(uint name, int* values);
    private static readonly Color Background = new(34, 40, 48, 255);

    static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--normal-self-check")
        {
            NormalCorrection.SelfCheck();
            return 0;
        }
        int population = args.Length > 0 ? int.Parse(args[0]) : 10000;
        int frames = args.Length > 1 ? int.Parse(args[1]) : 192;
        int warmup = args.Length > 2 ? int.Parse(args[2]) : 64;
        if (population is not (1000 or 5000 or 10000)) throw new ArgumentOutOfRangeException(nameof(population));
        if (frames <= warmup || frames % 4 != 0 || warmup < 4 || warmup % 4 != 0)
            throw new ArgumentException("Frames and warmup must be multiples of four; frames must exceed warmup >= 4.");
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        string output = Path.Combine(root, "tmp/mannequin-preskin", $"results-{population}-normal-fix");
        Directory.CreateDirectory(output);
        string path = Path.Combine(root, "projects/engine_gallery/Models/mannequin_large_walk.glb");
        if (!File.Exists(path)) throw new FileNotFoundException("Required mannequin model missing.", path);
        Environment.SetEnvironmentVariable("LUDOTS_RAYLIB_SYNC_ASSET_LOAD", "1");
        Rl.SetConfigFlags(0x80);
        Rl.InitWindow(1280, 720, $"Mannequin shared geometry A/B {population}");
        try
        {
            var assets = new Assets(path);
            using var cache = new RaylibGpuSkinnedModelCache(assets);
            var descriptor = assets.Descriptor;
            var acquire = cache.TryGetOrLoad(1, in descriptor, out var entry, out var status);
            if (acquire != RaylibGpuSkinnedModelAcquireOutcome.Resident)
                throw new InvalidOperationException($"Required synchronous model load failed: {acquire} {status}");
            long geometryBytes = ValidateModel(entry, out float[][] bindVertices, out float[][] bindNormals);
            using var renderer = new CpuPreskinProbeRenderer(cache, new RaylibInstancedMaterialPipeline(null), population);
            var stateMap = new Dictionary<int, int> { [42] = 3 };
            renderer.AnimationStateMapResolver = (profile, source) => profile == 1 && source == path
                ? stateMap : throw new InvalidOperationException("Unexpected animation binding.");
            using var shadow = new RaylibDirectionalShadowMap();
            var light = RaylibFrameLighting.LoadFromDefaultPath();
            var camera = new Camera3D { position = new Vector3(24, 23, 31), target = Vector3.Zero, up = Vector3.UnitY, fovy = 48, projection = 0 };
            renderer.ApplyFrameLighting(light, camera.position, shadow, 0.12f);
            using var gpu = new ProbeGpuTimer(frames);
            var samples = new FrameSample[frames];
            var viewport = new int[4];
            fixed (int* values = viewport) GetGlIntegers(0x0BA2, values);
            var metadata = new
            {
                population, frames, warmup, model = path,
                model_sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))),
                renderer_source_sha256 = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "tmp/mannequin-preskin/snapshot-manifest.json"))).RootElement.GetProperty("renderer_sha256").GetString(),
                gl_vendor = Marshal.PtrToStringAnsi(GetGlString(0x1F00)),
                gl_renderer = Marshal.PtrToStringAnsi(GetGlString(0x1F01)),
                gl_version = Marshal.PtrToStringAnsi(GetGlString(0x1F02)),
                screen_width = Rl.GetScreenWidth(), screen_height = Rl.GetScreenHeight(),
                framebuffer_width = Rl.GetRenderWidth(), framebuffer_height = Rl.GetRenderHeight(), viewport,
                mesh_count = entry.Model.meshCount, bones = entry.Model.boneCount, animation_count = entry.AnimCount,
                selected_clip = 3, native_geometry_upload_bytes = geometryBytes,
                normal_correction_upload_bytes = geometryBytes / 2,
                normal_correction_source_sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(root, "tmp/mannequin-preskin/NormalCorrection.cs")))),
                normal_correction = "After native UpdateModelAnimation, recalculate animNormals with the weighted upper 3x3 bone matrix and original normals, excluding translation; upload VBO 2 again. Both computation and second normal upload are included in preskin and frame totals.",
                schedule = "ABBA; frames 0/1 share a pose, 2/3 share the next pose; A=GPU texture skin, B=CPU skin once + identity skin shader",
                timing = "Native GL timestamps: prepare, shadow draw, main draw. Wall total includes per-frame glFinish. Restore original VBOs and drain before baseline outside timed frame. Synchronized renderer-only experiment; not whole application FPS.",
                capture = "EndMode3D -> glFinish -> framebuffer readback -> EndDrawing; three matching-pose pairs after timed frames"
            };
            File.WriteAllText(Path.Combine(output, "metadata.json"), JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine(JsonSerializer.Serialize(metadata));
            for (int frame = 0; frame < frames; frame++)
            {
                if (Rl.WindowShouldClose()) throw new InvalidOperationException("Probe was closed before all samples completed.");
                bool optimized = frame % 4 is 1 or 2;
                float time = ((frame / 2) % 62) / 61f;
                samples[frame] = RenderFrame(frame, time, optimized, population, renderer, assets, entry.Model, shadow, light, camera, gpu, null);
                if (frame % 32 == 31) Console.WriteLine($"Completed {frame + 1}/{frames} paired frames.");
            }
            gpu.Collect();
            AssertBindDataUnchanged(entry.Model, bindVertices, bindNormals);
            WriteCsv(Path.Combine(output, "frames.csv"), samples, gpu, population, warmup);
            var comparisons = new List<object>(3);
            float[] captureTimes = [0f, 0.25f, 0.5f];
            for (int index = 0; index < captureTimes.Length; index++)
            {
                string baseline = Path.Combine(output, $"pose-{index}-baseline.png");
                string optimized = Path.Combine(output, $"pose-{index}-preskin.png");
                RenderFrame(-1, captureTimes[index], false, population, renderer, assets, entry.Model, shadow, light, camera, null, baseline);
                RenderFrame(-1, captureTimes[index], true, population, renderer, assets, entry.Model, shadow, light, camera, null, optimized);
                comparisons.Add(ComparePixels(baseline, optimized, Path.Combine(output, $"pose-{index}-difference-x8.png"), captureTimes[index]));
            }
            File.WriteAllText(Path.Combine(output, "pixels.json"), JsonSerializer.Serialize(comparisons, new JsonSerializerOptions { WriteIndented = true }));
            AssertBindDataUnchanged(entry.Model, bindVertices, bindNormals);
            RestoreBindVbos(entry.Model);
            ProbeGpuTimer.Finish();
            Console.WriteLine("Completed; raw frames, metadata, three matching-pose pairs and pixel differences: " + output);
        }
        finally { Rl.CloseWindow(); }
        return 0;
    }

    private static FrameSample RenderFrame(int frame, float normalizedTime, bool optimized, int population,
        CpuPreskinProbeRenderer renderer, Assets assets, Model model, RaylibDirectionalShadowMap shadow,
        RaylibFrameLighting light, Camera3D camera, ProbeGpuTimer? gpu, string? screenshot)
    {
        ProbeGpuTimer.Finish();
        long restoreAlloc = GC.GetAllocatedBytesForCurrentThread();
        long restoreStart = Stopwatch.GetTimestamp();
        long restoredBytes = optimized ? 0 : RestoreBindVbos(model);
        double restoreSubmitMs = optimized ? 0 : Stopwatch.GetElapsedTime(restoreStart).TotalMilliseconds;
        ProbeGpuTimer.Finish();
        double restoreAndDrainMs = optimized ? 0 : Stopwatch.GetElapsedTime(restoreStart).TotalMilliseconds;
        restoreAlloc = GC.GetAllocatedBytesForCurrentThread() - restoreAlloc;
        int gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1);
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        renderer.EnableCpuPreskin = optimized;
        renderer.EnableSharedPoseUniforms = false;
        renderer.ResetStats();
        renderer.Prepare();
        var animator = AnimatorPackedState.Create(1);
        animator.SetPrimaryStateIndex(42);
        animator.SetNormalizedTime01(normalizedTime);
        animator.SetFlags(AnimatorPackedStateFlags.Active | AnimatorPackedStateFlags.Looping);
        int side = (int)Math.Ceiling(Math.Sqrt(population));
        for (int i = 0; i < population; i++)
        {
            var item = new SkinnedVisualBatchItem
            {
                StableId = i + 1, OwnerStableId = i + 1, MeshAssetId = 1, MaterialId = 0, AnimationProfileId = 1,
                RenderPath = VisualRenderPath.GpuSkinnedInstance, AssetKind = AssetKind.SkinnedMesh,
                Position = new Vector3((i % side - side / 2) * 1.8f, 0, (i / side - side / 2) * 1.8f),
                Rotation = Quaternion.Identity, Scale = Vector3.One, Color = Vector4.One, Animator = animator
            };
            if (!renderer.TrySubmit(in item, assets, 1, out var outcome) || outcome != RaylibGpuSkinnedSubmitOutcome.Submitted)
                throw new InvalidOperationException("Required mannequin instance was not submitted.");
        }
        double submissionMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        Rl.BeginDrawing();
        gpu?.Start(frame, 0);
        long prepStart = Stopwatch.GetTimestamp();
        renderer.PrepareFrameGeometry();
        double prepMs = Stopwatch.GetElapsedTime(prepStart).TotalMilliseconds;
        gpu?.End();
        shadow.BeginFrame(light.SunDirectionToward, Vector3.Zero, 110);
        gpu?.Start(frame, 1);
        renderer.FlushShadow(shadow);
        gpu?.End();
        shadow.EndFrame();
        Rl.ClearBackground(Background);
        Rl.BeginMode3D(camera);
        renderer.ApplyFrameLighting(light, camera.position, shadow, 0.12f);
        var pbr = new RaylibPbrUniformLocations(-1, -1, -1, -1);
        gpu?.Start(frame, 2);
        renderer.Flush(default, in pbr, null);
        gpu?.End();
        Rl.EndMode3D();
        ProbeGpuTimer.Finish();
        if (screenshot != null) RaylibFramebufferCapture.WriteFramebufferPng(screenshot);
        Rl.EndDrawing();
        double totalMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        long allocBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        if (renderer.LastInstances != population || renderer.LastShadowInstances != population ||
            renderer.LastBatches != model.meshCount || renderer.LastShadowDraws != model.meshCount ||
            renderer.LastMainMeshInstances != population * model.meshCount || renderer.LastShadowMeshInstances != population * model.meshCount ||
            renderer.LastUniquePoses != 1 || renderer.LastPreskinCalls != (optimized ? 1 : 0))
            throw new InvalidOperationException("Draw, instance or shared-pose invariant failed.");
        return new FrameSample(optimized, normalizedTime, totalMs, submissionMs, prepMs, renderer.LastPreskinCpuUploadMs,
            renderer.LastTextureUploadCpuMs, renderer.LastTextureUploadBytes, renderer.LastPreskinUploadBytes,
            renderer.LastPreskinCalls, renderer.LastInstances, renderer.LastBatches, renderer.LastMainMeshInstances,
            renderer.LastShadowInstances, renderer.LastShadowDraws, renderer.LastShadowMeshInstances,
            allocBytes, GC.CollectionCount(0) - gen0, GC.CollectionCount(1) - gen1,
            restoreSubmitMs, restoreAndDrainMs, restoredBytes, restoreAlloc,
            renderer.LastNormalCorrectionCpuUploadMs, renderer.LastNormalCorrectionUploadBytes);
    }

    private static long ValidateModel(RaylibGpuSkinnedModelCache.Entry entry, out float[][] vertices, out float[][] normals)
    {
        Model model = entry.Model;
        if (model.meshCount != 6 || entry.AnimCount <= 3 || entry.Animations == null ||
            entry.MeshRigidBoneIndices.Length != model.meshCount)
            throw new InvalidOperationException("Unexpected mannequin model or animation layout.");
        vertices = new float[model.meshCount][];
        normals = new float[model.meshCount][];
        long bytes = 0;
        int totalBones = 0;
        for (int index = 0; index < model.meshCount; index++)
        {
            Mesh mesh = model.meshes[index];
            if (entry.MeshRigidBoneIndices.Span[index] != RaylibGltfMeshSkinBindings.SkinnedMesh || mesh.vertexCount <= 0 ||
                mesh.vertices == null || mesh.normals == null || mesh.animVertices == null || mesh.animNormals == null ||
                mesh.boneIds == null || mesh.boneWeights == null || mesh.boneMatrices == null ||
                mesh.boneCount <= 0 || mesh.boneCount > RaylibPoseTexturePalette.MaxBoneCount ||
                mesh.vboId == null || mesh.vboId[0] == 0 || mesh.vboId[2] == 0)
                throw new InvalidOperationException($"Mesh {index} is not a complete CPU/GPU-skinnable mesh.");
            totalBones += mesh.boneCount;
            for (int vertex = 0; vertex < mesh.vertexCount; vertex++)
            {
                float sum = 0;
                for (int influence = 0; influence < 4; influence++)
                {
                    int slot = vertex * 4 + influence;
                    float weight = mesh.boneWeights[slot];
                    if (!float.IsFinite(weight) || weight < 0 || (weight > 0 && mesh.boneIds[slot] >= mesh.boneCount))
                        throw new InvalidOperationException($"Invalid weight or bone index in mesh {index}, vertex {vertex}.");
                    sum += weight;
                }
                if (Math.Abs(sum - 1) > 0.001f)
                    throw new InvalidOperationException($"Non-normalized weights in mesh {index}, vertex {vertex}: {sum}.");
            }
            vertices[index] = new ReadOnlySpan<float>(mesh.vertices, mesh.vertexCount * 3).ToArray();
            normals[index] = new ReadOnlySpan<float>(mesh.normals, mesh.vertexCount * 3).ToArray();
            bytes += (long)mesh.vertexCount * 6 * sizeof(float);
        }
        if (totalBones > RaylibPoseTexturePalette.MaxBoneSlotCapacity)
            throw new InvalidOperationException("Model exceeds palette slot capacity.");
        return bytes;
    }

    private static long RestoreBindVbos(Model model)
    {
        long bytes = 0;
        for (int index = 0; index < model.meshCount; index++)
        {
            Mesh mesh = model.meshes[index];
            int size = checked(mesh.vertexCount * 3 * sizeof(float));
            Rl.UpdateMeshBuffer(mesh, 0, mesh.vertices, size, 0);
            Rl.UpdateMeshBuffer(mesh, 2, mesh.normals, size, 0);
            bytes += size * 2L;
        }
        return bytes;
    }

    private static void AssertBindDataUnchanged(Model model, float[][] vertices, float[][] normals)
    {
        for (int index = 0; index < model.meshCount; index++)
        {
            Mesh mesh = model.meshes[index];
            if (!new ReadOnlySpan<float>(mesh.vertices, mesh.vertexCount * 3).SequenceEqual(vertices[index]) ||
                !new ReadOnlySpan<float>(mesh.normals, mesh.vertexCount * 3).SequenceEqual(normals[index]))
                throw new InvalidOperationException("Native animation mutated original bind data; A/B baseline invalid.");
        }
    }

    private static void WriteCsv(string path, FrameSample[] samples, ProbeGpuTimer gpu, int population, int warmup)
    {
        using var writer = new StreamWriter(path);
        writer.WriteLine("frame,pair,mode,count,warmup,normalized_time,total_sync_ms,submission_cpu_ms,prep_cpu_ms,prep_gpu_interval_ms,shadow_gpu_ms,main_gpu_ms,preskin_cpu_upload_ms,texture_upload_cpu_ms,texture_upload_bytes,geometry_upload_bytes,preskin_calls,main_instances,main_draws,main_mesh_instances,shadow_instances,shadow_draws,shadow_mesh_instances,alloc_bytes,gen0,gen1,restore_submit_ms,restore_drain_ms,restore_bytes,restore_alloc_bytes,normal_correction_cpu_upload_ms,normal_correction_upload_bytes");
        for (int frame = 0; frame < samples.Length; frame++)
        {
            FrameSample s = samples[frame];
            writer.WriteLine(FormattableString.Invariant($"{frame},{frame / 2},{(s.Optimized ? "preskin" : "baseline")},{population},{(frame < warmup ? 1 : 0)},{s.Time},{s.TotalMs},{s.SubmitMs},{s.PrepMs},{gpu.Get(frame, 0)},{gpu.Get(frame, 1)},{gpu.Get(frame, 2)},{s.PreskinMs},{s.TextureMs},{s.TextureBytes},{s.GeometryBytes},{s.PreskinCalls},{s.MainInstances},{s.MainDraws},{s.MainMeshInstances},{s.ShadowInstances},{s.ShadowDraws},{s.ShadowMeshInstances},{s.AllocBytes},{s.Gen0},{s.Gen1},{s.RestoreSubmitMs},{s.RestoreDrainMs},{s.RestoreBytes},{s.RestoreAlloc},{s.NormalCorrectionMs},{s.NormalCorrectionBytes}"));
        }
    }

    private static object ComparePixels(string baseline, string optimized, string diffPath, float time)
    {
        using var a = SKBitmap.Decode(baseline) ?? throw new InvalidOperationException("Baseline PNG decoding failed.");
        using var b = SKBitmap.Decode(optimized) ?? throw new InvalidOperationException("Preskin PNG decoding failed.");
        if (a.Width != b.Width || a.Height != b.Height) throw new InvalidOperationException("Screenshot sizes differ.");
        using var difference = new SKBitmap(a.Width, a.Height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        long absolute = 0, foregroundAbsolute = 0, changed = 0, over8 = 0, foreground = 0;
        int maximum = 0;
        for (int y = 0; y < a.Height; y++) for (int x = 0; x < a.Width; x++)
        {
            SKColor ac = a.GetPixel(x, y), bc = b.GetPixel(x, y);
            int r = Math.Abs(ac.Red - bc.Red), g = Math.Abs(ac.Green - bc.Green), blue = Math.Abs(ac.Blue - bc.Blue);
            int max = Math.Max(r, Math.Max(g, blue));
            absolute += r + g + blue;
            maximum = Math.Max(maximum, max);
            if (max > 0) changed++;
            if (max > 8) over8++;
            bool fg = ac.Red != Background.r || ac.Green != Background.g || ac.Blue != Background.b ||
                bc.Red != Background.r || bc.Green != Background.g || bc.Blue != Background.b;
            if (fg) { foreground++; foregroundAbsolute += r + g + blue; }
            difference.SetPixel(x, y, new SKColor((byte)Math.Min(255, r * 8), (byte)Math.Min(255, g * 8), (byte)Math.Min(255, blue * 8)));
        }
        if (foreground == 0) throw new InvalidOperationException("Both screenshots contain only background; visual evidence is invalid.");
        using var image = SKImage.FromBitmap(difference);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(diffPath, data.ToArray());
        long pixels = a.Width * (long)a.Height;
        return new { normalized_time = time, baseline, preskin = optimized, diffPath, width = a.Width, height = a.Height,
            pixel_exact = changed == 0, changed_pixels = changed, changed_fraction = changed / (double)pixels,
            pixels_over_8 = over8, fraction_over_8 = over8 / (double)pixels, max_rgb_error = maximum,
            mean_rgb_error = absolute / (3d * pixels), foreground_pixels = foreground,
            foreground_mean_rgb_error = foregroundAbsolute / (3d * foreground) };
    }

    private readonly record struct FrameSample(bool Optimized, float Time, double TotalMs, double SubmitMs, double PrepMs,
        double PreskinMs, double TextureMs, long TextureBytes, long GeometryBytes, int PreskinCalls,
        int MainInstances, int MainDraws, int MainMeshInstances, int ShadowInstances, int ShadowDraws, int ShadowMeshInstances,
        long AllocBytes, int Gen0, int Gen1, double RestoreSubmitMs, double RestoreDrainMs, long RestoreBytes, long RestoreAlloc,
        double NormalCorrectionMs, long NormalCorrectionBytes);

    private sealed class Assets(string path) : IRenderMeshAssets, IRenderAssetPathResolver
    {
        public MeshAssetDescriptor Descriptor { get; } = new() { Id = 1, Type = MeshAssetType.Model, SourceUris = [path] };
        public bool TryResolveFullPath(string uri, out string full) { full = path; return uri == path; }
        public bool TryGetDescriptor(int id, out MeshAssetDescriptor descriptor) { descriptor = Descriptor; return id == 1; }
        public bool TryGetPrimitiveKind(int id, out PrimitiveMeshKind kind) { kind = default; return false; }
        public int GetId(string key) => key == "mannequin" ? 1 : throw new KeyNotFoundException(key);
        public string GetName(int id) => id == 1 ? "mannequin" : throw new KeyNotFoundException(id.ToString());
    }
}
