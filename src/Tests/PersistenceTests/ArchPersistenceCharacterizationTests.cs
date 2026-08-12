using Arch.Core;
using Arch.Persistence;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Mathematics;
using Ludots.Core.Persistence;
using MessagePack;
using NUnit.Framework;
using CoreComponentRegistry = Ludots.Core.Config.ComponentRegistry;

namespace Ludots.Tests.Persistence;

[TestFixture]
public sealed class ArchPersistenceCharacterizationTests
{
    [Test]
    public void BinaryWorldRoundTripPreservesSimpleBlittableComponents()
    {
        using World world = World.Create();
        world.Create(
            WorldPositionCm.FromCm(1234, -5678),
            new FacingDirection { AngleRad = 1.25f });

        using World restored = RoundTrip(world);
        Entity restoredEntity = FindSingle<WorldPositionCm>(restored);

        ref readonly WorldPositionCm position = ref restored.Get<WorldPositionCm>(restoredEntity);
        ref readonly FacingDirection facing = ref restored.Get<FacingDirection>(restoredEntity);

        Assert.That(position.ToWorldCmInt2(), Is.EqualTo(new WorldCmInt2(1234, -5678)));
        Assert.That(facing.AngleRad, Is.EqualTo(1.25f));
    }

    [Test]
    public void BinaryWorldRoundTripPreservesEntityLocalClock()
    {
        using World world = World.Create();
        world.Create(new EntityLocalClock { AccumulatorPermille = 500, LocalStep = 12 });

        using World restored = CoreRoundTrip(world);
        Entity restoredEntity = FindSingle<EntityLocalClock>(restored);
        ref readonly EntityLocalClock clock = ref restored.Get<EntityLocalClock>(restoredEntity);
        int accumulatorPermille = clock.AccumulatorPermille;
        int localStep = clock.LocalStep;

        Assert.Multiple(() =>
        {
            Assert.That(accumulatorPermille, Is.EqualTo(500));
            Assert.That(localStep, Is.EqualTo(12));
        });
    }

    [Test]
    public void BinaryWorldRoundTripPreservesEmptyWorldShape()
    {
        using World world = World.Create();

        using World restored = RoundTrip(world);

        Assert.That(restored.CountEntities(in QueryDescription.Null), Is.EqualTo(0));
    }

    [Test]
    public void BinaryWorldRoundTripPreservesManagedNameString()
    {
        using World world = World.Create();
        world.Create(new Name { Value = "Save-中文-Name" });

        using World restored = RoundTrip(world);
        Entity restoredEntity = FindSingle<Name>(restored);

        Assert.That(restored.Get<Name>(restoredEntity).Value, Is.EqualTo("Save-中文-Name"));
    }

    [Test]
    public void BinaryWorldRoundTripPreservesRestoredEntityAliveMetadata()
    {
        using World world = World.Create();
        world.Create(WorldPositionCm.FromCm(10, 20));

        var serializer = new ArchBinarySerializer();

        byte[] bytes = serializer.Serialize(world);
        using World restored = serializer.Deserialize(bytes);
        Entity restoredEntity = FindSingle<WorldPositionCm>(restored);

        Assert.That(restored.IsAlive(restoredEntity), Is.True);
    }

    [Test]
    public void BinaryWorldRoundTripPreservesEntityVersionMetadata()
    {
        using World world = World.Create();
        Entity recycled = world.Create(new Name { Value = "old" });
        int recycledId = recycled.Id;
        world.Destroy(recycled);
        Entity current = world.Create(new Name { Value = "new" });
        Assert.That(current.Id, Is.EqualTo(recycledId));
        Assert.That(current.Version, Is.GreaterThan(0));

        var serializer = new ArchBinarySerializer();

        byte[] bytes = serializer.Serialize(world);
        using World restored = serializer.Deserialize(bytes);
        Entity restoredEntity = FindSingle<Name>(restored);

        Assert.That(restoredEntity.Id, Is.EqualTo(current.Id));
        Assert.That(restoredEntity.Version, Is.EqualTo(current.Version));
        Assert.That(restored.IsAlive(restoredEntity), Is.True);
    }

    [Test]
    public void BinaryWorldRoundTripCurrentlyFailsOrCorruptsAttributeBufferFixedStorage()
    {
        using World world = World.Create();
        var attributes = AttributeBuffer.CreateAttached();
        attributes.SetBase(1, 12.5f);
        attributes.SetCurrent(1, 7.25f);
        attributes.SetBase(7, 99f);
        world.Create(attributes);

        World? restored = TryRoundTrip(world, out Exception? error);
        try
        {
            if (error is not null)
            {
                Assert.That(error, Is.Not.Null);
                return;
            }

            bool preserved = TryFindSingle<AttributeBuffer>(restored!, out Entity restoredEntity) &&
                restored!.Get<AttributeBuffer>(restoredEntity).HasAttribute(1) &&
                restored.Get<AttributeBuffer>(restoredEntity).GetBase(1) == 12.5f &&
                restored.Get<AttributeBuffer>(restoredEntity).GetCurrent(1) == 7.25f &&
                restored.Get<AttributeBuffer>(restoredEntity).HasAttribute(7) &&
                restored.Get<AttributeBuffer>(restoredEntity).GetBase(7) == 99f;

            Assert.That(preserved, Is.False);
        }
        finally
        {
            restored?.Dispose();
        }
    }

    [Test]
    public void CoreBinarySerializerPreservesAttributeBufferFixedStorage()
    {
        using World world = World.Create();
        var attributes = AttributeBuffer.CreateAttached();
        attributes.SetBase(1, 12.5f);
        attributes.SetCurrent(1, 7.25f);
        attributes.SetBase(7, 99f);
        world.Create(attributes);

        using World restored = CoreRoundTrip(world);
        Entity restoredEntity = FindSingle<AttributeBuffer>(restored);
        ref readonly AttributeBuffer restoredAttributes = ref restored.Get<AttributeBuffer>(restoredEntity);

        Assert.That(restoredAttributes.HasAttribute(1), Is.True);
        Assert.That(restoredAttributes.GetBase(1), Is.EqualTo(12.5f));
        Assert.That(restoredAttributes.GetCurrent(1), Is.EqualTo(7.25f));
        Assert.That(restoredAttributes.HasAttribute(7), Is.True);
        Assert.That(restoredAttributes.GetBase(7), Is.EqualTo(99f));
    }

    [Test]
    public void BinaryWorldRoundTripCurrentlyFailsOrCorruptsGameplayTagContainerFixedStorage()
    {
        using World world = World.Create();
        var tags = new GameplayTagContainer();
        tags.AddTag(3);
        tags.AddTag(130);
        world.Create(tags);

        World? restored = TryRoundTrip(world, out Exception? error);
        try
        {
            if (error is not null)
            {
                Assert.That(error, Is.Not.Null);
                return;
            }

            bool preserved = TryFindSingle<GameplayTagContainer>(restored!, out Entity restoredEntity) &&
                restored!.Get<GameplayTagContainer>(restoredEntity).HasTag(3) &&
                restored.Get<GameplayTagContainer>(restoredEntity).HasTag(130);

            Assert.That(preserved, Is.False);
        }
        finally
        {
            restored?.Dispose();
        }
    }

    [Test]
    public void CoreBinarySerializerPreservesGameplayTagContainerFixedStorage()
    {
        using World world = World.Create();
        var tags = new GameplayTagContainer();
        tags.AddTag(3);
        tags.AddTag(130);
        world.Create(tags);

        using World restored = CoreRoundTrip(world);
        Entity restoredEntity = FindSingle<GameplayTagContainer>(restored);
        ref readonly GameplayTagContainer restoredTags = ref restored.Get<GameplayTagContainer>(restoredEntity);

        Assert.That(restoredTags.HasTag(3), Is.True);
        Assert.That(restoredTags.HasTag(130), Is.True);
    }

    [Test]
    public void BinaryWorldRoundTripCurrentlyFailsOrCorruptsEntityRefFixedStorage()
    {
        using World world = World.Create();
        Entity firstTarget = world.Create(new Name { Value = "target-a" });
        Entity secondTarget = world.Create(new Name { Value = "target-b" });
        var refs = new BlackboardEntityBuffer();
        refs.Set(42, firstTarget);
        refs.Set(43, secondTarget);
        world.Create(refs);

        World? restored = TryRoundTrip(world, out Exception? error);
        try
        {
            if (error is not null)
            {
                Assert.That(error, Is.Not.Null);
                return;
            }

            bool preserved = TryFindSingle<BlackboardEntityBuffer>(restored!, out Entity restoredEntity) &&
                restored!.Get<BlackboardEntityBuffer>(restoredEntity).TryGet(42, out Entity firstRestoredRef) &&
                restored.Get<BlackboardEntityBuffer>(restoredEntity).TryGet(43, out Entity secondRestoredRef) &&
                firstRestoredRef.Id == firstTarget.Id &&
                firstRestoredRef.Version == firstTarget.Version &&
                secondRestoredRef.Id == secondTarget.Id &&
                secondRestoredRef.Version == secondTarget.Version;

            Assert.That(preserved, Is.False);
        }
        finally
        {
            restored?.Dispose();
        }
    }

    [Test]
    public void CoreBinarySerializerPreservesEntityRefFixedStorage()
    {
        using World world = World.Create();
        Entity firstTarget = world.Create(new Name { Value = "target-a" });
        Entity secondTarget = world.Create(new Name { Value = "target-b" });
        var refs = new BlackboardEntityBuffer();
        refs.Set(42, firstTarget);
        refs.Set(43, secondTarget);
        world.Create(refs);

        using World restored = CoreRoundTrip(world);
        Entity restoredEntity = FindSingle<BlackboardEntityBuffer>(restored);
        ref readonly BlackboardEntityBuffer restoredRefs = ref restored.Get<BlackboardEntityBuffer>(restoredEntity);

        Assert.That(restoredRefs.TryGet(42, out Entity firstRestoredRef), Is.True);
        Assert.That(restoredRefs.TryGet(43, out Entity secondRestoredRef), Is.True);
        Assert.That(firstRestoredRef.Id, Is.EqualTo(firstTarget.Id));
        Assert.That(firstRestoredRef.Version, Is.EqualTo(firstTarget.Version));
        Assert.That(secondRestoredRef.Id, Is.EqualTo(secondTarget.Id));
        Assert.That(secondRestoredRef.Version, Is.EqualTo(secondTarget.Version));
    }

    [Test]
    public void CoreBinarySerializerPreservesRelationshipRuntimeEdges()
    {
        using World world = World.Create();
        Entity source = world.Create(new Name { Value = "source" });
        Entity target = world.Create(new Name { Value = "target" });
        RelationshipRuntime runtime = CreateRelationshipRuntime(
            world,
            out RelationshipTypeRegistry types,
            out RelationshipMetricRegistry metrics,
            out RelationshipFlagRegistry flags,
            out RelationshipBandRegistry bands,
            out int allianceType,
            out int rivalryType,
            out int trustMetric,
            out int visibleFlag);

        runtime.SetMetric(source, target, allianceType, trustMetric, 42);
        runtime.SetFlag(source, target, allianceType, visibleFlag, enabled: true);
        runtime.SetMetric(source, target, rivalryType, trustMetric, -17);

        using World restored = CoreRoundTrip(world);
        Entity restoredSource = FindByName(restored, "source");
        Entity restoredTarget = FindByName(restored, "target");
        var restoredRuntime = new RelationshipRuntime(
            restored,
            types,
            metrics,
            flags,
            bands,
            new RelationshipChangeBuffer(),
            new RelationshipReverseIndex(restored));

        Assert.That(restoredRuntime.HasLink(restoredSource, restoredTarget, allianceType), Is.True);
        Assert.That(restoredRuntime.HasLink(restoredSource, restoredTarget, rivalryType), Is.True);
        Assert.That(restoredRuntime.GetMetric(restoredSource, restoredTarget, allianceType, trustMetric), Is.EqualTo(42));
        Assert.That(restoredRuntime.GetMetric(restoredSource, restoredTarget, rivalryType, trustMetric), Is.EqualTo(-17));
        Assert.That(restoredRuntime.HasFlag(restoredSource, restoredTarget, allianceType, visibleFlag), Is.True);
        Span<Entity> incoming = stackalloc Entity[4];
        Assert.That(restoredRuntime.CollectIncoming(restoredTarget, allianceType, incoming), Is.EqualTo(1));
        Assert.That(incoming[0], Is.EqualTo(restoredSource));

        restoredRuntime.RemoveLink(restoredSource, restoredTarget, allianceType);
        Assert.That(restoredRuntime.HasLink(restoredSource, restoredTarget, rivalryType), Is.True);
        Assert.That(restoredRuntime.CollectIncoming(restoredTarget, allianceType, incoming), Is.EqualTo(0));
        Assert.That(restoredRuntime.CollectIncoming(restoredTarget, rivalryType, incoming), Is.EqualTo(1));

        Assert.DoesNotThrow(() => restoredRuntime.RemoveLink(restoredSource, restoredTarget, rivalryType));
        Assert.That(restoredRuntime.HasLink(restoredSource, restoredTarget), Is.False);
        Assert.That(restoredRuntime.CollectIncoming(restoredTarget, RelationshipTypeRegistry.AnyTypeId, incoming), Is.EqualTo(0));
    }

    [Test]
    public void ArchBinarySerializerReadsComponentArrayPayloadsThroughSignatureComponentContract()
    {
        var loadContext = new CrossLoadContextTypeTestHarness.DuplicateAssemblyLoadContext();
        try
        {
            Type duplicateNameType = CrossLoadContextTypeTestHarness.LoadDuplicateType(loadContext, typeof(Name));
            Assert.That(duplicateNameType, Is.Not.SameAs(typeof(Name)));
            using World world = World.Create();
            world.Create(new Name { Value = "cross-alc-name" });
            var typeFormatter = new CrossLoadContextTypeTestHarness.SubstitutingTypeFormatter(
                typeof(Name),
                duplicateNameType);
            var serializer = new ArchBinarySerializer(
                new NameFormatter(),
                typeFormatter);

            byte[] bytes = serializer.Serialize(world);
            using World restored = serializer.Deserialize(bytes);
            Entity restoredEntity = FindSingle<Name>(restored);

            Assert.That(restored.Get<Name>(restoredEntity).Value, Is.EqualTo("cross-alc-name"));
            Assert.That(typeFormatter.SubstitutionHitCount, Is.GreaterThan(0));
        }
        finally
        {
            loadContext.Unload();
        }
    }

    [Test]
    public void ArchBinarySerializerRejectsComponentArrayPayloadWhenSerializedTypeContractDiffersFromSignature()
    {
        using World world = World.Create();
        world.Create(new Name { Value = "wrong-contract" });
        var typeFormatter = new CrossLoadContextTypeTestHarness.SubstitutingTypeFormatter(
            typeof(Name),
            typeof(WorldPositionCm));
        var serializer = new ArchBinarySerializer(
            new NameFormatter(),
            typeFormatter);

        byte[] bytes = serializer.Serialize(world);
        var error = Assert.Throws<MessagePackSerializationException>(() => serializer.Deserialize(bytes));

        string diagnostic = FlattenExceptionMessages(error!);
        Assert.That(diagnostic, Does.Contain("does not match archetype component type"));
        Assert.That(diagnostic, Does.Contain(nameof(Name)));
        Assert.That(diagnostic, Does.Contain(nameof(WorldPositionCm)));
        Assert.That(typeFormatter.SubstitutionHitCount, Is.GreaterThan(0));
    }

    [Test]
    public void CoreBinarySerializerRejectsRelationshipsPointingAtExcludedEntities()
    {
        using World world = World.Create();
        Entity source = world.Create(new Name { Value = "source" });
        Entity target = world.Create(new Name { Value = "excluded-target" }, new SaveExcludedTag());
        RelationshipRuntime runtime = CreateRelationshipRuntime(
            world,
            out _,
            out _,
            out _,
            out _,
            out int allianceType,
            out _,
            out int trustMetric,
            out _);

        runtime.SetMetric(source, target, allianceType, trustMetric, 42);

        var directError = Assert.Throws<SaveContextException>(
            () => SaveEntityReferenceValidator.Validate(world, SaveEntityInclusionPolicy.Default));
        var roundTripError = Assert.Throws<SaveContextException>(() => CoreRoundTrip(world));

        Assert.That(directError!.Message, Does.Contain("excluded entity"));
        Assert.That(directError.Message, Does.Contain("Relationship<RelationshipEdgeSet>"));
        Assert.That(roundTripError!.Message, Does.Contain("missing entity"));
        Assert.That(roundTripError.Message, Does.Contain("Relationship<RelationshipEdgeSet>"));
    }

    [Test]
    public void CoreBinarySerializerRejectsRelationshipProjectionWithoutMatchingEdge()
    {
        using World world = World.Create();
        Entity source = world.Create(new Name { Value = "source" });
        Entity target = world.Create(new Name { Value = "target" });
        world.Create(new RelationshipInstanceCm
        {
            Source = source,
            Target = target,
            TypeId = 1,
            Revision = 1
        });

        var directError = Assert.Throws<SaveContextException>(
            () => SaveEntityReferenceValidator.Validate(world, SaveEntityInclusionPolicy.Default));
        var roundTripError = Assert.Throws<SaveContextException>(() => CoreRoundTrip(world));

        Assert.That(directError!.Message, Does.Contain(nameof(RelationshipInstanceCm)));
        Assert.That(directError.Message, Does.Contain("no matching relationship edge"));
        Assert.That(roundTripError!.Message, Does.Contain(nameof(RelationshipInstanceCm)));
        Assert.That(roundTripError.Message, Does.Contain("no matching relationship edge"));
    }

    [Test]
    public void CorePersistenceFormatterRegistryCoversComponentRegistryTypes()
    {
        IReadOnlySet<Type> formatterTypes = LudotsCorePersistenceFormatters.GetFormatterComponentTypes();
        string[] missing = CoreComponentRegistry.GetRegisteredComponentTypes()
            .Values
            .Select(componentType => componentType.Type)
            .Where(type => !formatterTypes.Contains(type))
            .Select(type => type.FullName ?? type.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.That(missing, Is.Empty);
    }

    [Test]
    public void CoreBinarySerializerRejectsPersistedComponentsWithoutLudotsFormatter()
    {
        using World world = World.Create();
        world.Create(new UnsupportedManagedComponent("contractless-is-not-a-save-format"));

        var error = Assert.Throws<SaveContextException>(() => CoreRoundTrip(world));

        Assert.That(error!.Message, Does.Contain(nameof(UnsupportedManagedComponent)));
        Assert.That(error.Message, Does.Contain("without Ludots persistence formatters"));
    }

    [Test]
    public void CorePersistenceFormattersAreScannedOnceForRepeatedSerializes()
    {
        LudotsCorePersistenceFormatters.ResetCacheForTests();
        using World world = World.Create();
        world.Create(new Name { Value = "scan-once" }, WorldPositionCm.FromCm(1, 2));
        var serializer = new LudotsBinaryWorldSerializer();

        serializer.Serialize(world);
        serializer.Serialize(world);

        Assert.That(LudotsCorePersistenceFormatters.FormatterCacheBuildCountForTests, Is.EqualTo(1));
    }

    private static World RoundTrip(World world)
    {
        var serializer = new ArchBinarySerializer();
        byte[] bytes = serializer.Serialize(world);
        return serializer.Deserialize(bytes);
    }

    private static World CoreRoundTrip(World world)
    {
        var serializer = new LudotsBinaryWorldSerializer();
        byte[] bytes = serializer.Serialize(world);
        return serializer.Deserialize(bytes);
    }

    private static World? TryRoundTrip(World world, out Exception? error)
    {
        try
        {
            error = null;
            return RoundTrip(world);
        }
        catch (Exception ex)
        {
            error = ex;
            return null;
        }
    }

    private static Entity FindSingle<T>(World world)
    {
        bool found = TryFindSingle<T>(world, out Entity result, out int count);
        Assert.That(found, Is.True);
        Assert.That(count, Is.EqualTo(1));
        return result;
    }

    private static string FlattenExceptionMessages(Exception exception)
    {
        var messages = new List<string>();
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            messages.Add(current.Message);
        }

        return string.Join(" | ", messages);
    }

    private static Entity FindByName(World world, string name)
    {
        var query = new QueryDescription().WithAll<Name>();
        Entity result = Entity.Null;
        int matches = 0;

        world.Query(in query, (Entity entity, ref Name entityName) =>
        {
            if (entityName.Value != name)
            {
                return;
            }

            result = entity;
            matches++;
        });

        Assert.That(matches, Is.EqualTo(1));
        return result;
    }

    private static RelationshipRuntime CreateRelationshipRuntime(
        World world,
        out RelationshipTypeRegistry types,
        out RelationshipMetricRegistry metrics,
        out RelationshipFlagRegistry flags,
        out RelationshipBandRegistry bands,
        out int allianceType,
        out int rivalryType,
        out int trustMetric,
        out int visibleFlag)
    {
        types = new RelationshipTypeRegistry();
        metrics = new RelationshipMetricRegistry();
        flags = new RelationshipFlagRegistry();
        bands = new RelationshipBandRegistry();
        RelationshipChangeBuffer changes = new();

        allianceType = types.Register("alliance");
        rivalryType = types.Register("rivalry");
        trustMetric = metrics.Register("trust", minValue: -100, maxValue: 100, defaultValue: 0);
        visibleFlag = flags.Register("visible");

        return new RelationshipRuntime(
            world,
            types,
            metrics,
            flags,
            bands,
            changes,
            new RelationshipReverseIndex(world));
    }

    private static bool TryFindSingle<T>(World world, out Entity result)
    {
        return TryFindSingle<T>(world, out result, out _);
    }

    private static bool TryFindSingle<T>(World world, out Entity result, out int count)
    {
        var query = new QueryDescription().WithAll<T>();
        Entity found = Entity.Null;
        int matches = 0;

        world.Query(in query, entity =>
        {
            found = entity;
            matches++;
        });

        result = found;
        count = matches;
        return count == 1;
    }

    private sealed class UnsupportedManagedPayload
    {
        public string Value { get; set; } = string.Empty;
    }

    private readonly struct UnsupportedManagedComponent
    {
        public UnsupportedManagedComponent(string value)
        {
            Payload = new UnsupportedManagedPayload { Value = value };
        }

        public UnsupportedManagedPayload Payload { get; }
    }

}
