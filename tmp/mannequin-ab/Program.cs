using System.Diagnostics;
using System.Numerics;
using Ludots.Platform.Abstractions;
using Ludots.Raylib.Render;
using Raylib_cs;
using Rl=Raylib_cs.Raylib;

internal static unsafe class Program
{
    const int Frames=192, Warmup=64;
    static int Main(string[] args)
    {
        int population = args.Length == 0 ? 10000 : int.Parse(args[0]);
        if (population is not (1000 or 5000 or 10000)) throw new ArgumentOutOfRangeException(nameof(population));
        string root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
        string path=Path.Combine(root,"projects/engine_gallery/Models/mannequin_large_walk.glb");
        Environment.SetEnvironmentVariable("LUDOTS_RAYLIB_SYNC_ASSET_LOAD","1");
        Rl.SetConfigFlags(0x80); Rl.InitWindow(1280,720,"mannequin gpu ab");
        try
        {
            var assets=new Assets(path); using var cache=new RaylibGpuSkinnedModelCache(assets);
            var d=assets.Descriptor; var acq=cache.TryGetOrLoad(1,in d,out var e,out var status); if(acq!=RaylibGpuSkinnedModelAcquireOutcome.Resident) throw new Exception($"load {acq} {status}");
            Console.WriteLine($"mannequin meshes={e.Model.meshCount} bones={e.Model.boneCount} anims={e.AnimCount}");
            using var r=new RaylibGpuSkinnedBatchRenderer(cache,new RaylibInstancedMaterialPipeline(null),32768);
            var stateMap = new Dictionary<int, int> { [41] = 0, [42] = 3 };
            r.AnimationStateMapResolver = (profile, source) => profile == 1 && source == path ? stateMap : throw new InvalidOperationException("Unexpected animation binding");
            using var shadow=new RaylibDirectionalShadowMap(); var light=RaylibFrameLighting.LoadFromDefaultPath();
            var cam=new Camera3D{position=new Vector3(24,23,31),target=Vector3.Zero,up=Vector3.UnitY,fovy=48,projection=0}; var pbr=new RaylibPbrUniformLocations(-1,-1,-1,-1);
            AuditGpuTimer.Initialize(Frames);
            double[] totalMs=new double[Frames]; long[] allocBytes=new long[Frames]; int[] instances=new int[Frames],draws=new int[Frames],poses=new int[Frames],sharedInstances=new int[Frames];
            for(int f=0;f<Frames;f++)
            {
                bool opt=(f%2)==1; long alloc=GC.GetAllocatedBytesForCurrentThread(); long start=Stopwatch.GetTimestamp(); r.EnableSharedPoseUniforms=opt; r.ResetStats(); r.Prepare();
                var a=AnimatorPackedState.Create(1); a.SetPrimaryStateIndex(42); a.SetNormalizedTime01(((f/2)%62)/61f); a.SetFlags(AnimatorPackedStateFlags.Active|AnimatorPackedStateFlags.Looping);
                int side=(int)Math.Ceiling(Math.Sqrt(population)); for(int i=0;i<population;i++){ var item=new SkinnedVisualBatchItem{StableId=i+1,OwnerStableId=i+1,MeshAssetId=1,MaterialId=0,AnimationProfileId=1,RenderPath=VisualRenderPath.GpuSkinnedInstance,AssetKind=AssetKind.SkinnedMesh,Position=new Vector3((i%side-side/2)*1.8f,0,(i/side-side/2)*1.8f),Rotation=Quaternion.Identity,Scale=Vector3.One,Color=Vector4.One,Animator=a}; if(!r.TrySubmit(in item,assets,1))throw new Exception("submit"); }
                Rl.BeginDrawing(); shadow.BeginFrame(light.SunDirectionToward,Vector3.Zero,110); AuditGpuTimer.FrameIndex=f; AuditGpuTimer.Start(true); r.FlushShadow(shadow); AuditGpuTimer.End(); shadow.EndFrame(); Rl.ClearBackground(new Color(34,40,48,255)); Rl.BeginMode3D(cam); r.ApplyFrameLighting(light,cam.position,shadow,0.12f); AuditGpuTimer.Start(false); r.Flush(default,in pbr,null); AuditGpuTimer.End(); Rl.EndMode3D();
                if(f is 60 or 61) Rl.TakeScreenshot($"equal-pose-{population}-{(opt?"optimized":"baseline")}.png");
                Rl.EndDrawing();
                totalMs[f]=Stopwatch.GetElapsedTime(start).TotalMilliseconds; allocBytes[f]=GC.GetAllocatedBytesForCurrentThread()-alloc; instances[f]=r.LastInstances; draws[f]=r.LastBatches; poses[f]=r.LastUniquePoses; sharedInstances[f]=r.LastSharedPoseInstances;
                if(r.LastInstances!=population||r.LastBatches!=6||r.LastUniquePoses!=1)throw new Exception("counts");
                if(f%16==0)Console.WriteLine($"frame={f} mode={(opt?"optimized":"baseline")} main={AuditGpuTimer.Get(f,false):F3} shadow={AuditGpuTimer.Get(f,true):F3} alloc={GC.GetAllocatedBytesForCurrentThread()-alloc}");
            }
            AuditGpuTimer.Collect();
            using var csv=new StreamWriter(Path.Combine(root,$"tmp/mannequin-ab/ab-paired-{population}.csv")); csv.WriteLine("frame,mode,count,main_gpu_ms,shadow_gpu_ms,total_ms,alloc_bytes,instances,draws,poses,shared_instances");
            var bm=new List<double>(); var om=new List<double>(); var bs=new List<double>(); var os=new List<double>(); var bt=new List<double>(); var ot=new List<double>();
            for(int f=0;f<Frames;f++){ bool opt=(f%2)==1; double main=AuditGpuTimer.Get(f,false), sh=AuditGpuTimer.Get(f,true); csv.WriteLine(FormattableString.Invariant($"{f},{(opt?"optimized":"baseline")},{population},{main},{sh},{totalMs[f]},{allocBytes[f]},{instances[f]},{draws[f]},{poses[f]},{sharedInstances[f]}")); if(f>=Warmup){(opt?om:bm).Add(main);(opt?os:bs).Add(sh);(opt?ot:bt).Add(totalMs[f]);} }
            double Avg(List<double> q)=>q.Average();
            Console.WriteLine(FormattableString.Invariant($"AB warmup={Warmup} baseline_main={Avg(bm):F4} optimized_main={Avg(om):F4} baseline_shadow={Avg(bs):F4} optimized_shadow={Avg(os):F4} baseline_total={Avg(bt):F4} optimized_total={Avg(ot):F4}"));
        } finally { Rl.CloseWindow(); }
        return 0;
    }
    sealed class Assets(string p):IRenderMeshAssets,IRenderAssetPathResolver{ public MeshAssetDescriptor Descriptor{get;}=new(){Id=1,Type=MeshAssetType.Model,SourceUris=[p]}; public bool TryResolveFullPath(string uri,out string full){full=p;return true;} public bool TryGetDescriptor(int id,out MeshAssetDescriptor d){d=Descriptor;return id==1;} public bool TryGetPrimitiveKind(int id,out PrimitiveMeshKind k){k=default;return false;} public int GetId(string k)=>1; public string GetName(int id)=>"mannequin"; }
}
