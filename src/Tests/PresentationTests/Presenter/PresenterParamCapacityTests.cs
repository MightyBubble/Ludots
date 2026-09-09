using System.Numerics;
using Arch.Core;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Presenters;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.Presentation;

[TestFixture]
public sealed class PresenterParamCapacityTests
{
    [TestCase(ParamLane.Float, false)]
    [TestCase(ParamLane.Int, false)]
    [TestCase(ParamLane.Vector, false)]
    [TestCase(ParamLane.Float, true)]
    [TestCase(ParamLane.Int, true)]
    [TestCase(ParamLane.Vector, true)]
    public void FullBuffer_ExistingKeyCanChange_NewKeyThrowsWithoutMutation(ParamLane lane, bool defaults)
    {
        using var world = World.Create();
        Entity entity = world.Create(new PresenterFloatParams(), new PresenterIntParams(), new PresenterVectorParams(),
            new PresenterFloatDefaults(), new PresenterIntDefaults(), new PresenterVectorDefaults());
        int capacity = Capacity(lane);
        for (int key = 0; key < capacity; key++) Write(world, entity, lane, defaults, key, key);
        Write(world, entity, lane, defaults, capacity - 1, 99);
        for (int attempt = 0; attempt < 2; attempt++)
        {
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => Write(world, entity, lane, defaults, capacity, 123))!;
            Assert.That(error.Message, Does.Contain("PRESENTATION.PRESENTER.ERR.ParamCapacity"));
            Assert.That(error.Message, Does.Contain(lane.ToString()));
        }
        for (int key = 0; key < capacity; key++)
            Assert.That(Read(world, entity, lane, defaults, key), Is.EqualTo(key == capacity - 1 ? 99 : key));
    }

    [TestCase(ParamLane.Float)]
    [TestCase(ParamLane.Int)]
    [TestCase(ParamLane.Vector)]
    public void RuntimeSet_OverflowPreservesVersionAndValues(ParamLane lane)
    {
        using var world = World.Create();
        var definitions = new PresenterDefinitionRegistry();
        int id = definitions.Register("capacity.runtime", new PresenterDefinition());
        var runtime = new PresenterEntityRuntime(world);
        runtime.BindDefinitions(definitions);
        Entity entity = runtime.CreateHierarchy(definitions, id, Entity.Null, 1,
            PresentationAnchorKind.WorldPosition, Vector3.Zero, 1, Entity.Null, definitions.Get(id));
        for (int key = 0; key < Capacity(lane); key++) runtime.SetParam(entity, key, lane, key, key, new Vector4(key));
        var before = world.Get<PresenterState>(entity).Version;
        Assert.Throws<InvalidOperationException>(() => runtime.SetParam(entity, Capacity(lane), lane, 123, 123, new Vector4(123)));
        Assert.That(world.Get<PresenterState>(entity).Version, Is.EqualTo(before));
        for (int key = 0; key < Capacity(lane); key++) Assert.That(Read(world, entity, lane, false, key), Is.EqualTo(key));
    }

    [TestCase(ParamLane.Float)]
    [TestCase(ParamLane.Int)]
    [TestCase(ParamLane.Vector)]
    public void Registration_RejectsDefaultsOverCapacity(ParamLane lane)
    {
        var registry = new PresenterDefinitionRegistry();
        var definition = new PresenterDefinition { ParamDefaults = Entries(lane, Capacity(lane) + 1) };
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => registry.Register("too.many.defaults", definition))!;
        Assert.That(error.Message, Does.Contain("too.many.defaults"));
        Assert.That(registry.RegisteredIds, Is.Empty);
    }

    [TestCase(ParamLane.Float)]
    [TestCase(ParamLane.Int)]
    [TestCase(ParamLane.Vector)]
    public void CreatePlan_RejectsOversizedChildOverridesBeforeCreatingRoot(ParamLane lane)
    {
        using var world = World.Create();
        var definitions = new PresenterDefinitionRegistry();
        int childId = definitions.Register("capacity.child", new PresenterDefinition());
        int rootId = definitions.Register("capacity.root", new PresenterDefinition
        {
            Children = [new ChildPresenterRef { DefinitionId = childId, ParamOverrides = Entries(lane, Capacity(lane) + 1) }],
        });
        var runtime = new PresenterEntityRuntime(world);
        runtime.BindDefinitions(definitions);
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => runtime.CreateHierarchy(
            definitions, rootId, Entity.Null, 1, PresentationAnchorKind.WorldPosition, Vector3.Zero, 1, Entity.Null, definitions.Get(rootId)))!;
        Assert.That(error.Message, Does.Contain("root/children[0]"));
        Assert.That(world.CountEntities(new QueryDescription().WithAll<PresenterState>()), Is.Zero);
        Assert.That(runtime.CreateTraceCount, Is.Zero);
    }

    [TestCase(ParamLane.Float)]
    [TestCase(ParamLane.Int)]
    [TestCase(ParamLane.Vector)]
    public void Defaults_RepeatedExistingKeyDoesNotConsumeAnotherSlot(ParamLane lane)
    {
        var entries = Entries(lane, Capacity(lane) + 1);
        entries[^1].ParamKey = 0;
        var registry = new PresenterDefinitionRegistry();
        Assert.DoesNotThrow(() => registry.Register("capacity.repeated", new PresenterDefinition { ParamDefaults = entries }));
    }

    private static ParamDefault[] Entries(ParamLane lane, int count) => Enumerable.Range(0, count)
        .Select(key => new ParamDefault { ParamKey = key, Lane = lane, FloatValue = key, IntValue = key, VectorValue = new Vector4(key) }).ToArray();

    [Test]
    public void SetDefaults_OverflowDoesNotPartiallyApplyEarlierLanes()
    {
        using var world = World.Create();
        var registry = new PresenterDefinitionRegistry();
        int id = registry.Register("capacity.defaults.atomic", new PresenterDefinition());
        var runtime = new PresenterEntityRuntime(world);
        runtime.BindDefinitions(registry);
        Entity entity = runtime.CreateHierarchy(registry, id, Entity.Null, 1,
            PresentationAnchorKind.WorldPosition, Vector3.Zero, 1, Entity.Null, registry.Get(id));
        for (int key = 0; key < PresenterVectorDefaults.MAX_ENTRIES; key++)
            world.Get<PresenterVectorDefaults>(entity).Set(key, new Vector4(key));
        world.Get<PresenterFloatDefaults>(entity).Set(0, 7f);
        var incoming = new PresenterDefinition
        {
            ParamDefaults =
            [
                new ParamDefault { ParamKey = 0, Lane = ParamLane.Float, FloatValue = 99f },
                new ParamDefault { ParamKey = 100, Lane = ParamLane.Vector, VectorValue = Vector4.One },
            ],
        };
        Assert.Throws<InvalidOperationException>(() => runtime.SetParamDefault(incoming, entity));
        Assert.That(world.Get<PresenterFloatDefaults>(entity).TryGet(0, out float original), Is.True);
        Assert.That(original, Is.EqualTo(7f));
        Assert.That(world.Get<PresenterVectorDefaults>(entity).Count, Is.EqualTo(PresenterVectorDefaults.MAX_ENTRIES));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void BatchCreate_ParameterCapacityFailureLeavesNoPresenters(bool childOverride)
    {
        using var world = World.Create();
        var registry = new PresenterDefinitionRegistry();
        var definition = new PresenterDefinition();
        if (childOverride)
        {
            int childId = registry.Register("capacity.batch.child", new PresenterDefinition());
            definition.Children = [new ChildPresenterRef
            {
                DefinitionId = childId,
                ParamOverrides = Entries(ParamLane.Vector, PresenterVectorParams.MAX_ENTRIES + 1),
            }];
        }
        int rootId = registry.Register("capacity.batch.root", definition);
        var runtime = new PresenterEntityRuntime(world);
        runtime.BindDefinitions(registry);
        Entity[] owners = [world.Create(VisualTransform.Default), world.Create(VisualTransform.Default)];
        var created = new Entity[2];
        ParamDefault[][] overrides = childOverride ? [] : [[], Entries(ParamLane.Vector, PresenterVectorParams.MAX_ENTRIES + 1)];
        Assert.Throws<InvalidOperationException>(() => runtime.CreateEntityAnchoredRootBatch(
            registry, rootId, owners, new[] { 1, 2 }, new[] { 1, 2 },
            new[] { VisualTransform.Default, VisualTransform.Default }, new CullState[2], definition, created,
            rootParamOverrides: overrides));
        Assert.That(world.CountEntities(new QueryDescription().WithAll<PresenterState>()), Is.Zero);
        Assert.That(runtime.CreateTraceCount, Is.Zero);
    }

    private static int Capacity(ParamLane lane) => lane switch
    {
        ParamLane.Float => PresenterFloatParams.MAX_ENTRIES,
        ParamLane.Int => PresenterIntParams.MAX_ENTRIES,
        ParamLane.Vector => PresenterVectorParams.MAX_ENTRIES,
        _ => throw new ArgumentOutOfRangeException(nameof(lane)),
    };

    private static void Write(World world, Entity entity, ParamLane lane, bool defaults, int key, int value)
    {
        switch (lane, defaults)
        {
            case (ParamLane.Float, false): world.Get<PresenterFloatParams>(entity).Set(key, value); break;
            case (ParamLane.Int, false): world.Get<PresenterIntParams>(entity).Set(key, value); break;
            case (ParamLane.Vector, false): world.Get<PresenterVectorParams>(entity).Set(key, new Vector4(value)); break;
            case (ParamLane.Float, true): world.Get<PresenterFloatDefaults>(entity).Set(key, value); break;
            case (ParamLane.Int, true): world.Get<PresenterIntDefaults>(entity).Set(key, value); break;
            case (ParamLane.Vector, true): world.Get<PresenterVectorDefaults>(entity).Set(key, new Vector4(value)); break;
            default: throw new ArgumentOutOfRangeException(nameof(lane));
        }
    }

    private static float Read(World world, Entity entity, ParamLane lane, bool defaults, int key)
    {
        switch (lane, defaults)
        {
            case (ParamLane.Float, false):
            {
                Assert.That(world.Get<PresenterFloatParams>(entity).TryGet(key, out float value), Is.True);
                return value;
            }
            case (ParamLane.Int, false):
            {
                Assert.That(world.Get<PresenterIntParams>(entity).TryGet(key, out int value), Is.True);
                return value;
            }
            case (ParamLane.Vector, false):
            {
                Assert.That(world.Get<PresenterVectorParams>(entity).TryGet(key, out Vector4 value), Is.True);
                return value.X;
            }
            case (ParamLane.Float, true):
            {
                Assert.That(world.Get<PresenterFloatDefaults>(entity).TryGet(key, out float value), Is.True);
                return value;
            }
            case (ParamLane.Int, true):
            {
                Assert.That(world.Get<PresenterIntDefaults>(entity).TryGet(key, out int value), Is.True);
                return value;
            }
            case (ParamLane.Vector, true):
            {
                Assert.That(world.Get<PresenterVectorDefaults>(entity).TryGet(key, out Vector4 value), Is.True);
                return value.X;
            }
            default: throw new ArgumentOutOfRangeException(nameof(lane));
        }
    }
}
