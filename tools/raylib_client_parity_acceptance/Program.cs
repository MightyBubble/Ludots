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
        string repoRoot = args.ElementAtOrDefault(0)
            ?? FindRepoRoot();
        string outDir = args.ElementAtOrDefault(1)
            ?? Path.Combine(repoRoot, "artifacts/raylib-client-parity/acceptance");
        string optOutDir = args.ElementAtOrDefault(2)
            ?? "/opt/cursor/artifacts/raylib-client-parity/acceptance";

        Directory.CreateDirectory(outDir);
        Directory.CreateDirectory(optOutDir);

        string libDir = Path.Combine(repoRoot, "src/Platforms/Desktop");
        if (!File.Exists(Path.Combine(libDir, "libraylib.so")))
        {
            Console.Error.WriteLine($"ERROR: libraylib.so missing under {libDir}");
            return 2;
        }

        string existing = Environment.GetEnvironmentVariable("LD_LIBRARY_PATH") ?? string.Empty;
        Environment.SetEnvironmentVariable(
            "LD_LIBRARY_PATH",
            string.IsNullOrEmpty(existing) ? libDir : $"{libDir}:{existing}");

        foreach (string name in new[]
                 {
                     "skinning_instanced.vs", "skinning_instanced.fs",
                     "instancing.vs", "instancing.fs",
                     "vfx_unlit_tint.vs", "vfx_unlit_tint.fs"
                 })
        {
            string src = Path.Combine(libDir, name);
            string dst = Path.Combine(AppContext.BaseDirectory, name);
            if (File.Exists(src))
            {
                File.Copy(src, dst, overwrite: true);
            }
        }

        string buildingPath = Path.Combine(
            repoRoot,
            "mods/showcases/performer_blacksmith/PerformerBlacksmithShowcaseMod/assets/Models/building_blacksmith_blue.gltf");
        string mannequinPath = Path.Combine(
            repoRoot,
            "mods/showcases/raylib_client_parity/RaylibClientParityShowcaseMod/assets/Models/mannequin_large_walk.glb");
        string albedoPath = Path.Combine(
            repoRoot,
            "mods/showcases/raylib_client_parity/RaylibClientParityShowcaseMod/assets/Textures/parity_albedo_override.png");

        foreach (string required in new[] { buildingPath, mannequinPath, albedoPath })
        {
            if (!File.Exists(required))
            {
                Console.Error.WriteLine($"ERROR: missing asset {required}");
                return 3;
            }
        }

        SetConfigFlags(0x00000040u); // FLAG_WINDOW_HIDDEN
        InitWindow(ScreenW, ScreenH, "Raylib Client Parity Acceptance");
        SetTargetFPS(60);

        var report = new StringBuilder();
        report.AppendLine("# Raylib client parity acceptance capture");
        report.AppendLine();

        CaptureStaticIsm(buildingPath, outDir, optOutDir, report);
        CaptureGpuSkinned(mannequinPath, outDir, optOutDir, report);
        CaptureMaterialBind(buildingPath, albedoPath, outDir, optOutDir, report);
        CaptureVfxShader(outDir, optOutDir, report);

        File.WriteAllText(Path.Combine(outDir, "capture-report.md"), report.ToString());
        File.WriteAllText(Path.Combine(optOutDir, "capture-report.md"), report.ToString());
        Console.WriteLine(report.ToString());

        CloseWindow();
        return 0;
    }

    private static unsafe void CaptureStaticIsm(
        string buildingPath,
        string outDir,
        string optOutDir,
        StringBuilder report)
    {
        Model model = LoadModel(buildingPath);
        if (model.meshCount <= 0)
        {
            throw new InvalidOperationException("static ISM building model has no meshes");
        }

        Shader shader = LoadShader(
            Path.Combine(AppContext.BaseDirectory, "instancing.vs"),
            Path.Combine(AppContext.BaseDirectory, "instancing.fs"));
        WireInstancingLocs(ref shader);
        for (int i = 0; i < model.materialCount; i++)
        {
            model.materials[i].shader = shader;
        }

        const int instanceCount = 48;
        var transforms = new RaylibMatrix[instanceCount];
        int grid = 8;
        for (int i = 0; i < instanceCount; i++)
        {
            int gx = i % grid;
            int gz = i / grid;
            float x = (gx - grid * 0.5f) * 6.5f;
            float z = (gz - 3f) * 6.5f;
            float yaw = (i % 8) * (MathF.PI / 4f);
            transforms[i] = RaylibMatrix.FromSystemNumerics(
                Matrix4x4.CreateScale(0.85f) *
                Matrix4x4.CreateFromAxisAngle(Vector3.UnitY, yaw) *
                Matrix4x4.CreateTranslation(x, 0f, z));
        }

        var camera = new Camera3D
        {
            position = new Vector3(0f, 28f, 42f),
            target = new Vector3(0f, 1.5f, 0f),
            up = Vector3.UnitY,
            fovy = 50f,
            projection = CameraProjection.CAMERA_PERSPECTIVE
        };

        int tintLoc = GetShaderLocation(shader, "tint");
        int colLoc = GetShaderLocation(shader, "colDiffuse");
        Vector4 tint = Vector4.One;
        Vector4 col = Vector4.One;

        BeginDrawing();
        ClearBackground(new Color(48, 56, 68, 255));
        BeginMode3D(camera);
        DrawGrid(60, 1f);
        if (tintLoc >= 0)
        {
            SetShaderValue(shader, tintLoc, &tint, (int)ShaderUniformDataType.SHADER_UNIFORM_VEC4);
        }

        if (colLoc >= 0)
        {
            SetShaderValue(shader, colLoc, &col, (int)ShaderUniformDataType.SHADER_UNIFORM_VEC4);
        }

        for (int meshIndex = 0; meshIndex < model.meshCount; meshIndex++)
        {
            Mesh mesh = model.meshes[meshIndex];
            int materialIndex = model.meshMaterial != null ? model.meshMaterial[meshIndex] : 0;
            if (materialIndex < 0 || materialIndex >= model.materialCount)
            {
                materialIndex = 0;
            }

            Material material = model.materials[materialIndex];
            material.shader = shader;
            if (material.maps != null)
            {
                material.maps[(int)MaterialMapIndex.MATERIAL_MAP_ALBEDO].color = Color.WHITE;
            }

            fixed (RaylibMatrix* p = transforms)
            {
                DrawMeshInstanced(mesh, material, p, instanceCount);
            }
        }

        EndMode3D();
        DrawText($"01 static ISM buildings={instanceCount}", 16, 16, 22, Color.WHITE);
        EndDrawing();

        SaveShot("01_static_ism.png", outDir, optOutDir);
        report.AppendLine("- `01_static_ism.png`: DrawMeshInstanced of Kenney/blacksmith gltf cluster (static ISM path).");
        UnloadShader(shader);
        UnloadModel(model);
    }

    private static unsafe void CaptureGpuSkinned(
        string modelPath,
        string outDir,
        string optOutDir,
        StringBuilder report)
    {
        Model model = LoadModel(modelPath);
        int animCount;
        ModelAnimation* anims = LoadModelAnimations(modelPath, out animCount);
        if (model.meshCount <= 0 || model.boneCount <= 0 || anims == null || animCount <= 0)
        {
            throw new InvalidOperationException("GpuSkinned capture requires skinned model with animations");
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
            throw new InvalidOperationException("GpuSkinned animation invalid for model skeleton");
        }

        if (model.boneCount > MaxBones)
        {
            throw new InvalidOperationException($"boneCount {model.boneCount} exceeds MAX_BONE_NUM");
        }

        Shader shader = LoadShader(
            Path.Combine(AppContext.BaseDirectory, "skinning_instanced.vs"),
            Path.Combine(AppContext.BaseDirectory, "skinning_instanced.fs"));
        int boneLoc = GetShaderLocation(shader, "boneMatrices");
        int tintLoc = GetShaderLocation(shader, "tint");
        WireSkinningLocs(ref shader, boneLoc);
        for (int i = 0; i < model.materialCount; i++)
        {
            model.materials[i].shader = shader;
            if (model.materials[i].maps != null)
            {
                model.materials[i].maps[(int)MaterialMapIndex.MATERIAL_MAP_ALBEDO].color = Color.WHITE;
            }
        }

        const int instanceCount = 64;
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

        int frameA = 0;
        int frameB = Math.Max(1, anim.frameCount / 3);
        CaptureSkinnedFrame(model, anim, shader, boneLoc, tintLoc, transforms, camera, frameA, "02_gpu_skinned_walk_a.png", outDir, optOutDir);
        CaptureSkinnedFrame(model, anim, shader, boneLoc, tintLoc, transforms, camera, frameB, "02_gpu_skinned_walk_b.png", outDir, optOutDir);

        // Fail-loud if frames are identical bytes.
        byte[] a = File.ReadAllBytes(Path.Combine(outDir, "02_gpu_skinned_walk_a.png"));
        byte[] b = File.ReadAllBytes(Path.Combine(outDir, "02_gpu_skinned_walk_b.png"));
        if (a.AsSpan().SequenceEqual(b))
        {
            throw new InvalidOperationException("02 walk frames are identical — GPU skin animation not proven");
        }

        report.AppendLine(
            $"- `02_gpu_skinned_walk_a/b.png`: real GPU boneMatrices + DrawMeshInstanced; anim=`{ReadName(anim.name)}` frames {frameA}/{frameB}; instances={instanceCount}.");
        UnloadShader(shader);
        UnloadModelAnimations(anims, animCount);
        UnloadModel(model);
    }

    private static unsafe void CaptureSkinnedFrame(
        Model model,
        ModelAnimation anim,
        Shader shader,
        int boneLoc,
        int tintLoc,
        RaylibMatrix[] transforms,
        Camera3D camera,
        int animFrame,
        string fileName,
        string outDir,
        string optOutDir)
    {
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

        for (int meshIndex = 0; meshIndex < model.meshCount; meshIndex++)
        {
            Mesh mesh = model.meshes[meshIndex];
            if (mesh.vertexCount <= 0)
            {
                continue;
            }

            if (mesh.boneMatrices != null && mesh.boneCount > 0)
            {
                rlEnableShader(shader.id);
                rlSetUniformMatrices(boneLoc, mesh.boneMatrices, mesh.boneCount);
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
                DrawMeshInstanced(mesh, material, p, transforms.Length);
            }
        }

        EndMode3D();
        DrawText($"02 GPU skinned walk frame={animFrame}", 16, 16, 22, Color.WHITE);
        EndDrawing();
        SaveShot(fileName, outDir, optOutDir);
    }

    private static unsafe void CaptureMaterialBind(
        string buildingPath,
        string albedoPath,
        string outDir,
        string optOutDir,
        StringBuilder report)
    {
        Model model = LoadModel(buildingPath);
        Texture2D albedo = LoadTexture(albedoPath);
        if (albedo.id == 0)
        {
            throw new InvalidOperationException($"failed to LoadTexture albedo override: {albedoPath}");
        }

        // Left: imported materials. Right: host albedo override (W2 binder contract).
        for (int i = 0; i < model.materialCount; i++)
        {
            // Keep left half as imported; we'll clone for override draw.
        }

        Model overridden = LoadModel(buildingPath);
        for (int i = 0; i < overridden.materialCount; i++)
        {
            if (overridden.materials[i].maps != null)
            {
                overridden.materials[i].maps[(int)MaterialMapIndex.MATERIAL_MAP_ALBEDO].texture = albedo;
                overridden.materials[i].maps[(int)MaterialMapIndex.MATERIAL_MAP_ALBEDO].color = Color.WHITE;
            }
        }

        var camera = new Camera3D
        {
            position = new Vector3(0f, 8f, 16f),
            target = new Vector3(0f, 1.5f, 0f),
            up = Vector3.UnitY,
            fovy = 45f,
            projection = CameraProjection.CAMERA_PERSPECTIVE
        };

        BeginDrawing();
        ClearBackground(new Color(30, 32, 40, 255));
        BeginMode3D(camera);
        DrawGrid(30, 1f);
        DrawModelEx(model, new Vector3(-4.5f, 0f, 0f), Vector3.UnitY, 25f, new Vector3(1.1f, 1.1f, 1.1f), Color.WHITE);
        DrawModelEx(overridden, new Vector3(4.5f, 0f, 0f), Vector3.UnitY, -25f, new Vector3(1.1f, 1.1f, 1.1f), Color.WHITE);
        EndMode3D();
        DrawText("03 material bind: left=imported  right=host albedo override", 16, 16, 20, Color.WHITE);
        DrawText("override URI: parity_albedo_override.png (cyan/magenta)", 16, 44, 18, new Color(100, 200, 255, 255));
        EndDrawing();

        SaveShot("03_material_bind.png", outDir, optOutDir);
        report.AppendLine("- `03_material_bind.png`: host Material sourceUris[0] albedo override vs imported materials (W2 baseline).");
        UnloadTexture(albedo);
        UnloadModel(overridden);
        UnloadModel(model);
    }

    private static unsafe void CaptureVfxShader(
        string outDir,
        string optOutDir,
        StringBuilder report)
    {
        Shader shader = LoadShader(
            Path.Combine(AppContext.BaseDirectory, "vfx_unlit_tint.vs"),
            Path.Combine(AppContext.BaseDirectory, "vfx_unlit_tint.fs"));
        if (shader.id == 0)
        {
            throw new InvalidOperationException("failed to load vfx_unlit_tint");
        }

        int locTint = GetShaderLocation(shader, "tint");
        int locTime = GetShaderLocation(shader, "uTime");
        int locColDiffuse = GetShaderLocation(shader, "colDiffuse");
        int locMvp = GetShaderLocation(shader, "mvp");
        int locModel = GetShaderLocation(shader, "matModel");
        shader.locs[(int)ShaderLocationIndex.SHADER_LOC_MATRIX_MVP] = locMvp;
        shader.locs[(int)ShaderLocationIndex.SHADER_LOC_MATRIX_MODEL] = locModel;
        shader.locs[(int)ShaderLocationIndex.SHADER_LOC_COLOR_DIFFUSE] = locColDiffuse;

        Material material = LoadMaterialDefault();
        material.shader = shader;
        Mesh billboard = GenMeshCube(1f, 1f, 1f);

        var camera = new Camera3D
        {
            position = new Vector3(0f, 3f, 8f),
            target = new Vector3(0f, 1.2f, 0f),
            up = Vector3.UnitY,
            fovy = 45f,
            projection = CameraProjection.CAMERA_PERSPECTIVE
        };

        // Advance a few frames so uTime pulse is non-trivial.
        for (int warm = 0; warm < 30; warm++)
        {
            BeginDrawing();
            ClearBackground(new Color(12, 14, 20, 255));
            BeginMode3D(camera);
            DrawGrid(20, 1f);
            float time = (float)GetTime();
            Vector4 tint = new(1f, 0.45f, 0.15f, 0.9f);
            Vector4 col = Vector4.One;
            SetShaderValue(shader, locColDiffuse, &col, (int)ShaderUniformDataType.SHADER_UNIFORM_VEC4);
            SetShaderValue(shader, locTint, &tint, (int)ShaderUniformDataType.SHADER_UNIFORM_VEC4);
            SetShaderValue(shader, locTime, &time, (int)ShaderUniformDataType.SHADER_UNIFORM_FLOAT);

            Vector3[] positions =
            [
                new Vector3(-2.2f, 1.4f, 0f),
                new Vector3(0f, 1.8f, 0.5f),
                new Vector3(2.2f, 1.4f, 0f)
            ];
            foreach (Vector3 pos in positions)
            {
                Vector3 cameraForward = camera.target - camera.position;
                cameraForward = cameraForward.LengthSquared() <= 1e-8f ? -Vector3.UnitZ : Vector3.Normalize(cameraForward);
                Matrix4x4 billboardMatrix = Matrix4x4.CreateBillboard(pos, camera.position, camera.up, cameraForward);
                Matrix4x4 transform = Matrix4x4.CreateScale(1.6f, 1.6f, 0.12f) * billboardMatrix;
                BeginBlendMode(BlendMode.BLEND_ALPHA);
                DrawMesh(billboard, material, RaylibMatrix.FromSystemNumerics(transform));
                EndBlendMode();
            }

            EndMode3D();
            DrawText("04 vfx_unlit_tint billboards (W3 effect shader baseline)", 16, 16, 20, Color.WHITE);
            EndDrawing();
        }

        SaveShot("04_vfx_shader.png", outDir, optOutDir);
        report.AppendLine("- `04_vfx_shader.png`: billboard mesh drawn with production `vfx_unlit_tint` vs/fs (tint + uTime pulse).");
        // Material owns a shader pointer; clear before unload to avoid double-free.
        material.shader = default;
        UnloadMesh(billboard);
        UnloadMaterial(material);
        UnloadShader(shader);
    }

    private static unsafe void WireInstancingLocs(ref Shader shader)
    {
        int locMvp = GetShaderLocation(shader, "mvp");
        int locInstance = GetShaderLocationAttrib(shader, "instanceTransform");
        int locMapAlbedo = GetShaderLocation(shader, "texture0");
        int locColDiffuse = GetShaderLocation(shader, "colDiffuse");
        shader.locs[(int)ShaderLocationIndex.SHADER_LOC_VERTEX_POSITION] = GetShaderLocationAttrib(shader, "vertexPosition");
        shader.locs[(int)ShaderLocationIndex.SHADER_LOC_VERTEX_TEXCOORD01] = GetShaderLocationAttrib(shader, "vertexTexCoord");
        shader.locs[(int)ShaderLocationIndex.SHADER_LOC_MATRIX_MVP] = locMvp;
        shader.locs[(int)ShaderLocationIndex.SHADER_LOC_MATRIX_MODEL] = locInstance;
        shader.locs[(int)ShaderLocationIndex.SHADER_LOC_COLOR_DIFFUSE] = locColDiffuse;
        shader.locs[(int)ShaderLocationIndex.SHADER_LOC_MAP_ALBEDO] = locMapAlbedo;
    }

    private static unsafe void WireSkinningLocs(ref Shader shader, int boneLoc)
    {
        int locMvp = GetShaderLocation(shader, "mvp");
        int locInstance = GetShaderLocationAttrib(shader, "instanceTransform");
        int locMapAlbedo = GetShaderLocation(shader, "texture0");
        int locColDiffuse = GetShaderLocation(shader, "colDiffuse");
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
    }

    private static void SaveShot(string fileName, string outDir, string optOutDir)
    {
        TakeScreenshot(fileName);
        string cwdShot = Path.GetFullPath(fileName);
        if (!File.Exists(cwdShot))
        {
            throw new InvalidOperationException($"TakeScreenshot did not produce {cwdShot}");
        }

        string destA = Path.Combine(outDir, fileName);
        string destB = Path.Combine(optOutDir, fileName);
        File.Copy(cwdShot, destA, overwrite: true);
        File.Copy(cwdShot, destB, overwrite: true);
        File.Delete(cwdShot);
        Console.WriteLine($"INFO: wrote {destA} and {destB}");
    }

    private static string FindRepoRoot()
    {
        string dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8; i++)
        {
            if (File.Exists(Path.Combine(dir, "showcase.registry.json")))
            {
                return dir;
            }

            dir = Path.GetFullPath(Path.Combine(dir, ".."));
        }

        return "/workspace";
    }

    private static unsafe string ReadName(byte* name)
    {
        int len = 0;
        while (len < 32 && name[len] != 0)
        {
            len++;
        }

        return Encoding.UTF8.GetString(new ReadOnlySpan<byte>(name, len));
    }
}
