using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;
using Ludots.Raylib.Render;
using NUnit.Framework;

namespace Ludots.Tests.RaylibAdapter;

[TestFixture]
public sealed class RaylibGltfMeshSkinBindingsTests
{
    private string _directory = null!;

    [SetUp]
    public void SetUp()
    {
        _directory = Path.Combine(Path.GetTempPath(), nameof(RaylibGltfMeshSkinBindingsTests), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_directory, recursive: true);

    [Test]
    public void Soldier_AttachmentsFollowTheirNamedBones_InNativeMeshOrder()
    {
        string? root = AppContext.BaseDirectory;
        while (root != null && !File.Exists(Path.Combine(root, "showcase.registry.json"))) root = Directory.GetParent(root)?.FullName;
        Assert.That(root, Is.Not.Null, "Repository root was not found.");
        string path = Path.Combine(root!, "mods", "capabilities", "navigation", "MassNavigationMod", "assets", "Models", "mass_navigation_agent_soldier.glb");
        string[] names =
        [
            "root", "hips", "spine", "chest", "upperarm.l", "lowerarm.l", "wrist.l", "hand.l", "handslot.l",
            "upperarm.r", "lowerarm.r", "wrist.r", "hand.r", "handslot.r", "head", "upperleg.l", "lowerleg.l",
            "foot.l", "toes.l", "upperleg.r", "lowerleg.r", "foot.r", "toes.r", "kneeIK.l", "control-toe-roll.l",
            "control-heel-roll.l", "control-foot-roll.l", "heelIK.l", "IK-foot.l", "IK-toe.l", "kneeIK.r",
            "control-toe-roll.r", "control-heel-roll.r", "control-foot-roll.r", "heelIK.r", "IK-foot.r",
            "IK-toe.r", "elbowIK.l", "handIK.l", "elbowIK.r", "handIK.r"
        ];

        int[] bindings = RaylibGltfMeshSkinBindings.Load(path, 15, names);

        Assert.That(bindings, Is.EqualTo(new[] { 8, 8, 8, 8, 8, 13, 13, 14, 3, -1, -1, -1, -1, -1, -1 }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void NestedRigidAttachment_UsesNearestJoint_AndStaticMeshUsesIdentity(bool glb)
    {
        string path = WriteModel(Model(), glb);

        Assert.That(RaylibGltfMeshSkinBindings.Load(path, 3, ["root", "hand"]), Is.EqualTo(new[] { 0, -1, -2 }));
    }

    [Test]
    public void NodeOrder_PrimitiveOrder_AndRepeatedMeshReferencesMatchNativeLoader()
    {
        JsonObject model = Model();
        model["meshes"]![0]!["primitives"]!.AsArray().Add(JsonNode.Parse("""{"attributes":{"POSITION":0}}"""));
        model["nodes"]![5]!["mesh"] = 0;
        model["nodes"]![3]!["children"]!.AsArray().Add(2);
        model["nodes"]![1]!["children"]!.AsArray().Clear();

        Assert.That(RaylibGltfMeshSkinBindings.Load(WriteModel(model), 5, ["root", "hand"]), Is.EqualTo(new[] { 1, 1, -1, -2, -2 }));
    }

    [Test]
    public void StaticModelWithoutSkin_UsesIdentityWithoutReadingExternalBuffers()
    {
        JsonObject model = Model();
        model.Remove("skins");
        model["nodes"]![4]!.AsObject().Remove("skin");
        model["meshes"]![1]!["primitives"]![0]!["attributes"]!.AsObject().Remove("JOINTS_0");
        model["meshes"]![1]!["primitives"]![0]!["attributes"]!.AsObject().Remove("WEIGHTS_0");
        model["buffers"] = JsonNode.Parse("""[{"uri":"does-not-exist.bin","byteLength":65536}]""");

        Assert.That(RaylibGltfMeshSkinBindings.Load(WriteModel(model), 3, []), Is.EqualTo(new[] { -2, -2, -2 }));
    }

    [TestCase(0)]
    [TestCase(3)]
    public void JointAnimation_IsRepresentable(int target)
    {
        JsonObject model = Model();
        AddAnimation(model, target);

        Assert.That(RaylibGltfMeshSkinBindings.Load(WriteModel(model), 3, ["root", "hand"]), Is.EqualTo(new[] { 0, -1, -2 }));
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(5)]
    public void AnimatedNonJointAttachmentOrAncestor_IsRejected(int target)
    {
        JsonObject model = Model();
        AddAnimation(model, target);

        Assert.That(() => RaylibGltfMeshSkinBindings.Load(WriteModel(model), 3, new[] { "root", "hand" }),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("non-joint"));
    }

    [TestCase("missing_weights", "together")]
    [TestCase("missing_joints", "together")]
    [TestCase("missing_skin", "must agree")]
    [TestCase("skin_without_attributes", "must agree")]
    [TestCase("invalid_skin", "outside count")]
    [TestCase("multiple_skins", "one skin")]
    [TestCase("duplicate_joint", "more than once")]
    [TestCase("duplicate_name", "ambiguous name")]
    [TestCase("missing_name", "name")]
    [TestCase("multiple_parents", "multiple parent")]
    [TestCase("cycle", "cycle")]
    [TestCase("invalid_child", "outside count")]
    [TestCase("invalid_mesh", "outside count")]
    [TestCase("non_triangles", "non-triangle")]
    [TestCase("extra_weights", "Unsupported skin attribute")]
    [TestCase("invalid_accessor", "outside count")]
    [TestCase("morph_targets", "morph targets")]
    [TestCase("animation_weights", "Animation path")]
    [TestCase("invalid_animation_node", "outside count")]
    public void MalformedOrUnsupportedBindings_AreRejected(string scenario, string message)
    {
        JsonObject model = Model();
        JsonObject skinAttributes = model["meshes"]![1]!["primitives"]![0]!["attributes"]!.AsObject();
        switch (scenario)
        {
            case "missing_weights": skinAttributes.Remove("WEIGHTS_0"); break;
            case "missing_joints": skinAttributes.Remove("JOINTS_0"); break;
            case "missing_skin": model["nodes"]![4]!.AsObject().Remove("skin"); break;
            case "skin_without_attributes": model["nodes"]![2]!["skin"] = 0; break;
            case "invalid_skin": model["nodes"]![4]!["skin"] = 1; break;
            case "multiple_skins": model["skins"]!.AsArray().Add(model["skins"]![0]!.DeepClone()); break;
            case "duplicate_joint": model["skins"]![0]!["joints"]![1] = 0; break;
            case "duplicate_name": model["nodes"]![3]!["name"] = "root"; break;
            case "missing_name": model["nodes"]![3]!.AsObject().Remove("name"); break;
            case "multiple_parents": model["nodes"]![3]!["children"]!.AsArray().Add(2); break;
            case "cycle": model["nodes"]![2]!["children"] = new JsonArray(0); break;
            case "invalid_child": model["nodes"]![1]!["children"]![0] = 99; break;
            case "invalid_mesh": model["nodes"]![2]!["mesh"] = 99; break;
            case "non_triangles": model["meshes"]![0]!["primitives"]![0]!["mode"] = 1; break;
            case "extra_weights": skinAttributes["WEIGHTS_1"] = 2; break;
            case "invalid_accessor": skinAttributes["WEIGHTS_0"] = 99; break;
            case "morph_targets": model["meshes"]![0]!["primitives"]![0]!["targets"] = new JsonArray(new JsonObject()); break;
            case "animation_weights": AddAnimation(model, 0, "weights"); break;
            case "invalid_animation_node": AddAnimation(model, 99); break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        Assert.That(() => RaylibGltfMeshSkinBindings.Load(WriteModel(model), 3, new[] { "root", "hand" }),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains(message));
    }

    [TestCase(2)]
    [TestCase(4)]
    public void NativeMeshCountMismatch_IsRejected(int count)
    {
        Assert.That(() => RaylibGltfMeshSkinBindings.Load(WriteModel(Model()), count, new[] { "root", "hand" }),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("mesh count"));
    }

    [Test]
    public void NativeBoneOrderMismatch_IsRejected()
    {
        Assert.That(() => RaylibGltfMeshSkinBindings.Load(WriteModel(Model()), 3, new[] { "hand", "root" }),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("does not match native bone"));
    }

    [Test]
    public void NativeBoneCapacityOverflow_IsRejectedBeforeReadingSource()
    {
        Assert.That(() => RaylibGltfMeshSkinBindings.Load("missing.glb", 1, new string[RaylibPoseTexturePalette.MaxBoneCount + 1]),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains("exceeds capacity"));
    }

    [TestCase("magic")]
    [TestCase("version")]
    [TestCase("file_length")]
    [TestCase("chunk_type")]
    [TestCase("chunk_length")]
    [TestCase("duplicate_json")]
    public void MalformedGlb_IsRejected(string scenario)
    {
        string path = WriteModel(Model(), glb: true);
        byte[] bytes = File.ReadAllBytes(path);
        switch (scenario)
        {
            case "magic": bytes[0] = 0; break;
            case "version": bytes[4] = 1; break;
            case "file_length": bytes[8] = 0; break;
            case "chunk_type": bytes[16] = 0; break;
            case "chunk_length": BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), uint.MaxValue); break;
            case "duplicate_json":
                Array.Resize(ref bytes, bytes.Length + 8);
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), (uint)bytes.Length);
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(bytes.Length - 4), 0x4E4F534A);
                break;
            default: throw new ArgumentOutOfRangeException(nameof(scenario));
        }

        File.WriteAllBytes(path, bytes);
        Assert.That(() => RaylibGltfMeshSkinBindings.Load(path, 3, new[] { "root", "hand" }), Throws.TypeOf<InvalidDataException>());
    }

    private string WriteModel(JsonObject model, bool glb = false)
    {
        string path = Path.Combine(_directory, glb ? "model.glb" : "model.gltf");
        if (!glb)
        {
            File.WriteAllText(path, model.ToJsonString());
            return path;
        }

        byte[] json = Encoding.UTF8.GetBytes(model.ToJsonString());
        int paddedLength = (json.Length + 3) & ~3;
        byte[] bytes = new byte[20 + paddedLength];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 0x46546C67);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), (uint)bytes.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), (uint)paddedLength);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), 0x4E4F534A);
        bytes.AsSpan(20).Fill(0x20);
        json.CopyTo(bytes.AsSpan(20));
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static void AddAnimation(JsonObject model, int node, string path = "translation")
    {
        model["animations"] = new JsonArray(new JsonObject
        {
            ["samplers"] = new JsonArray(new JsonObject { ["input"] = 0, ["output"] = 0 }),
            ["channels"] = new JsonArray(new JsonObject
            {
                ["sampler"] = 0,
                ["target"] = new JsonObject { ["node"] = node, ["path"] = path }
            })
        });
    }

    private static JsonObject Model() => JsonNode.Parse("""
        {
          "asset":{"version":"2.0"},
          "nodes":[
            {"name":"root","children":[1]},
            {"name":"attachmentOffset","children":[2],"translation":[1,2,3]},
            {"mesh":0},
            {"name":"hand","children":[4]},
            {"mesh":1,"skin":0},
            {"mesh":2}
          ],
          "skins":[{"joints":[0,3]}],
          "meshes":[
            {"primitives":[{"attributes":{"POSITION":0}}]},
            {"primitives":[{"attributes":{"POSITION":0,"JOINTS_0":1,"WEIGHTS_0":2}}]},
            {"primitives":[{"attributes":{"POSITION":0}}]}
          ],
          "accessors":[
            {"componentType":5126,"type":"VEC3","count":3},
            {"componentType":5121,"type":"VEC4","count":3},
            {"componentType":5126,"type":"VEC4","count":3}
          ]
        }
        """)!.AsObject();
}
