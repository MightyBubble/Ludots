using System.Text.Json;
using Ludots.Raylib.Render;
using NUnit.Framework;

namespace Ludots.Tests.RaylibAdapter;

[TestFixture]
public sealed class RaylibGpuSkinnedSoftwareRenderingContractTests
{
    [Test]
    public void KnightRigidMeshes_FollowTheirHandHeadAndChestJoints()
    {
        string knight = Path.Combine(
            FindRepoRoot(), "mods", "fixtures", "raylib_platform_meshes", "RaylibPlatformMeshesMod", "assets", "Models", "Knight.glb");

        RaylibGltfRigidMeshJoints.MeshSkinSource[] meshes = RaylibGltfRigidMeshJoints.Read(knight);

        Assert.That(meshes.Length, Is.EqualTo(15));
        Assert.That(meshes.Count(mesh => mesh.HasJoints), Is.EqualTo(6));
        AssertRigid(meshes, "1H_Sword_Offhand", 8);
        AssertRigid(meshes, "Round_Shield", 8);
        AssertRigid(meshes, "1H_Sword", 13);
        AssertRigid(meshes, "2H_Sword", 13);
        AssertRigid(meshes, "Knight_Helmet", 14);
        AssertRigid(meshes, "Knight_Cape", 3);
        Assert.That(meshes.Single(mesh => mesh.NodeName == "Knight_Body").HasJoints, Is.True);
    }

    [Test]
    public void RigidMeshWithoutJointAncestor_FailsAtLoad()
    {
        const string gltf = """
        {
          "nodes": [
            { "name": "root", "children": [1, 2] },
            { "name": "hip" },
            { "name": "loose_prop", "mesh": 0 }
          ],
          "skins": [ { "joints": [1] } ],
          "meshes": [ { "primitives": [ { "attributes": { "POSITION": 0 } } ] } ],
          "accessors": [ { "count": 3 } ]
        }
        """;
        using JsonDocument document = JsonDocument.Parse(gltf);

        var ex = Assert.Throws<InvalidDataException>(() => RaylibGltfRigidMeshJoints.Read(document.RootElement, "inline"));
        Assert.That(ex!.Message, Does.Contain("loose_prop"));
        Assert.That(ex.Message, Does.Contain("no joint ancestor"));
    }

    [Test]
    public void MultipleSkins_FailAtLoadBecauseRaylibReadsOnlyTheFirst()
    {
        const string gltf = """
        {
          "nodes": [ { "name": "a" }, { "name": "b" } ],
          "skins": [ { "joints": [0] }, { "joints": [1] } ],
          "meshes": [],
          "accessors": []
        }
        """;
        using JsonDocument document = JsonDocument.Parse(gltf);

        var ex = Assert.Throws<InvalidDataException>(() => RaylibGltfRigidMeshJoints.Read(document.RootElement, "inline"));
        Assert.That(ex!.Message, Does.Contain("2 skins"));
    }

    [Test]
    public void NonTrianglePrimitives_AreSkippedLikeRaylib()
    {
        const string gltf = """
        {
          "nodes": [ { "name": "hip", "children": [1] }, { "name": "gear", "mesh": 0 } ],
          "skins": [ { "joints": [0] } ],
          "meshes": [ { "primitives": [
            { "mode": 1, "attributes": { "POSITION": 0 } },
            { "attributes": { "POSITION": 1 } }
          ] } ],
          "accessors": [ { "count": 2 }, { "count": 6 } ]
        }
        """;
        using JsonDocument document = JsonDocument.Parse(gltf);

        RaylibGltfRigidMeshJoints.MeshSkinSource[] meshes = RaylibGltfRigidMeshJoints.Read(document.RootElement, "inline");

        Assert.That(meshes.Length, Is.EqualTo(1));
        Assert.That(meshes[0].VertexCount, Is.EqualTo(6));
        Assert.That(meshes[0].RigidJointIndex, Is.EqualTo(0));
    }

    [Test]
    public void SkinnedModelLoad_BindsRigidMeshesThroughTheSingleNativeGateway()
    {
        string native = ReadRendering("RaylibNativeResources.cs");
        string cache = ReadRendering("RaylibGpuSkinnedModelCache.cs");

        Assert.That(native, Does.Contain("BindRigidMeshesToJoints(model, RaylibGltfRigidMeshJoints.Read(fileName), fileName)"));
        Assert.That(native, Does.Contain("mesh.vboId[BoneIdsAttribLocation] = RlLoadVertexBufferNative("));
        Assert.That(native, Does.Contain("mesh.vboId[BoneWeightsAttribLocation] = RlLoadVertexBufferNative("));
        Assert.That(cache, Does.Contain("has no bone ids/weights; GPU skinning would read undefined bones"));
    }

    [Test]
    public void DirectSkinnedDraws_RestoreBlendAndOwnVertexColorDefault()
    {
        string batch = ReadRendering("RaylibGpuSkinnedBatchRenderer.cs");
        string shadow = ReadRendering("RaylibDirectionalShadowMap.cs");

        foreach ((string name, string source, string method) in new[]
        {
            ("main", batch, "private unsafe void DrawMeshInstancedSsboDirect("),
            ("shadow", shadow, "public void DrawSkinnedMeshSsboShadow("),
        })
        {
            string body = MethodBody(source, method);
            Assert.That(body, Does.Contain("bool blendWasEnabled = Gl43.DisableBlendForOpaqueDraw();"), name);
            Assert.That(body, Does.Contain("Gl43.RestoreBlend(blendWasEnabled);"), name);
            Assert.That(body, Does.Not.Contain("Gl43.Disable(Gl43.GL_BLEND)"), name);
        }

        Assert.That(MethodBody(batch, "private unsafe void DrawMeshInstancedSsboDirect("),
            Does.Contain("Gl43.VertexAttrib4f((uint)_locVertexColor, 1f, 1f, 1f, 1f);"));
    }

    [Test]
    public void TimestampQueries_UseTheRealAvailabilityEnumAndOnlyReadIssuedQueries()
    {
        Assert.That(Gl43.GL_QUERY_RESULT_AVAILABLE, Is.EqualTo(0x8867));
        string gl = ReadRendering("Gl43.cs");
        Assert.That(gl, Does.Not.Contain("GL_QUERY_RESULT_NO_WAIT"));

        string batch = ReadRendering("RaylibGpuSkinnedBatchRenderer.cs");
        Assert.That(batch, Does.Contain("_mainDrawTimingIssued"));
        Assert.That(batch, Does.Contain("_shadowDrawTimingIssued"));
        Assert.That(ReadRendering("RaylibGpuSkinnedSsboPipeline.cs"), Does.Contain("Gl43.TryReadElapsedNanoseconds("));
    }

    private static void AssertRigid(RaylibGltfRigidMeshJoints.MeshSkinSource[] meshes, string nodeName, int jointIndex)
    {
        RaylibGltfRigidMeshJoints.MeshSkinSource mesh = meshes.Single(candidate => candidate.NodeName == nodeName);
        Assert.That(mesh.HasJoints, Is.False, nodeName);
        Assert.That(mesh.RigidJointIndex, Is.EqualTo(jointIndex), nodeName);
    }

    private static string MethodBody(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0), signature);
        int open = source.IndexOf('{', start);
        int depth = 0;
        for (int i = open; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}' && --depth == 0)
            {
                return source[open..(i + 1)];
            }
        }

        throw new InvalidOperationException($"Unclosed method body for {signature}.");
    }

    private static string ReadRendering(string fileName) =>
        File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Client", "Ludots.Raylib.Render", "Rendering", fileName));

    private static string FindRepoRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current != null)
        {
            if (Directory.Exists(Path.Combine(current.FullName, ".git")) ||
                File.Exists(Path.Combine(current.FullName, ".git")) ||
                File.Exists(Path.Combine(current.FullName, "global.json")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
