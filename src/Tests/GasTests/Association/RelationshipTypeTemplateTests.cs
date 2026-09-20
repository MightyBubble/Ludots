using System;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Relationships.Config;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class RelationshipTypeTemplateTests
    {
        private const string TemplatedTypeName = "Tests.Relationship.Kinship.FatherSon";
        private const string UntemplatedTypeName = "Tests.Relationship.Untemplated";
        private const string FatherBondAttribute = "Tests.Kinship.FatherBond";
        private const string DutyAttribute = "Tests.Kinship.Duty";
        private const string BloodTag = "Tests.Kinship.Blood";
        private const string PatriarchTag = "Tests.Kinship.Patriarch";

        private static readonly JsonSerializerOptions CatalogOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() },
        };

        [Test]
        public void TypeTemplate_FromCatalogJson_MaterializesInitialAttributesAndBirthTags()
        {
            RelationshipCatalogConfig catalog = LoadCatalog($$"""
                {
                  "types": [
                    {
                      "id": "{{TemplatedTypeName}}",
                      "isSymmetric": false,
                      "template": {
                        "components": {
                          "AttributeBuffer": { "base": { "{{FatherBondAttribute}}": 80, "{{DutyAttribute}}": 40 } },
                          "GameplayTagContainer": { "tags": ["{{BloodTag}}", "{{PatriarchTag}}"] }
                        }
                      }
                    }
                  ]
                }
                """);
            using var world = World.Create();
            RelationshipRuntime runtime = CreateRuntime(world, out RelationshipTypeRegistry types);
            RelationshipCatalogInstaller.RegisterCatalog(catalog, types, new RelationshipMetricRegistry(), new RelationshipFlagRegistry(), new RelationshipBandRegistry(), new RelationshipReasonRegistry());
            runtime.InstallTypeTemplates(catalog);

            int fatherSonTypeId = types.GetId(TemplatedTypeName);
            Entity source = world.Create();
            Entity target = world.Create();

            runtime.EnsureLink(source, target, fatherSonTypeId);

            Assert.That(runtime.TryResolveRelationshipEntity(source, target, fatherSonTypeId, out Entity relationEntity), Is.True);
            int fatherBondId = AttributeRegistry.GetId(FatherBondAttribute);
            int dutyId = AttributeRegistry.GetId(DutyAttribute);
            AttributeBuffer attributes = world.Get<AttributeBuffer>(relationEntity);
            Assert.Multiple(() =>
            {
                Assert.That(attributes.GetBase(fatherBondId), Is.EqualTo(80f));
                Assert.That(attributes.GetCurrent(fatherBondId), Is.EqualTo(80f));
                Assert.That(attributes.GetBase(dutyId), Is.EqualTo(40f));
                Assert.That(attributes.GetCurrent(dutyId), Is.EqualTo(40f));
                Assert.That(world.Has<AttributeLastSnapshot>(relationEntity), Is.True,
                    "The ComponentRegistry AttributeBuffer authoring chain installs its snapshot companion.");
            });

            int bloodTagId = TagRegistry.GetId(BloodTag);
            int patriarchTagId = TagRegistry.GetId(PatriarchTag);
            GameplayTagContainer tags = world.Get<GameplayTagContainer>(relationEntity);
            Assert.Multiple(() =>
            {
                Assert.That(tags.HasTag(bloodTagId), Is.True);
                Assert.That(tags.HasTag(patriarchTagId), Is.True);
                Assert.That(world.Has<TagCountContainer>(relationEntity), Is.True);
                Assert.That(world.Has<ActiveEffectContainer>(relationEntity), Is.True);
                Assert.That(world.Has<DirtyFlags>(relationEntity), Is.True);
            });

            RelationshipInstanceCm instance = world.Get<RelationshipInstanceCm>(relationEntity);
            Assert.Multiple(() =>
            {
                Assert.That(instance.Source, Is.EqualTo(source));
                Assert.That(instance.Target, Is.EqualTo(target));
                Assert.That(instance.TypeId, Is.EqualTo(fatherSonTypeId));
                Assert.That(instance.Revision, Is.EqualTo(1));
            });
        }

        [Test]
        public void TypeTemplate_RepeatedEnsureLink_DoesNotReplayTemplateOrResetValues()
        {
            RelationshipCatalogConfig catalog = LoadCatalog($$"""
                {
                  "types": [
                    {
                      "id": "{{TemplatedTypeName}}",
                      "isSymmetric": false,
                      "template": {
                        "components": {
                          "AttributeBuffer": { "base": { "{{FatherBondAttribute}}": 80 } },
                          "GameplayTagContainer": { "tags": ["{{BloodTag}}"] }
                        }
                      }
                    }
                  ]
                }
                """);
            using var world = World.Create();
            RelationshipRuntime runtime = CreateRuntime(world, out RelationshipTypeRegistry types);
            RelationshipCatalogInstaller.RegisterCatalog(catalog, types, new RelationshipMetricRegistry(), new RelationshipFlagRegistry(), new RelationshipBandRegistry(), new RelationshipReasonRegistry());
            runtime.InstallTypeTemplates(catalog);
            int fatherSonTypeId = types.GetId(TemplatedTypeName);
            Entity source = world.Create();
            Entity target = world.Create();

            runtime.EnsureLink(source, target, fatherSonTypeId);
            Assert.That(runtime.TryResolveRelationshipEntity(source, target, fatherSonTypeId, out Entity firstEntity), Is.True);
            int fatherBondId = AttributeRegistry.GetId(FatherBondAttribute);
            ref AttributeBuffer attributes = ref world.Get<AttributeBuffer>(firstEntity);
            attributes.SetCurrent(fatherBondId, 12f);
            world.Get<GameplayTagContainer>(firstEntity).RemoveTag(TagRegistry.GetId(BloodTag));

            runtime.EnsureLink(source, target, fatherSonTypeId);
            runtime.MaterializeRelationshipEntity(source, target, fatherSonTypeId);

            Assert.That(runtime.TryResolveRelationshipEntity(source, target, fatherSonTypeId, out Entity secondEntity), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(secondEntity, Is.EqualTo(firstEntity));
                Assert.That(world.Get<AttributeBuffer>(secondEntity).GetCurrent(fatherBondId), Is.EqualTo(12f),
                    "A repeated EnsureLink must not replay the type template over mutated values.");
                Assert.That(world.Get<GameplayTagContainer>(secondEntity).HasTag(TagRegistry.GetId(BloodTag)), Is.False,
                    "A repeated EnsureLink must not re-add removed birth tags.");
                Assert.That(world.Get<RelationshipInstanceCm>(secondEntity).Revision, Is.EqualTo(1));
            });
        }

        [Test]
        public void TypeTemplate_AbsentTemplate_MaterializesDefaultRelationshipEntity()
        {
            RelationshipCatalogConfig catalog = LoadCatalog($$"""{ "types": [ { "id": "{{UntemplatedTypeName}}", "isSymmetric": false } ] }""");
            using var world = World.Create();
            RelationshipRuntime runtime = CreateRuntime(world, out RelationshipTypeRegistry types);
            RelationshipCatalogInstaller.RegisterCatalog(catalog, types, new RelationshipMetricRegistry(), new RelationshipFlagRegistry(), new RelationshipBandRegistry(), new RelationshipReasonRegistry());
            runtime.InstallTypeTemplates(catalog);
            int untemplatedTypeId = types.GetId(UntemplatedTypeName);
            Entity source = world.Create();
            Entity target = world.Create();

            runtime.EnsureLink(source, target, untemplatedTypeId);

            Assert.That(runtime.TryResolveRelationshipEntity(source, target, untemplatedTypeId, out Entity relationEntity), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(world.Has<AttributeBuffer>(relationEntity), Is.True);
                Assert.That(world.Get<AttributeBuffer>(relationEntity).DefinedMask, Is.EqualTo(0UL));
                Assert.That(world.Get<GameplayTagContainer>(relationEntity).IsEmpty, Is.True);
                Assert.That(world.Has<AttributeLastSnapshot>(relationEntity), Is.False,
                    "Untemplated relationship entities keep the pre-template component shape.");
            });
        }

        [Test]
        public void TypeTemplate_AuthoringRuntimeOwnedIdentityComponent_FailsFast()
        {
            RelationshipCatalogConfig catalog = LoadCatalog($$"""
                {
                  "types": [
                    {
                      "id": "{{TemplatedTypeName}}",
                      "template": { "components": { "RelationshipInstanceCm": { "TypeId": 7 } } }
                    }
                  ]
                }
                """);
            using var world = World.Create();
            RelationshipRuntime runtime = CreateRuntime(world, out RelationshipTypeRegistry types);
            RelationshipCatalogInstaller.RegisterCatalog(catalog, types, new RelationshipMetricRegistry(), new RelationshipFlagRegistry(), new RelationshipBandRegistry(), new RelationshipReasonRegistry());

            InvalidOperationException? ex = Assert.Throws<InvalidOperationException>(() => runtime.InstallTypeTemplates(catalog));
            Assert.That(ex!.Message, Does.Contain("runtime-owned"));

            var relationshipEntities = new QueryDescription().WithAll<RelationshipInstanceCm>();
            int survivors = 0;
            world.Query(in relationshipEntities, (Entity entity) => { survivors++; });
            Assert.That(survivors, Is.EqualTo(0), "The failed bake must not leave a prototype entity behind.");
        }

        [Test]
        public void TypeTemplate_TypedEdgeChurnOnTemplatedType_AllocatesZeroAfterWarmup()
        {
            RelationshipCatalogConfig catalog = LoadCatalog($$"""
                {
                  "types": [
                    {
                      "id": "{{TemplatedTypeName}}",
                      "template": {
                        "components": {
                          "AttributeBuffer": { "base": { "{{FatherBondAttribute}}": 80 } },
                          "GameplayTagContainer": { "tags": ["{{BloodTag}}"] }
                        }
                      }
                    }
                  ]
                }
                """);
            using var world = World.Create();
            RelationshipRuntime runtime = CreateRuntime(world, out RelationshipTypeRegistry types);
            var flags = new RelationshipFlagRegistry();
            RelationshipCatalogInstaller.RegisterCatalog(catalog, types, new RelationshipMetricRegistry(), flags, new RelationshipBandRegistry(), new RelationshipReasonRegistry());
            runtime.InstallTypeTemplates(catalog);
            int fatherSonTypeId = types.GetId(TemplatedTypeName);
            int kinshipFlagId = flags.Register("Tests.Kinship.Flag");
            int stableTypeId = types.Register("Tests.Relationship.StablePair");
            Entity source = world.Create();
            Entity target = world.Create();

            runtime.EnsureLink(source, target, stableTypeId);
            runtime.EnsureLink(source, target, fatherSonTypeId);
            for (int i = 0; i < 4; i++)
            {
                runtime.EnsureLink(source, target, fatherSonTypeId);
                runtime.RemoveLink(source, target, fatherSonTypeId);
            }

            long allocated = MeasureTemplatedChurn(runtime, source, target, fatherSonTypeId, kinshipFlagId, iterations: 1_024);
            long second = MeasureTemplatedChurn(runtime, source, target, fatherSonTypeId, kinshipFlagId, iterations: 1_024);

            Assert.That(Math.Min(allocated, second), Is.EqualTo(0),
                "Warmed typed edge churn through template application must stay allocation-free.");
            Assert.That(runtime.HasLink(source, target, stableTypeId), Is.True,
                "The retained stable pair edge must survive the transient typed churn.");

            runtime.EnsureLink(source, target, fatherSonTypeId);
            Assert.That(runtime.TryResolveRelationshipEntity(source, target, fatherSonTypeId, out Entity entity), Is.True);
            Assert.That(world.Get<AttributeBuffer>(entity).GetCurrent(AttributeRegistry.GetId(FatherBondAttribute)), Is.EqualTo(80f),
                "Every rematerialized entity must receive the template values again.");
        }

        private static long MeasureTemplatedChurn(
            RelationshipRuntime runtime,
            Entity source,
            Entity target,
            int typeId,
            int flagId,
            int iterations)
        {
            GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < iterations; i++)
            {
                runtime.EnsureLink(source, target, typeId);
                runtime.SetFlag(source, target, typeId, flagId, enabled: true);
                if (!runtime.TryResolveRelationshipEntity(source, target, typeId, out Entity entity) ||
                    !runtime.World.IsAlive(entity) ||
                    !runtime.World.Has<RelationshipInstanceCm>(entity))
                {
                    throw new InvalidOperationException("Measured templated churn lost the relationship entity.");
                }

                runtime.RemoveLink(source, target, typeId);
            }

            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        private static RelationshipCatalogConfig LoadCatalog(string json)
        {
            return JsonNode.Parse(json)!.Deserialize<RelationshipCatalogConfig>(CatalogOptions)
                ?? throw new InvalidOperationException("Failed to deserialize relationship catalog fixture.");
        }

        private static RelationshipRuntime CreateRuntime(World world, out RelationshipTypeRegistry types)
        {
            types = new RelationshipTypeRegistry();
            return new RelationshipRuntime(
                world,
                types,
                new RelationshipMetricRegistry(),
                new RelationshipFlagRegistry(),
                new RelationshipBandRegistry(),
                new RelationshipChangeBuffer(),
                new RelationshipReverseIndex(world));
        }
    }
}
