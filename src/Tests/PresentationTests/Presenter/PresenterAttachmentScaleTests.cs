using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Presentation;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Requests;
using Ludots.Core.Presentation.Systems;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.Presentation;

[TestFixture]
[Explicit("Run isolated Release measurements with LUDOTS_ATTACHMENT_BENCHMARK_OUTPUT set.")]
public sealed class PresenterAttachmentScaleTests
{
    [TestCaseSource(nameof(Cases))]
    public void SyncAndBehavior_Scale(int roots, int children, bool attached, int movingRoots)
    {
        string directory = Environment.GetEnvironmentVariable("LUDOTS_ATTACHMENT_BENCHMARK_OUTPUT")
            ?? throw new InvalidOperationException("LUDOTS_ATTACHMENT_BENCHMARK_OUTPUT is required.");
        using var world = World.Create();
        var definitions = new PresenterDefinitionRegistry();
        var childDefinition = new PresenterDefinition
        {
            ParamDefaults = [
                new ParamDefault { ParamKey = PresenterParamKeyRegistry.Register("test.attachment.0.position"), Lane = ParamLane.Vector, VectorValue = new Vector4(new Vector3(0f, 2f, 0f), 0f) },
                new ParamDefault { ParamKey = PresenterParamKeyRegistry.Register("test.attachment.0.rotation"), Lane = ParamLane.Vector, VectorValue = new Vector4((Quaternion.Identity).X, (Quaternion.Identity).Y, (Quaternion.Identity).Z, (Quaternion.Identity).W) },
                new ParamDefault { ParamKey = PresenterParamKeyRegistry.Register("test.attachment.0.scale"), Lane = ParamLane.Vector, VectorValue = new Vector4(1f, 1f, 1f, 0f) }
            ],
            Key = "scale.child",
            Behaviors = attached ? [Mesh(), new BehaviorSlot
            {
                SlotIndex = 1, Kind = BehaviorKind.Attachment, ActiveByDefault = true,
                Attachment = new AttachmentConfig { Target = AttachmentTarget.Parent, BoneId = 0, UpdatePolicy = AttachmentUpdatePolicy.Continuous, Inherit = AttachmentInheritance.Position | AttachmentInheritance.Rotation, LocalPositionParamKey = PresenterParamKeyRegistry.Register("test.attachment.0.position"), LocalRotationParamKey = PresenterParamKeyRegistry.Register("test.attachment.0.rotation"), LocalScaleParamKey = PresenterParamKeyRegistry.Register("test.attachment.0.scale") },
            }] : [Mesh()],
        };
        int childId = definitions.Register(childDefinition.Key, childDefinition);
        var rootDefinition = new PresenterDefinition
        {
            Key = "scale.root", Behaviors = [Mesh()], Children = new ChildPresenterRef[children],
        };
        for (int i = 0; i < children; i++) rootDefinition.Children[i] = new ChildPresenterRef { DefinitionId = childId };
        int rootId = definitions.Register(rootDefinition.Key, rootDefinition);
        var runtime = new PresenterEntityRuntime(world);
        runtime.BindDefinitions(definitions);
        var owners = new Entity[roots];
        var presenters = new Entity[roots];
        int nextStableId = 0;
        Func<int> allocateStableId = () => ++nextStableId;
        for (int i = 0; i < roots; i++)
        {
            owners[i] = world.Create(
                new VisualTransform { Position = new Vector3(i, 0f, 0f), Rotation = Quaternion.Identity, Scale = Vector3.One },
                new CullState { IsVisible = true, LOD = LODLevel.High });
            presenters[i] = runtime.CreateHierarchy(definitions, rootId, owners[i], i + 1,
                PresentationAnchorKind.Entity, Vector3.Zero, allocateStableId(), Entity.Null, rootDefinition, allocateStableId);
        }
        using var sync = new PresenterEntityTransformSyncSystem(world, runtime, definitions);
        using var behavior = new PresenterBehaviorSystem(world, runtime, definitions,
            new PresentationEventStream(64), new PresentationOwnerChangeBuffer(roots), new SoundRequestBuffer());
        int moving = movingRoots < 0 ? roots : movingRoots;
        const int warmup = 32;
        const int samples = 65;
        var syncTimes = new double[samples];
        var behaviorTimes = new double[samples];
        long allocations = 0;
        for (int frame = 0; frame < warmup + samples; frame++)
        {
            for (int i = 0; i < moving; i++)
                world.Get<VisualTransform>(owners[i]).Position = new Vector3(i, 0f, frame + 1);
            long beforeBytes = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            sync.Update(1f / 60f);
            long middle = Stopwatch.GetTimestamp();
            behavior.Update(1f / 60f);
            long end = Stopwatch.GetTimestamp();
            long bytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
            if (frame >= warmup)
            {
                int index = frame - warmup;
                syncTimes[index] = Stopwatch.GetElapsedTime(start, middle).TotalMilliseconds;
                behaviorTimes[index] = Stopwatch.GetElapsedTime(middle, end).TotalMilliseconds;
                allocations += bytes;
            }
        }
        for (int i = 0; i < moving; i++)
        {
            Vector3 position = world.Get<VisualTransform>(owners[i]).Position;
            Assert.That(world.Get<PresenterWorldPosition>(presenters[i]).Value, Is.EqualTo(position));
            if (attached)
            {
                for (int c = 0; c < children; c++)
                    Assert.That(world.Get<PresenterWorldPosition>(world.Get<PresenterChildren>(presenters[i]).Get(c)).Value,
                        Is.EqualTo(position + new Vector3(0f, 2f, 0f)));
            }
        }
        Array.Sort(syncTimes);
        Array.Sort(behaviorTimes);
        string output = string.Join(",", roots, children, attached, moving,
            syncTimes[samples / 2].ToString("F6", CultureInfo.InvariantCulture),
            syncTimes[(int)Math.Ceiling(samples * 0.95) - 1].ToString("F6", CultureInfo.InvariantCulture),
            behaviorTimes[samples / 2].ToString("F6", CultureInfo.InvariantCulture),
            behaviorTimes[(int)Math.Ceiling(samples * 0.95) - 1].ToString("F6", CultureInfo.InvariantCulture),
            allocations / samples);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, $"roots-{roots}-children-{children}-attached-{attached}-moving-{moving}.csv"),
            "roots,children,attached,moving,sync_median_ms,sync_p95_ms,behavior_median_ms,behavior_p95_ms,allocated_bytes_per_frame\n" + output + "\n");
        TestContext.Out.WriteLine(output);
        Assert.That(allocations, Is.Zero, "Warmed sync and behavior must not allocate.");
        if (!attached)
            Assert.That(runtime.AttachmentDependentVisitCount, Is.Zero);
    }

    [TestCaseSource(nameof(ParameterCases))]
    public void ParameterChanges_VisitOnlyActiveConsumers(int roots, bool once, bool instance, bool relevant)
    {
        string directory = Environment.GetEnvironmentVariable("LUDOTS_ATTACHMENT_BENCHMARK_OUTPUT")
            ?? throw new InvalidOperationException("LUDOTS_ATTACHMENT_BENCHMARK_OUTPUT is required.");
        using var world = World.Create();
        var definitions = new PresenterDefinitionRegistry();
        int positionKey = PresenterParamKeyRegistry.Register("scale.local.position");
        int rotationKey = PresenterParamKeyRegistry.Register("scale.local.rotation");
        int scaleKey = PresenterParamKeyRegistry.Register("scale.local.scale");
        int unrelatedKey = PresenterParamKeyRegistry.Register("scale.unrelated");
        var attachment = new BehaviorSlot
        {
            SlotIndex = 1, Kind = BehaviorKind.Attachment, ActiveByDefault = true,
            Attachment = new AttachmentConfig
            {
                Target = AttachmentTarget.Parent,
                UpdatePolicy = once ? AttachmentUpdatePolicy.Once : AttachmentUpdatePolicy.Continuous,
                Inherit = AttachmentInheritance.Position,
                LocalPositionParamKey = positionKey,
                LocalRotationParamKey = rotationKey,
                LocalScaleParamKey = scaleKey,
            },
        };
        int childId = definitions.Register("parameter.child", new PresenterDefinition
        {
            Behaviors = instance ? [Mesh()] : [Mesh(), attachment],
        });
        int unrelatedId = definitions.Register("parameter.unrelated", new PresenterDefinition());
        var definition = new PresenterDefinition
        {
            ParamDefaults = [
                new ParamDefault { ParamKey = positionKey, Lane = ParamLane.Vector, VectorValue = Vector4.Zero },
                new ParamDefault { ParamKey = rotationKey, Lane = ParamLane.Vector, VectorValue = new Vector4(0, 0, 0, 1) },
                new ParamDefault { ParamKey = scaleKey, Lane = ParamLane.Vector, VectorValue = new Vector4(1, 1, 1, 0) },
            ],
            Children = [
                new ChildPresenterRef { DefinitionId = childId, InstanceOverride = instance ? new PresenterChildInstanceOverride { InstanceBehaviors = [attachment] } : null! },
                new ChildPresenterRef { DefinitionId = unrelatedId },
                new ChildPresenterRef { DefinitionId = unrelatedId },
            ],
        };
        int rootId = definitions.Register("parameter.root", definition);
        var runtime = new PresenterEntityRuntime(world);
        runtime.BindDefinitions(definitions);
        Entity[] presenters = new Entity[roots];
        int stableId = 0;
        for (int i = 0; i < roots; i++)
            presenters[i] = runtime.CreateHierarchy(definitions, rootId, Entity.Null, i + 1,
                PresentationAnchorKind.WorldPosition, new Vector3(i, 0, 0), ++stableId, Entity.Null, definition, () => ++stableId);
        using var behavior = new PresenterBehaviorSystem(world, runtime, definitions,
            new PresentationEventStream(64), new PresentationOwnerChangeBuffer(roots), new SoundRequestBuffer());
        behavior.Update(1f / 60f);
        int key = relevant ? positionKey : unrelatedKey;
        const int samples = 65;
        const int warmup = 32;
        double[] timings = new double[samples];
        long allocated = 0;
        long visits = 0;
        for (int frame = 0; frame < samples + warmup; frame++)
        {
            Vector4 value = new(frame + 1, 2, 3, 0);
            long beforeVisits = runtime.ParamDependencyVisitCount;
            long beforeBytes = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            foreach (Entity presenter in presenters)
                runtime.SetParam(presenter, key, ParamLane.Vector, 0f, 0, value);
            long end = Stopwatch.GetTimestamp();
            long bytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
            if (frame < warmup) continue;
            timings[frame - warmup] = Stopwatch.GetElapsedTime(start, end).TotalMilliseconds;
            allocated += bytes;
            visits += runtime.ParamDependencyVisitCount - beforeVisits;
        }
        Assert.That(allocated, Is.Zero);
        Assert.That(visits, Is.EqualTo(relevant && !once ? (long)roots * samples : 0));
        foreach (Entity presenter in presenters)
        {
            Entity child = world.Get<PresenterChildren>(presenter).Get(0);
            Vector3 expected = relevant && !once ? new Vector3(samples + warmup, 2, 3) : Vector3.Zero;
            expected += world.Get<PresenterWorldPosition>(presenter).Value;
            Assert.That(world.Get<PresenterWorldPosition>(child).Value, Is.EqualTo(expected));
        }
        Array.Sort(timings);
        Directory.CreateDirectory(directory);
        string name = $"params-{roots}-once-{once}-instance-{instance}-relevant-{relevant}.csv";
        string output = FormattableString.Invariant($"{roots},{once},{instance},{relevant},{timings[samples / 2]:F6},{timings[(int)Math.Ceiling(samples * .95) - 1]:F6},{allocated / samples},{visits / samples},{runtime.ParamDependencyCount},{runtime.ParamDependencyStorageBytes}");
        File.WriteAllText(Path.Combine(directory, name),
            "roots,once,instance,relevant,median_ms,p95_ms,allocated_bytes,dependency_visits,dependency_count,dependency_storage_bytes\n" + output + "\n");
        TestContext.Out.WriteLine(output);
    }

    private static IEnumerable<TestCaseData> ParameterCases()
    {
        foreach (int roots in new[] { 1000, 5000, 10000 })
        foreach (bool once in new[] { false, true })
        foreach (bool instance in new[] { false, true })
        foreach (bool relevant in new[] { false, true })
            yield return new TestCaseData(roots, once, instance, relevant);
    }

    private static IEnumerable<TestCaseData> Cases()
    {
        foreach (int roots in new[] { 1000, 5000, 10000 })
        foreach ((int children, bool attached) in new[] { (0, false), (3, false), (3, true) })
        foreach (int moving in new[] { 0, 1, -1 })
            yield return new TestCaseData(roots, children, attached, moving);
    }

    private static BehaviorSlot Mesh() => new()
    {
        SlotIndex = 0, Kind = BehaviorKind.AssetBinding, ActiveByDefault = true,
        AssetBinding = new AssetBindingConfig
        {
            AssetKind = AssetKind.Mesh, AssetId = 1, MaterialId = 1,
            RenderPath = VisualRenderPath.StaticMesh, Mobility = VisualMobility.Movable,
            LocalRotation = Quaternion.Identity, LocalScale = Vector3.One,
        },
    };
}
