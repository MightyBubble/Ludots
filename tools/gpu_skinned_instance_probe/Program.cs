using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Raylib_cs;
using static Raylib_cs.Raylib;

internal static class Program
{
    private const int MaxBones = 128;
    private const int ScreenW = 1280;
    private const int ScreenH = 720;

    public static unsafe int Main(string[] args)
    {
        string modelPath = args.ElementAtOrDefault(0)
            ?? "/tmp/retarget-out/mannequin_large_walk.glb";
        string outDir = args.ElementAtOrDefault(1)
            ?? "/opt/cursor/artifacts/gpu-skinned-instance-probe";
        int instanceCount = args.Length > 2 && int.TryParse(args[2], out int n) ? n : 2000;
        int framesToRun = args.Length > 3 && int.TryParse(args[3], out int f) ? f : 180;

        Directory.CreateDirectory(outDir);
        if (!File.Exists(modelPath))
        {
            Console.Error.WriteLine($"ERROR: model missing: {modelPath}");
            return 2;
        }

        string libDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../src/Platforms/Desktop"));
        if (!File.Exists(Path.Combine(libDir, "libraylib.so")))
        {
            libDir = "/workspace/src/Platforms/Desktop";
        }

        string existing = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH") ?? string.Empty;
        Environment.SetEnvironmentVariable(
            "LD_LIBRARY_PATH",
            string.IsNullOrEmpty(existing) ? libDir : $"{libDir}:{existing}");

        // Copy shaders next to cwd expectations from BaseDirectory.
        foreach (string name in new[] { "skinning_instanced.vs", "skinning_instanced.fs" })
        {
            string src = Path.Combine(libDir, name);
            string dst = Path.Combine(AppContext.BaseDirectory, name);
            if (File.Exists(src))
            {
                File.Copy(src, dst, overwrite: true);
            }
        }

        SetConfigFlags(0x00000040u); // FLAG_WINDOW_HIDDEN
        InitWindow(ScreenW, ScreenH, "GPU Skinned Instance Probe");
        SetTargetFPS(60);

        Model model = LoadModel(modelPath);
        int animCount;
        ModelAnimation* anims = LoadModelAnimations(modelPath, out animCount);
        if (model.meshCount <= 0 || model.boneCount <= 0)
        {
            Console.Error.WriteLine("ERROR: model has no meshes/bones");
            return 3;
        }

        if (anims == null || animCount <= 0)
        {
            Console.Error.WriteLine("ERROR: no animations loaded");
            return 4;
        }

        int animIndex = 0;
        for (int i = 0; i < animCount; i++)
        {
            string name = ReadName(anims[i].name);
            if (name.Contains("Walk", StringComparison.OrdinalIgnoreCase))
            {
                animIndex = i;
                break;
            }
        }

        ModelAnimation anim = anims[animIndex];
        if (!IsModelAnimationValid(model, anim))
        {
            Console.Error.WriteLine(
                $"ERROR: IsModelAnimationValid=false modelBones={model.boneCount} animBones={anim.boneCount} anim={ReadName(anim.name)}");
            return 5;
        }

        if (model.boneCount > MaxBones)
        {
            Console.Error.WriteLine($"ERROR: boneCount {model.boneCount} exceeds MAX_BONE_NUM {MaxBones}");
            return 6;
        }

        Shader shader = LoadShader(
            Path.Combine(AppContext.BaseDirectory, "skinning_instanced.vs"),
            Path.Combine(AppContext.BaseDirectory, "skinning_instanced.fs"));
        int boneLoc = GetShaderLocation(shader, "boneMatrices");
        int tintLoc = GetShaderLocation(shader, "tint");
        int locMvp = GetShaderLocation(shader, "mvp");
        int locInstance = GetShaderLocationAttrib(shader, "instanceTransform");
        int locMapAlbedo = GetShaderLocation(shader, "texture0");
        int locColDiffuse = GetShaderLocation(shader, "colDiffuse");
        if (boneLoc < 0 || locMvp < 0 || locInstance < 0)
        {
            Console.Error.WriteLine($"ERROR: shader locs bone={boneLoc} mvp={locMvp} instance={locInstance}");
            return 7;
        }

        // Match RaylibPrimitiveRenderer instancing loc wiring so DrawMeshInstanced binds instance matrices.
        shader.locs[(int)ShaderLocationIndex.SHADER_LOC_VERTEX_POSITION] = GetShaderLocationAttrib(shader, "vertexPosition");
        shader.locs[(int)ShaderLocationIndex.SHADER_LOC_VERTEX_TEXCOORD01] = GetShaderLocationAttrib(shader, "vertexTexCoord");
        shader.locs[(int)ShaderLocationIndex.SHADER_LOC_VERTEX_COLOR] = GetShaderLocationAttrib(shader, "vertexColor");
        shader.locs[(int)ShaderLocationIndex.SHADER_LOC_VERTEX_BONEIDS] = GetShaderLocationAttrib(shader, "vertexBoneIds");
        shader.locs[(int)ShaderLocationIndex.SHADER_LOC_VERTEX_BONEWEIGHTS] = GetShaderLocationAttrib(shader, "vertexBoneWeights");
        shader.locs[(int)ShaderLocationIndex.SHADER_LOC_MATRIX_MVP] = locMvp;
        shader.locs[(int)ShaderLocationIndex.SHADER_LOC_MATRIX_MODEL] = locInstance;
        shader.locs[(int)ShaderLocationIndex.SHADER_LOC_COLOR_DIFFUSE] = locColDiffuse;
        shader.locs[(int)ShaderLocationIndex.SHADER_LOC_MAP_ALBEDO] = locMapAlbedo;
        shader.locs[(int)ShaderLocationIndex.SHADER_LOC_BONE_MATRICES] = boneLoc;

        Console.WriteLine($"INFO: shader mvpLoc={locMvp} boneLoc={boneLoc} instanceLoc={locInstance} tintLoc={tintLoc}");
        for (int mi = 0; mi < model.meshCount; mi++)
        {
            Mesh mesh = model.meshes[mi];
            Console.WriteLine(
                $"INFO: mesh[{mi}] verts={mesh.vertexCount} boneCount={mesh.boneCount} boneMatrices={(mesh.boneMatrices == null ? "null" : "ok")} boneIds={(mesh.boneIds == null ? "null" : "ok")}");
        }

        for (int i = 0; i < model.materialCount; i++)
        {
            model.materials[i].shader = shader;
        }

        var transforms = new RaylibMatrix[instanceCount];
        int grid = (int)MathF.Ceiling(MathF.Sqrt(instanceCount));
        for (int i = 0; i < instanceCount; i++)
        {
            int gx = i % grid;
            int gz = i / grid;
            float x = (gx - grid * 0.5f) * 2.2f;
            float z = (gz - grid * 0.5f) * 2.2f;
            transforms[i] = RaylibMatrix.FromSystemNumerics(
                Matrix4x4.CreateScale(1.2f) *
                Matrix4x4.CreateTranslation(x, 0f, z));
        }

        var camera = new Camera3D
        {
            position = new Vector3(0f, 8f, 14f),
            target = new Vector3(0f, 1f, 0f),
            up = Vector3.UnitY,
            fovy = 50f,
            projection = CameraProjection.CAMERA_PERSPECTIVE
        };

        // Ensure materials have an opaque diffuse tint even when textures are unusual.
        for (int i = 0; i < model.materialCount; i++)
        {
            if (model.materials[i].maps != null)
            {
                model.materials[i].maps[(int)MaterialMapIndex.MATERIAL_MAP_ALBEDO].color = Color.WHITE;
            }
        }

        var report = new StringBuilder();
        report.AppendLine("# GPU skinned instancing probe");
        report.AppendLine();
        report.AppendLine($"- model: `{modelPath}`");
        report.AppendLine($"- bones: {model.boneCount}");
        report.AppendLine($"- anim: `{ReadName(anim.name)}` frames={anim.frameCount}");
        report.AppendLine($"- instances: {instanceCount}");
        report.AppendLine($"- path: real GPU boneMatrices skinning + DrawMeshInstanced (not VAT)");
        report.AppendLine();

        int frame = 0;
        int animFrame = 0;
        while (!WindowShouldClose() && frame < framesToRun)
        {
            animFrame = (animFrame + 1) % Math.Max(1, anim.frameCount);
            // One shared bone palette for the whole instance bucket (same clip+frame).
            UpdateModelAnimationBones(model, anim, animFrame);

            BeginDrawing();
            ClearBackground(new Color(24, 28, 36, 255));
            BeginMode3D(camera);
            DrawGrid(40, 1f);

            Vector4 tint = new(1f, 1f, 1f, 1f);
            if (tintLoc >= 0)
            {
                SetShaderValue(shader, tintLoc, &tint, (int)ShaderUniformDataType.SHADER_UNIFORM_VEC4);
            }

            int drawnMeshes = 0;
            for (int meshIndex = 0; meshIndex < model.meshCount; meshIndex++)
            {
                Mesh mesh = model.meshes[meshIndex];
                if (mesh.vertexCount <= 0)
                {
                    continue;
                }

                if (mesh.boneMatrices != null && mesh.boneCount > 0)
                {
                    // Same upload path DrawMesh/DrawMeshInstanced use for GPU skinning.
                    rlEnableShader(shader.id);
                    rlSetUniformMatrices(boneLoc, mesh.boneMatrices, mesh.boneCount);

                    if (frame == 1 && meshIndex == 0)
                    {
                        RaylibMatrix b0 = mesh.boneMatrices[0];
                        RaylibMatrix b1 = mesh.boneMatrices[Math.Min(1, mesh.boneCount - 1)];
                        Console.WriteLine(
                            $"INFO: palette f={animFrame} b0.t=({b0.m12:F3},{b0.m13:F3},{b0.m14:F3}) b1.t=({b1.m12:F3},{b1.m13:F3},{b1.m14:F3})");
                    }
                }

                int materialIndex = model.meshMaterial != null ? model.meshMaterial[meshIndex] : 0;
                if (materialIndex < 0 || materialIndex >= model.materialCount)
                {
                    materialIndex = 0;
                }

                Material material = model.materials[materialIndex];
                material.shader = shader;
                fixed (RaylibMatrix* p = transforms)
                {
                    DrawMeshInstanced(mesh, material, p, instanceCount);
                }
                drawnMeshes++;
            }

            if (frame == 0)
            {
                Console.WriteLine($"INFO: drawnMeshes={drawnMeshes}");
            }

            EndMode3D();
            DrawText($"GPU skinned instances={instanceCount} bones={model.boneCount} frame={animFrame}", 16, 16, 20, Color.WHITE);
            DrawFPS(16, 44);
            EndDrawing();

            if (frame == 0 || frame == 60 || frame == 120)
            {
                string shotName = $"gpu_skin_{frame:000}.png";
                TakeScreenshot(shotName);
                string cwdShot = Path.GetFullPath(shotName);
                string dest = Path.Combine(outDir, shotName);
                if (File.Exists(cwdShot))
                {
                    File.Move(cwdShot, dest, overwrite: true);
                }
            }

            frame++;
        }

        report.AppendLine("## Runtime");
        report.AppendLine();
        report.AppendLine($"- renderedFrames: {frame}");
        report.AppendLine("- verdict: GPU bone palette uploaded per clip-frame bucket; instances share palette via DrawMeshInstanced.");
        File.WriteAllText(Path.Combine(outDir, "probe-report.md"), report.ToString());
        Console.WriteLine(report.ToString());

        UnloadShader(shader);
        UnloadModelAnimations(anims, animCount);
        UnloadModel(model);
        CloseWindow();
        return 0;
    }

    private static unsafe string ReadName(byte* name)
    {
        int len = 0;
        while (len < 32 && name[len] != 0)
        {
            len++;
        }

        return Encoding.UTF8.GetString(name, len);
    }
}
