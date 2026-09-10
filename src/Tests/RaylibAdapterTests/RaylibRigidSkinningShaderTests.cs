using System.Numerics;
using System.Runtime.InteropServices;
using Ludots.Raylib.Render;
using NUnit.Framework;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.Tests.RaylibAdapter;

[TestFixture]
[NonParallelizable]
[Category("NativeGraphics")]
public sealed unsafe class RaylibRigidSkinningShaderTests
{
    private const int Joint = 14;
    private const int BoneBase = RaylibPoseTexturePalette.BoneSlotsPerRow;
    private const int InstanceBase = RaylibPoseTexturePalette.InstancesPerRow - 1;
    private const string FragmentShader = """
        #version 330
        out vec4 finalColor;
        void main() { finalColor = vec4(1.0); }
        """;

    [Test]
    public void RigidAndStaticBindings_PreserveGeometryAcrossMainShadowAndPalettePages()
    {
        Rl.SetConfigFlags(0x80);
        Rl.InitWindow(192, 192, "Rigid skinning shader regression");
        try
        {
            using var probe = new ShaderProbe();
            byte[] mainRigid = probe.Render(probe.MainShader, Joint);
            byte[] mainWeighted = probe.Render(probe.MainShader, -1);
            byte[] shadowRigid = probe.Render(probe.ShadowShader, Joint);
            byte[] shadowWeighted = probe.Render(probe.ShadowShader, -1);
            Assert.That(CountLitPixels(mainRigid), Is.GreaterThan(100), "The geometry must be visible.");
            AssertPixelsEqual(mainRigid, mainWeighted, "Main rigid binding must match an explicit full joint weight.");
            AssertPixelsEqual(shadowRigid, shadowWeighted, "Shadow rigid binding must match an explicit full joint weight.");
            AssertPixelsEqual(mainRigid, shadowRigid, "Main and shadow must place the same geometry.");

            Assert.That(mainRigid.AsSpan().SequenceEqual(probe.Render(probe.MainShader, Joint, boneBase: 0)),
                Is.False, "The bone page offset must affect the selected transform.");
            Assert.That(mainRigid.AsSpan().SequenceEqual(probe.Render(probe.MainShader, Joint, instanceBase: 0)),
                Is.False, "The instance table offset must select the authored poses.");

            byte[] mainStatic = probe.Render(probe.MainShader, -2);
            byte[] shadowStatic = probe.Render(probe.ShadowShader, -2);
            Assert.That(CountLitPixels(mainStatic), Is.GreaterThan(100));
            Assert.That(mainStatic.AsSpan().SequenceEqual(mainRigid), Is.False,
                "The nonidentity bone transforms must distinguish static from rigid geometry.");
            AssertPixelsEqual(mainStatic, shadowStatic, "Static geometry must agree across both passes.");

            probe.ReplacePoses();
            byte[] movedRigid = probe.Render(probe.MainShader, Joint);
            Assert.That(mainRigid.AsSpan().SequenceEqual(movedRigid), Is.False,
                "Replacing the palette must move rigid geometry.");
            AssertPixelsEqual(mainStatic, probe.Render(probe.MainShader, -2), "Static main geometry must ignore the palette.");
            AssertPixelsEqual(shadowStatic, probe.Render(probe.ShadowShader, -2), "Static shadow geometry must ignore the palette.");
        }
        finally
        {
            Rl.CloseWindow();
        }
    }

    private static int CountLitPixels(byte[] pixels)
    {
        int count = 0;
        for (int i = 0; i < pixels.Length; i += 4)
            if (pixels[i] != 0) count++;
        return count;
    }

    private static void AssertPixelsEqual(byte[] expected, byte[] actual, string message)
    {
        Assert.That(actual.Length, Is.EqualTo(expected.Length), message);
        int differences = 0;
        for (int i = 0; i < expected.Length; i += 4)
            if (!expected.AsSpan(i, 4).SequenceEqual(actual.AsSpan(i, 4))) differences++;
        Assert.That(differences, Is.Zero, message);
    }

    private sealed class ShaderProbe : IDisposable
    {
        private Mesh _mesh;
        private Material _material;
        private Shader _defaultShader;
        private RaylibPoseTexturePalette? _palette;
        private readonly RaylibMatrix[] _instances =
        [
            RaylibMatrix.FromSystemNumerics(Matrix4x4.CreateScale(0.65f, 0.8f, 0.7f) *
                Matrix4x4.CreateRotationZ(0.22f) * Matrix4x4.CreateTranslation(-0.8f, -0.1f, 0f)),
            RaylibMatrix.FromSystemNumerics(Matrix4x4.CreateScale(0.85f, 0.6f, 0.75f) *
                Matrix4x4.CreateRotationZ(-0.18f) * Matrix4x4.CreateTranslation(0.85f, 0.12f, 0f)),
        ];

        public Shader MainShader { get; private set; }
        public Shader ShadowShader { get; private set; }

        public ShaderProbe()
        {
            try
            {
                _mesh = RaylibNativeResources.GenMeshCube(0.8f, 0.65f, 0.4f);
                BindJointWeights(ref _mesh);
                _material = RaylibNativeResources.LoadMaterialDefault();
                _defaultShader = _material.shader;
                MainShader = LoadShader("skinning_instanced_pose_texture.vs");
                ShadowShader = LoadShader("shadow_depth_skinning_pose_texture.vs");
                _palette = new RaylibPoseTexturePalette(initialPoseRows: 16, initialInstances: 1024);
                _palette.EnsureBoneSlotCapacity(BoneBase + Joint + 1);
                for (int pose = 0; pose < 3; pose++)
                {
                    WritePose(pose, 0, Matrix4x4.Identity);
                    WritePose(pose, BoneBase, Matrix4x4.Identity);
                }
                WritePose(0, BoneBase, Matrix4x4.CreateRotationZ(0.38f) * Matrix4x4.CreateTranslation(0.2f, 0.38f, 0f));
                WritePose(1, BoneBase, Matrix4x4.CreateRotationZ(-0.44f) * Matrix4x4.CreateTranslation(-0.15f, -0.3f, 0f));
                _palette.WriteInstance(0, 2, 1, 1, 1, 1);
                _palette.WriteInstance(1, 2, 1, 1, 1, 1);
                _palette.WriteInstance(InstanceBase, 0, 1, 1, 1, 1);
                _palette.WriteInstance(InstanceBase + 1, 1, 1, 1, 1, 1);
                _palette.FlushInstanceRows(0, 2);
                Rl.SetMaterialTexture(ref _material, (int)Rl.MaterialMapIndex.MATERIAL_MAP_OCCLUSION, _palette.BonePalette);
                Rl.SetMaterialTexture(ref _material, (int)Rl.MaterialMapIndex.MATERIAL_MAP_HEIGHT, _palette.InstanceTable);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void ReplacePoses()
        {
            WritePose(0, BoneBase, Matrix4x4.CreateRotationZ(-0.7f) * Matrix4x4.CreateTranslation(-0.2f, -0.45f, 0f));
            WritePose(1, BoneBase, Matrix4x4.CreateRotationZ(0.6f) * Matrix4x4.CreateTranslation(0.25f, 0.4f, 0f));
        }

        private void WritePose(int pose, int boneBase, Matrix4x4 transform)
        {
            RaylibMatrix matrix = RaylibMatrix.FromSystemNumerics(transform);
            _palette!.WriteBoneMatrix(pose, boneBase + Joint, in matrix);
            _palette.FlushPaletteRow(pose);
        }

        public byte[] Render(Shader shader, float rigidBone, int boneBase = BoneBase, int instanceBase = InstanceBase)
        {
            _material.shader = shader;
            SetFloat(shader, "uRigidBoneIndex", rigidBone);
            SetFloat(shader, "uBoneBase", boneBase);
            SetFloat(shader, "uInstanceBase", instanceBase);
            SetFloat(shader, "uPaletteSlotsPerRow", RaylibPoseTexturePalette.BoneSlotsPerRow);
            SetFloat(shader, "uPaletteSlotRows", _palette!.SlotRowsPerPose);
            var camera = new Camera3D
            {
                position = new Vector3(0, 0, 5), target = Vector3.Zero, up = Vector3.UnitY,
                fovy = 4, projection = CameraProjection.CAMERA_ORTHOGRAPHIC,
            };
            Rl.BeginDrawing();
            try
            {
                Rl.ClearBackground(new Color(0, 0, 0, 255));
                Rl.BeginMode3D(camera);
                fixed (RaylibMatrix* instances = _instances)
                    Rl.DrawMeshInstanced(_mesh, _material, instances, _instances.Length);
                Rl.EndMode3D();
                Rl.rlDrawRenderBatchActive();
                byte* pixels = Rl.rlReadScreenPixels(Rl.GetRenderWidth(), Rl.GetRenderHeight());
                if (pixels == null) throw new InvalidOperationException("Framebuffer readback failed.");
                try { return new ReadOnlySpan<byte>(pixels, Rl.GetRenderWidth() * Rl.GetRenderHeight() * 4).ToArray(); }
                finally { Rl.MemFree(pixels); }
            }
            finally
            {
                Rl.EndDrawing();
            }
        }

        public void Dispose()
        {
            if (_material.maps != null)
            {
                _material.shader = _defaultShader;
                _material.maps[(int)Rl.MaterialMapIndex.MATERIAL_MAP_OCCLUSION].texture = default;
                _material.maps[(int)Rl.MaterialMapIndex.MATERIAL_MAP_HEIGHT].texture = default;
                RaylibNativeResources.UnloadMaterial(_material);
                _material = default;
            }
            if (MainShader.id != 0) RaylibNativeResources.UnloadShader(MainShader);
            if (ShadowShader.id != 0) RaylibNativeResources.UnloadShader(ShadowShader);
            if (_mesh.vaoId != 0) RaylibNativeResources.UnloadMesh(_mesh);
            _palette?.Dispose();
        }
    }

    private static Shader LoadShader(string filename)
    {
        string? root = TestContext.CurrentContext.TestDirectory;
        while (root != null && !File.Exists(Path.Combine(root, "src", "Platforms", "Desktop", filename)))
            root = Path.GetDirectoryName(root);
        if (root == null) throw new FileNotFoundException($"Production shader {filename} was not found.");
        Shader shader = RaylibNativeResources.LoadShaderFromMemory(
            File.ReadAllText(Path.Combine(root, "src", "Platforms", "Desktop", filename)), FragmentShader);
        shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MATRIX_MODEL] = Rl.GetShaderLocationAttrib(shader, "instanceTransform");
        shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_BONEIDS] = Rl.GetShaderLocationAttrib(shader, "vertexBoneIds");
        shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_VERTEX_BONEWEIGHTS] = Rl.GetShaderLocationAttrib(shader, "vertexBoneWeights");
        shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_OCCLUSION] = RequireUniform(shader, "uBonePalette");
        shader.locs[(int)Rl.ShaderLocationIndex.SHADER_LOC_MAP_HEIGHT] = RequireUniform(shader, "uInstanceTable");
        return shader;
    }

    private static int RequireUniform(Shader shader, string name)
    {
        int location = Rl.GetShaderLocation(shader, name);
        Assert.That(location, Is.GreaterThanOrEqualTo(0), $"Shader uniform {name} must be active.");
        return location;
    }

    private static void SetFloat(Shader shader, string name, float value) =>
        Rl.SetShaderValue(shader, RequireUniform(shader, name), &value, (int)Rl.ShaderUniformDataType.SHADER_UNIFORM_FLOAT);

    private static void BindJointWeights(ref Mesh mesh)
    {
        mesh.boneIds = (byte*)Rl.MemAlloc(mesh.vertexCount * 4);
        mesh.boneWeights = (float*)Rl.MemAlloc(mesh.vertexCount * 4 * sizeof(float));
        new Span<byte>(mesh.boneIds, mesh.vertexCount * 4).Clear();
        new Span<float>(mesh.boneWeights, mesh.vertexCount * 4).Clear();
        for (int i = 0; i < mesh.vertexCount; i++)
        {
            mesh.boneIds[i * 4] = Joint;
            mesh.boneWeights[i * 4] = 1;
        }
        Assert.That(rlEnableVertexArray(mesh.vaoId), Is.True);
        mesh.vboId[7] = rlLoadVertexBuffer(mesh.boneIds, mesh.vertexCount * 4, false);
        rlSetVertexAttribute(7, 4, 0x1401, false, 0, null);
        rlEnableVertexAttribute(7);
        mesh.vboId[8] = rlLoadVertexBuffer(mesh.boneWeights, mesh.vertexCount * 4 * sizeof(float), false);
        rlSetVertexAttribute(8, 4, 0x1406, false, 0, null);
        rlEnableVertexAttribute(8);
        rlDisableVertexArray();
    }

    [DllImport("raylib", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool rlEnableVertexArray(uint id);
    [DllImport("raylib", CallingConvention = CallingConvention.Cdecl)]
    private static extern void rlDisableVertexArray();
    [DllImport("raylib", CallingConvention = CallingConvention.Cdecl)]
    private static extern uint rlLoadVertexBuffer(void* data, int size, [MarshalAs(UnmanagedType.I1)] bool dynamic);
    [DllImport("raylib", CallingConvention = CallingConvention.Cdecl)]
    private static extern void rlSetVertexAttribute(uint index, int compSize, int type, [MarshalAs(UnmanagedType.I1)] bool normalized, int stride, void* pointer);
    [DllImport("raylib", CallingConvention = CallingConvention.Cdecl)]
    private static extern void rlEnableVertexAttribute(uint index);
}
