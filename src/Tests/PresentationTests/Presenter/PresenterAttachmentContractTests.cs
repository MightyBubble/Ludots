using System.Numerics;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Modding;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.Presentation;

[TestFixture]
public sealed class PresenterAttachmentContractTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "Ludots_AttachmentContract", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_root, "Presentation"));
        File.WriteAllText(Path.Combine(_root, "config_catalog.json"),
            """[{ "Path": "Presentation/presenters.json", "Policy": "ArrayById", "IdField": "id" }]""");
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_root, recursive: true);

    [Test]
    public void ChildrenWithoutAttachment_MovingOwnerDoesNotChangeChildTransform()
    {
        var definitions = Load("""
            [{ "id": "child" }, { "id": "root", "children": [{ "definitionId": "child" }] }]
            """);
        using var world = World.Create();
        var runtime = new PresenterEntityRuntime(world);
        runtime.BindDefinitions(definitions);
        Entity owner = CreateOwner(world);
        Entity root = CreateRoot(world, runtime, definitions, owner);
        Entity child = world.Get<PresenterChildren>(root).Get(0);
        using var behavior = CreateBehavior(world, runtime, definitions);
        using var sync = new PresenterEntityTransformSyncSystem(world, runtime, definitions);
        behavior.Update(1f / 60f);
        Vector3 position = world.Get<PresenterWorldPosition>(child).Value;
        Quaternion rotation = world.Get<PresenterWorldRotation>(child).Value;
        Vector3 scale = world.Get<PresenterWorldScale>(child).Value;

        world.Get<VisualTransform>(owner) = new VisualTransform
        {
            Position = new Vector3(40f, 5f, -10f),
            Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f),
            Scale = new Vector3(2f, 3f, 4f),
        };
        sync.Update(1f / 60f);
        behavior.Update(1f / 60f);
        Assert.Multiple(() =>
        {
            Assert.That(world.Get<PresenterWorldPosition>(child).Value, Is.EqualTo(position));
            Assert.That(world.Get<PresenterWorldRotation>(child).Value, Is.EqualTo(rotation));
            Assert.That(world.Get<PresenterWorldScale>(child).Value, Is.EqualTo(scale));
            Assert.That(world.Get<PresenterParent>(child).Parent, Is.EqualTo(root));
        });
    }

    [TestCaseSource(nameof(ParentCases))]
    public void ParentAttachment_ExplicitPolicyAndInheritanceControlTransform(int mask, bool once, bool instance)
    {
        var definitions = Load(BuildConfig(mask, once, instance).ToJsonString());
        using var world = World.Create();
        var runtime = new PresenterEntityRuntime(world);
        runtime.BindDefinitions(definitions);
        Entity owner = CreateOwner(world);
        Entity root = CreateRoot(world, runtime, definitions, owner);
        Entity child = world.Get<PresenterChildren>(root).Get(0);
        using var behavior = CreateBehavior(world, runtime, definitions);
        using var sync = new PresenterEntityTransformSyncSystem(world, runtime, definitions);
        behavior.Update(1f / 60f);
        Vector3 firstPosition = world.Get<PresenterWorldPosition>(child).Value;
        Assert.That(Vector3.Distance(firstPosition, new Vector3(12f, 3f, 24f)), Is.LessThan(0.0001f));

        world.Get<VisualTransform>(owner) = new VisualTransform
        {
            Position = new Vector3(40f, 5f, -10f),
            Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f),
            Scale = new Vector3(2f, 3f, 4f),
        };
        sync.Update(1f / 60f);
        behavior.Update(1f / 60f);
        Vector3 expectedPosition = new(12f, 3f, 24f);
        Quaternion expectedRotation = Quaternion.Identity;
        Vector3 expectedScale = new(2f, 1f, 0.5f);
        if (!once)
        {
            if ((mask & 1) != 0)
            {
                Vector3 offset = (mask & 4) != 0 ? new Vector3(4f, 9f, 16f) : new Vector3(2f, 3f, 4f);
                if ((mask & 2) != 0) offset = new Vector3(offset.Z, offset.Y, -offset.X);
                expectedPosition = new Vector3(40f, 5f, -10f) + offset;
            }
            if ((mask & 2) != 0) expectedRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2f);
            if ((mask & 4) != 0) expectedScale = new Vector3(4f, 3f, 2f);
        }
        Assert.Multiple(() =>
        {
            Assert.That(Vector3.Distance(world.Get<PresenterWorldPosition>(child).Value, expectedPosition), Is.LessThan(0.0001f));
            Assert.That(MathF.Abs(Quaternion.Dot(world.Get<PresenterWorldRotation>(child).Value, expectedRotation)), Is.EqualTo(1f).Within(0.0001f));
            Assert.That(world.Get<PresenterWorldScale>(child).Value, Is.EqualTo(expectedScale));
        });
    }

    [TestCase("offset")]
    [TestCase("rotationOffset")]
    [TestCase("inheritScale")]
    public void Attachment_RejectsRemovedConstantFields(string field)
    {
        JsonArray config = BuildConfig(7, false, false);
        JsonObject attachment = config[0]!["behaviors"]![0]!["attachment"]!.AsObject();
        attachment[field] = field == "inheritScale" ? JsonValue.Create(true) : JsonNode.Parse("[0,0,0]");
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => Load(config.ToJsonString()))!;
        Assert.That(error.Message, Does.Contain(field));
    }

    [TestCase("updatePolicy")]
    [TestCase("inherit")]
    [TestCase("target")]
    [TestCase("localPositionParamKey")]
    [TestCase("localRotationParamKey")]
    [TestCase("localScaleParamKey")]
    public void Attachment_RejectsMissingRequiredField(string field)
    {
        JsonArray config = BuildConfig(7, false, false);
        config[0]!["behaviors"]![0]!["attachment"]!.AsObject().Remove(field);
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => Load(config.ToJsonString()))!;
        Assert.That(error.Message, Does.Contain(field));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Attachment_DeactivateActivateStartsNewCycleAndRepeatedActivateIsIdempotent(bool once)
    {
        var definitions = Load(BuildConfig(1, once, false).ToJsonString());
        using var world = World.Create();
        var runtime = new PresenterEntityRuntime(world);
        runtime.BindDefinitions(definitions);
        Entity owner = CreateOwner(world);
        Entity root = CreateRoot(world, runtime, definitions, owner);
        Entity child = world.Get<PresenterChildren>(root).Get(0);
        PresenterDefinition childDefinition = definitions.Get(definitions.GetId("child"));
        int slot = childDefinition.Behaviors[0].SlotIndex;
        using var behavior = CreateBehavior(world, runtime, definitions);
        using var sync = new PresenterEntityTransformSyncSystem(world, runtime, definitions);
        behavior.Update(1f / 60f);
        runtime.SetBehaviorActive(child, childDefinition, slot, false);
        world.Get<VisualTransform>(owner).Position = new Vector3(40f, 5f, -10f);
        sync.Update(1f / 60f);
        behavior.Update(1f / 60f);
        Assert.That(world.Get<PresenterWorldPosition>(child).Value, Is.EqualTo(new Vector3(12f, 3f, 24f)));
        Assert.That(world.Get<PresenterWorldScale>(child).Value, Is.EqualTo(new Vector3(2f, 1f, 0.5f)));
        runtime.SetBehaviorActive(child, childDefinition, slot, true);
        behavior.Update(1f / 60f);
        Assert.That(world.Get<PresenterWorldPosition>(child).Value, Is.EqualTo(new Vector3(42f, 8f, -6f)));
        Assert.That(runtime.SetBehaviorActive(child, childDefinition, slot, true), Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Attachment_LocalParameterUpdatesRespectOncePolicy(bool once)
    {
        var definitions = Load(BuildConfig(0, once, true).ToJsonString());
        using var world = World.Create();
        var runtime = new PresenterEntityRuntime(world);
        runtime.BindDefinitions(definitions);
        Entity owner = CreateOwner(world);
        Entity root = CreateRoot(world, runtime, definitions, owner);
        Entity child = world.Get<PresenterChildren>(root).Get(0);
        using var behavior = CreateBehavior(world, runtime, definitions);
        behavior.Update(1f / 60f);
        runtime.SetParam(child, PresenterParamKeyRegistry.Register("contract.position"), ParamLane.Vector,
            0f, 0, new Vector4(5f, 6f, 7f, 0f));
        behavior.Update(1f / 60f);
        Assert.That(world.Get<PresenterWorldPosition>(child).Value,
            Is.EqualTo(once ? new Vector3(12f, 3f, 24f) : new Vector3(15f, 6f, 27f)));
    }

    private static IEnumerable<TestCaseData> ParentCases()
    {
        for (int mask = 0; mask < 8; mask++)
        foreach (bool once in new[] { false, true })
        foreach (bool instance in new[] { false, true })
            yield return new TestCaseData(mask, once, instance);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ParentParameterChange_PreservesChildDefaultsAndOverrides(bool childDefault)
    {
        JsonArray config = BuildConfig(1, false, true);
        JsonNode defaults = config[0]!["paramDefaults"]!.DeepClone();
        config[1]!["paramDefaults"] = defaults;
        if (!childDefault) config[0]!.AsObject().Remove("paramDefaults");
        var definitions = Load(config.ToJsonString());
        using var world = World.Create();
        var runtime = new PresenterEntityRuntime(world);
        runtime.BindDefinitions(definitions);
        Entity root = CreateRoot(world, runtime, definitions, CreateOwner(world));
        Entity child = world.Get<PresenterChildren>(root).Get(0);
        using var behavior = CreateBehavior(world, runtime, definitions);
        behavior.Update(1f / 60f);
        int key = PresenterParamKeyRegistry.Register("contract.position");
        runtime.SetParam(root, key, ParamLane.Vector, 0f, 0, new Vector4(5f, 6f, 7f, 0f));
        behavior.Update(1f / 60f);
        Assert.That(world.Get<PresenterWorldPosition>(child).Value,
            Is.EqualTo(childDefault ? new Vector3(12f, 3f, 24f) : new Vector3(15f, 6f, 27f)));
        Assert.That(world.Get<PresenterVectorParams>(child).TryGet(key, out _), Is.False);
        runtime.SetParam(child, key, ParamLane.Vector, 0f, 0, new Vector4(8f, 9f, 10f, 0f));
        runtime.ClearParam(root, key, ParamLane.Vector);
        behavior.Update(1f / 60f);
        Assert.That(world.Get<PresenterWorldPosition>(child).Value, Is.EqualTo(new Vector3(18f, 9f, 30f)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void InvalidParameterWrite_PreservesOverridesAndAttachedOutput(bool inherited)
    {
        JsonArray config = BuildConfig(7, false, true);
        if (inherited)
        {
            config[1]!["paramDefaults"] = config[0]!["paramDefaults"]!.DeepClone();
            config[0]!.AsObject().Remove("paramDefaults");
        }
        var definitions = Load(config.ToJsonString());
        using var world = World.Create();
        var runtime = new PresenterEntityRuntime(world);
        runtime.BindDefinitions(definitions);
        Entity root = CreateRoot(world, runtime, definitions, CreateOwner(world));
        Entity child = world.Get<PresenterChildren>(root).Get(0);
        using var behavior = CreateBehavior(world, runtime, definitions);
        behavior.Update(1f / 60f);
        Entity source = inherited ? root : child;
        int key = PresenterParamKeyRegistry.Register("contract.rotation");
        Vector3 position = world.Get<PresenterWorldPosition>(child).Value;
        int version = world.Get<PresenterState>(source).Version;

        Assert.Throws<InvalidOperationException>(() => runtime.SetParam(source, key, ParamLane.Vector, 0f, 0, Vector4.Zero));
        Assert.Multiple(() =>
        {
            Assert.That(world.Get<PresenterVectorParams>(source).TryGet(key, out _), Is.False);
            Assert.That(world.Get<PresenterState>(source).Version, Is.EqualTo(version));
            Assert.That(world.Get<PresenterWorldPosition>(child).Value, Is.EqualTo(position));
        });
        runtime.SetParam(source, key, ParamLane.Vector, 0f, 0, new Vector4(0f, 0f, 0f, 1f));
        behavior.Update(1f / 60f);
    }

    [Test]
    public void ClearingLastParameterSource_FailsWithoutChangingActiveAttachment()
    {
        JsonArray config = BuildConfig(1, false, true);
        JsonNode values = config[0]!["paramDefaults"]!.DeepClone();
        config[0]!.AsObject().Remove("paramDefaults");
        config[1]!["children"]![0]!["overrides"] = new JsonObject { ["params"] = values };
        var definitions = Load(config.ToJsonString());
        using var world = World.Create();
        var runtime = new PresenterEntityRuntime(world);
        runtime.BindDefinitions(definitions);
        Entity root = CreateRoot(world, runtime, definitions, CreateOwner(world));
        Entity child = world.Get<PresenterChildren>(root).Get(0);
        using var behavior = CreateBehavior(world, runtime, definitions);
        behavior.Update(1f / 60f);
        int key = PresenterParamKeyRegistry.Register("contract.position");
        Assert.Throws<InvalidOperationException>(() => runtime.ClearParam(child, key, ParamLane.Vector));
        Assert.That(runtime.ResolveVector(child, key, default), Is.EqualTo(new Vector4(2f, 3f, 4f, 0f)));
        Assert.That(world.Get<PresenterWorldPosition>(child).Value, Is.EqualTo(new Vector3(12f, 3f, 24f)));
    }

    [Test]
    public void ParameterNotifications_VisitOnlySubscribedBranchAndReleaseWithTree()
    {
        JsonArray config = BuildConfig(1, false, true);
        config[1]!["paramDefaults"] = config[0]!["paramDefaults"]!.DeepClone();
        config[0]!.AsObject().Remove("paramDefaults");
        JsonArray children = config[1]!["children"]!.AsArray();
        for (int i = 0; i < 20; i++) children.Add(new JsonObject { ["definitionId"] = "unrelated", ["scopeTag"] = $"unrelated.{i}" });
        config.Add(new JsonObject { ["id"] = "unrelated" });
        var definitions = Load(config.ToJsonString());
        using var world = World.Create();
        var runtime = new PresenterEntityRuntime(world);
        runtime.BindDefinitions(definitions);
        Entity root = CreateRoot(world, runtime, definitions, CreateOwner(world));
        Entity child = world.Get<PresenterChildren>(root).Get(0);
        using var behavior = CreateBehavior(world, runtime, definitions);
        behavior.Update(1f / 60f);
        int key = PresenterParamKeyRegistry.Register("contract.position");
        long visits = runtime.ParamDependencyVisitCount;
        runtime.SetParam(root, key, ParamLane.Vector, 0f, 0, new Vector4(5f, 0f, 0f, 0f));
        Assert.That(runtime.ParamDependencyVisitCount - visits, Is.EqualTo(1));
        runtime.SetParam(child, key, ParamLane.Vector, 0f, 0, new Vector4(8f, 0f, 0f, 0f));
        visits = runtime.ParamDependencyVisitCount;
        runtime.SetParam(root, key, ParamLane.Vector, 0f, 0, new Vector4(6f, 0f, 0f, 0f));
        Assert.That(runtime.ParamDependencyVisitCount, Is.EqualTo(visits));
        runtime.ClearParam(child, key, ParamLane.Vector);
        Assert.That(world.Get<PresenterWorldPosition>(child).Value, Is.EqualTo(new Vector3(16f, 0f, 20f)));
        visits = runtime.ParamDependencyVisitCount;
        runtime.SetParam(root, key, ParamLane.Vector, 0f, 0, new Vector4(7f, 0f, 0f, 0f));
        Assert.That(runtime.ParamDependencyVisitCount - visits, Is.EqualTo(1));
        runtime.Destroy(root);
        Assert.That(runtime.ParamDependencyCount, Is.Zero);
    }

    [Test]
    public void ActivatingAttachmentWithMissingParameter_PreservesInactiveState()
    {
        JsonArray config = BuildConfig(1, false, false);
        config[0]!["behaviors"]![0]!["activeByDefault"] = false;
        config[0]!.AsObject().Remove("paramDefaults");
        var definitions = Load(config.ToJsonString());
        using var world = World.Create();
        var runtime = new PresenterEntityRuntime(world);
        runtime.BindDefinitions(definitions);
        Entity root = CreateRoot(world, runtime, definitions, CreateOwner(world));
        Entity child = world.Get<PresenterChildren>(root).Get(0);
        PresenterDefinition definition = definitions.Get(definitions.GetId("child"));
        int slot = definition.Behaviors[0].SlotIndex;
        int version = world.Get<PresenterState>(child).Version;
        Assert.Throws<InvalidOperationException>(() => runtime.SetBehaviorActive(child, definition, slot, true));
        Assert.That(world.Get<PresenterState>(child).BehaviorActiveMask & (1u << slot), Is.Zero);
        Assert.That(world.Get<PresenterState>(child).Version, Is.EqualTo(version));
    }

    [Test]
    public void WrongLaneChildOverride_CannotReadParentVectorInstead()
    {
        JsonArray config = BuildConfig(1, false, true);
        config[1]!["paramDefaults"] = config[0]!["paramDefaults"]!.DeepClone();
        config[0]!.AsObject().Remove("paramDefaults");
        var definitions = Load(config.ToJsonString());
        using var world = World.Create();
        var runtime = new PresenterEntityRuntime(world);
        runtime.BindDefinitions(definitions);
        Entity root = CreateRoot(world, runtime, definitions, CreateOwner(world));
        Entity child = world.Get<PresenterChildren>(root).Get(0);
        using var behavior = CreateBehavior(world, runtime, definitions);
        behavior.Update(1f / 60f);
        int key = PresenterParamKeyRegistry.Register("contract.position");
        var error = Assert.Throws<InvalidOperationException>(() => runtime.SetParam(child, key, ParamLane.Float, 2f, 0, default));
        Assert.That(error!.Message, Does.Contain("ParamLane"));
        Assert.That(world.Get<PresenterFloatParams>(child).TryGet(key, out _), Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ActiveSplineAndAttachment_AreRejectedBeforeCreation(bool instance)
    {
        JsonArray config = BuildConfig(1, false, instance);
        config[0]!["behaviors"] ??= new JsonArray();
        config[0]!["behaviors"]!.AsArray().Add(JsonNode.Parse("""
            { "slot": "spline", "kind": "Spline", "activeByDefault": true,
              "spline": { "splineAssetId": "test.spline", "usage": "Patrol", "progressParamKey": "travel.progress" } }
            """));
        var error = Assert.Throws<InvalidOperationException>(() => Load(config.ToJsonString(),
            (kind, key) => kind == AssetKind.Spline && key == "test.spline" ? 1 : throw new InvalidOperationException("Unknown test asset.")));
        Assert.That(error!.Message, Does.Contain("TransformConflict"));
    }

    private static JsonArray BuildConfig(int mask, bool once, bool instance)
    {
        var inherit = new JsonArray();
        if ((mask & 1) != 0) inherit.Add("Position");
        if ((mask & 2) != 0) inherit.Add("Rotation");
        if ((mask & 4) != 0) inherit.Add("Scale");
        JsonNode behavior = JsonNode.Parse("""
            { "slot": "attachment", "kind": "Attachment", "activeByDefault": true,
              "attachment": { "target": "Parent", "updatePolicy": "Continuous", "inherit": [],
                "localPositionParamKey": "contract.position", "localRotationParamKey": "contract.rotation",
                "localScaleParamKey": "contract.scale" } }
            """)!;
        behavior["attachment"]!["updatePolicy"] = once ? "Once" : "Continuous";
        behavior["attachment"]!["inherit"] = inherit;
        JsonArray config = JsonNode.Parse("""
            [{ "id": "child", "paramDefaults": [
               { "paramKey": "contract.position", "lane": "Vector", "vectorValue": [2,3,4,0] },
               { "paramKey": "contract.rotation", "lane": "Vector", "vectorValue": [0,0,0,1] },
               { "paramKey": "contract.scale", "lane": "Vector", "vectorValue": [2,1,0.5,0] }] },
             { "id": "root", "children": [{ "definitionId": "child" }] }]
            """)!.AsArray();
        if (instance) config[1]!["children"]![0]!["instanceBehaviors"] = new JsonArray(behavior);
        else config[0]!["behaviors"] = new JsonArray(behavior);
        return config;
    }

    private PresenterDefinitionRegistry Load(string json, Func<AssetKind, string, int>? resolveAsset = null)
    {
        File.WriteAllText(Path.Combine(_root, "Presentation", "presenters.json"), json);
        var vfs = new VirtualFileSystem();
        vfs.Mount("Core", _root);
        var pipeline = new ConfigPipeline(vfs, new ModLoader(vfs, new FunctionRegistry(), new TriggerManager()));
        var definitions = new PresenterDefinitionRegistry();
        new PresenterDefinitionConfigLoader(pipeline, definitions, resolveBehaviorAssetId: resolveAsset).Load(ConfigCatalogLoader.Load(pipeline));
        return definitions;
    }

    private static Entity CreateOwner(World world) => world.Create(
        new VisualTransform { Position = new Vector3(10f, 0f, 20f), Rotation = Quaternion.Identity, Scale = Vector3.One },
        new CullState { IsVisible = true, LOD = LODLevel.High });

    private static Entity CreateRoot(World world, PresenterEntityRuntime runtime, PresenterDefinitionRegistry definitions, Entity owner)
    {
        int stableId = 1;
        int rootId = definitions.GetId("root");
        return runtime.CreateHierarchy(definitions, rootId, owner, 1, PresentationAnchorKind.Entity,
            Vector3.Zero, stableId, Entity.Null, definitions.Get(rootId), () => ++stableId);
    }

    private static PresenterBehaviorSystem CreateBehavior(World world, PresenterEntityRuntime runtime, PresenterDefinitionRegistry definitions) =>
        new(world, runtime, definitions, new PresentationEventStream(64), new PresentationOwnerChangeBuffer(16), new SoundRequestBuffer());
}
