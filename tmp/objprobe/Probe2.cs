using Assimp;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

unsafe static class Probe2
{
    static void Main(string[] args)
    {
        string input = Path.GetFullPath(args[0]);
        string glb = Path.Combine(Path.GetDirectoryName(input)!, Path.GetFileNameWithoutExtension(input) + ".probe.glb");

        var ctx = new AssimpContext();
        Scene scene = ctx.ImportFile(input,
            PostProcessSteps.JoinIdenticalVertices |
            PostProcessSteps.Triangulate |
            PostProcessSteps.GenerateSmoothNormals |
            PostProcessSteps.FlipUVs |
            PostProcessSteps.LimitBoneWeights);
        Console.WriteLine($"[assimp] import ok: meshes={scene.MeshCount} materials={scene.MaterialCount} animations={scene.AnimationCount} bones={(scene.MeshCount > 0 && scene.Meshes[0].HasBones ? scene.Meshes[0].BoneCount : 0)}");
        ctx.ExportFile(scene, glb, "glb2");
        Console.WriteLine($"[assimp] export ok: {glb} size={new FileInfo(glb).Length}");
        ctx.Dispose();

        Rl.InitWindow(200, 200, "probe2");
        Model model = Rl.LoadModel(glb);
        Console.WriteLine($"[raylib] meshCount={model.meshCount} materialCount={model.materialCount} boneCount={model.boneCount}");
        int animCount;
        var anims = Rl.LoadModelAnimations(glb, out animCount);
        Console.WriteLine($"[raylib] animCount={animCount}");
        var bb = Rl.GetModelBoundingBox(model);
        Console.WriteLine($"[raylib] bounds {bb.min} .. {bb.max}");
        Console.WriteLine("[probe2] SURVIVED");
        Rl.CloseWindow();
    }
}
