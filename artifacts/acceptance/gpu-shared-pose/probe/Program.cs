using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using Ludots.Core.Presentation.Rendering;
using Ludots.Platform.Abstractions;
using Ludots.Raylib.Render;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

internal static unsafe class Program
{
    private const int HiddenWindow = 0x80;
    private const int WarmupFrames = 48;
    private const int SampleFrames = 96;

    private static int Main(string[] args)
    {
        bool twoPoses = args.Any(a => string.Equals(a, "--two-poses", StringComparison.Ordinal));
        string[] numericArgs = args.Where(a => !string.Equals(a, "--two-poses", StringComparison.Ordinal)).ToArray();
        int[] populations = numericArgs.Length == 0 ? [1000, 5000, 10000] : numericArgs.Select(int.Parse).ToArray();
        string root = FindRoot();
        string output = Path.Combine(root, "artifacts/acceptance/gpu-shared-pose/probe");
        Directory.CreateDirectory(output);
        Environment.SetEnvironmentVariable("LUDOTS_RAYLIB_SYNC_ASSET_LOAD", "1");
        Rl.SetConfigFlags(HiddenWindow);
        Rl.InitWindow(1280, 720, "Gpu shared pose probe");
        try
        {
            var assets = new SoldierAssets(Path.Combine(root, "mods/capabilities/navigation/MassNavigationMod/assets/Models/mass_navigation_agent_soldier.glb"));
            using var cache = new RaylibGpuSkinnedModelCache(assets);
            using var renderer = new RaylibPrimitiveRenderer(RaylibPrimitiveRenderMode.Immediate, assets);
            using var shadow = new RaylibDirectionalShadowMap();
            RaylibFrameLighting lighting = RaylibFrameLighting.LoadFromJsonFile(
                Path.Combine(root, "src/Client/Ludots.Raylib.Render/Resources/ambient_day_ramp.json"),
                Path.Combine(root, "src/Client/Ludots.Raylib.Render/Resources/distance_fog.json"));
            var toggle = renderer.GetType().GetProperty("EnableSharedPoseUniforms", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var camera = new Camera3D { position = new Vector3(0, 22, 30), target = new Vector3(0, 0, 0), up = Vector3.UnitY, fovy = 38, projection = CameraProjection.CAMERA_PERSPECTIVE };
            var draws = new PrimitiveDrawBuffer(1);
            var snapshot = new PrimitiveDrawBuffer(1);
            AuditGpuTimer.Initialize(populations.Length * 2 * (WarmupFrames + SampleFrames) * 2);
            foreach (int population in populations)
            {
                if (population <= 0 || population > 10000) throw new ArgumentOutOfRangeException(nameof(population));
                foreach (bool shared in new[] { false, true })
                {
                    if (shared && toggle == null) throw new InvalidOperationException("RaylibPrimitiveRenderer.EnableSharedPoseUniforms is not available.");
                    toggle?.SetValue(renderer, shared);
                    string mode = shared ? "shared" : "baseline";
                    string csvPath = Path.Combine(output, $"{mode}-{population}.csv");
                    var rows = new List<FrameRow>(WarmupFrames + SampleFrames);
                    var batch = new SkinnedVisualBatchBuffer(population + 8);
                    int totalFrames = WarmupFrames + SampleFrames;
                    for (int frame = 0; frame < totalFrames; frame++)
                    {
                        int phase = frame < WarmupFrames ? 0 : 1;
                        int animatorFrame = (frame / 2) & 1;
                        batch.Clear();
                        FillBatch(batch, population, animatorFrame, twoPoses);
                        long allocBefore = GC.GetAllocatedBytesForCurrentThread();
                        int g0Before = GC.CollectionCount(0), g1Before = GC.CollectionCount(1);
                        int queryFrame = AuditGpuTimer.FrameIndex;
                        Rl.BeginDrawing();
                        try
                        {
                            shadow.BeginFrame(lighting.SunDirectionToward, Vector3.Zero, 48f);
                            AuditGpuTimer.Start(true);
                            renderer.DrawShadow(batch, shadow, assets, camera);
                            AuditGpuTimer.End();
                            shadow.EndFrame();
                            Rl.ClearBackground(new Color(10, 14, 20, 255));
                            Rl.BeginMode3D(camera);
                            renderer.ApplyFrameLighting(lighting, camera.position, shadow, 0.04f);
                            AuditGpuTimer.Start(false);
                            renderer.Draw(draws, camera, snapshot, batch, assets, 1f, null, null, Rl.GetTime());
                            AuditGpuTimer.End();
                            Rl.EndMode3D();
                        }
                        finally { Rl.EndDrawing(); }
                        rows.Add(new FrameRow(population, mode, frame, phase, queryFrame, renderer.LastGpuSkinnedInstances, renderer.LastGpuSkinnedBatches, renderer.LastGpuSkinnedUniquePoses, renderer.LastGpuSkinnedTextureUploadBytes, GC.GetAllocatedBytesForCurrentThread() - allocBefore, GC.CollectionCount(0) - g0Before, GC.CollectionCount(1) - g1Before));
                        AuditGpuTimer.FrameIndex++;
                    }
                    AuditGpuTimer.Collect();
                    using var csv = new StreamWriter(csvPath, false);
                    csv.WriteLine("population,mode,frame,phase,shadow_gpu_ms,main_gpu_ms,instances,batches,unique_poses,pose_upload_bytes,cpu_alloc_bytes,gen0,gen1");
                    foreach (FrameRow row in rows)
                        csv.WriteLine($"{row.Population},{row.Mode},{row.Frame},{row.Phase},{AuditGpuTimer.Get(row.QueryFrame, true):F6},{AuditGpuTimer.Get(row.QueryFrame, false):F6},{row.Instances},{row.Batches},{row.UniquePoses},{row.PoseUploadBytes},{row.AllocBytes},{row.Gen0},{row.Gen1}");
                }
            }
            File.WriteAllText(Path.Combine(output, "metadata.json"), $"{{\"commit\":\"{GitHead(root)}\",\"window\":\"1280x720\",\"warmup\":{WarmupFrames},\"samples\":{SampleFrames},\"populations\":[{string.Join(',', populations)}],\"two_pose\":true,\"shadow\":true}}\n");
            return 0;
        }
        finally { Rl.CloseWindow(); }
    }

    private static void FillBatch(SkinnedVisualBatchBuffer batch, int count, int phase, bool twoPoses)
    {
        int side = (int)MathF.Ceiling(MathF.Sqrt(count));
        for (int i = 0; i < count; i++)
        {
            int x = i % side, z = i / side;
            var animator = AnimatorPackedState.Create(1);
            int pose = twoPoses ? ((i & 1) ^ phase) : phase;
            animator.SetPrimaryStateIndex(pose);
            animator.SetNormalizedTime01(pose == 0 ? 0.31f : 0.69f);
            batch.TryAdd(new SkinnedVisualBatchItem { StableId = i + 1, OwnerStableId = i + 1, MeshAssetId = 1, MaterialId = 0, RenderPath = VisualRenderPath.GpuSkinnedInstance, AssetKind = AssetKind.SkinnedMesh, Position = new Vector3((x - side / 2f) * 1.25f, 0, (z - side / 2f) * 1.25f), Rotation = Quaternion.Identity, Scale = Vector3.One, Color = Vector4.One, Animator = animator, Visibility = VisualVisibility.Visible });
        }
    }

    private readonly record struct FrameRow(int Population, string Mode, int Frame, int Phase, int QueryFrame, int Instances, int Batches, int UniquePoses, long PoseUploadBytes, long AllocBytes, int Gen0, int Gen1);

    private static string FindRoot()
    {
        string? p = AppContext.BaseDirectory;
        while (p != null && !File.Exists(Path.Combine(p, "AGENTS.md"))) p = Path.GetDirectoryName(p);
        return p ?? throw new DirectoryNotFoundException("Ludots root not found.");
    }
    private static string GitHead(string root) => new Process { StartInfo = new ProcessStartInfo("git", "rev-parse HEAD") { WorkingDirectory = root, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true } }.RunAndRead().Trim();

    private sealed class SoldierAssets(string path) : IRenderMeshAssets, IRenderAssetPathResolver
    {
        public bool TryGetDescriptor(int id, out MeshAssetDescriptor descriptor) { descriptor = new MeshAssetDescriptor { Id = 1, Type = MeshAssetType.Model, SourceUris = [path] }; return id == 1; }
        public bool TryResolveFullPath(string uri, out string fullPath) { fullPath = path; return true; }
        public bool TryGetPrimitiveKind(int id, out PrimitiveMeshKind kind) { kind = default; return false; }
        public int GetId(string key) => 1;
        public string GetName(int id) => "mass_navigation.agent.soldier";
    }

    private static class ProcessExtensions
    {
        public static string RunAndRead(this Process p) { p.Start(); string s = p.StandardOutput.ReadToEnd(); p.WaitForExit(); return s; }
    }

    private static class AuditGpuTimer
    {
        private static readonly List<ulong> _ids = new(); private static readonly List<double> _results = new(); private static BeginQuery? _begin; private static EndQuery? _end; private static GetQuery? _get; private static int _slot;
        public static int FrameIndex;
        public static void Initialize(int capacity) { _ids.Clear(); _results.Clear(); _ids.AddRange(new ulong[capacity * 2]); _results.AddRange(Enumerable.Repeat(double.NaN, capacity * 2)); _begin = Load<BeginQuery>("glBeginQuery"); _end = Load<EndQuery>("glEndQuery"); _get = Load<GetQuery>("glGetQueryObjectui64v"); var gen = Load<GenQueries>("glGenQueries"); uint[] ids = new uint[capacity * 2]; fixed (uint* p = ids) gen(ids.Length, p); for (int i = 0; i < ids.Length; i++) _ids[i] = ids[i]; }
        public static void Start(bool shadow) { _slot = checked(FrameIndex * 2 + (shadow ? 1 : 0)); _begin!(0x88BF, (uint)_ids[_slot]); }
        public static void End() => _end!(0x88BF);
        public static void Collect() { for (int i = 0; i < _ids.Count; i++) { ulong ns; _get!((uint)_ids[i], 0x8866, &ns); _results[i] = ns / 1_000_000d; } }
        public static double Get(int frame, bool shadow) => _results[frame * 2 + (shadow ? 1 : 0)];
        private static T Load<T>(string name) where T : Delegate { IntPtr p = wglGetProcAddress(name); if (p == IntPtr.Zero) throw new InvalidOperationException("Missing OpenGL query API " + name); return Marshal.GetDelegateForFunctionPointer<T>(p); }
        [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void GenQueries(int n, uint* ids); [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void BeginQuery(uint target, uint id); [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void EndQuery(uint target); [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate void GetQuery(uint id, uint pname, ulong* value);
        [DllImport("opengl32.dll", CharSet = CharSet.Ansi)] private static extern IntPtr wglGetProcAddress(string name);
    }
}
