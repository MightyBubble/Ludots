using System.Text.Json.Nodes;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Items;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Relationships.Config;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Gameplay.GAS.Input;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Engine;
using Ludots.Core.Association;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Features.TagState;

[TestFixture]
[NonParallelizable]
public sealed class TagStateInstallationContractTests
{
    [SetUp]
    public void SetUp()
    {
        AbilityFormSetIdRegistry.Clear();
    }

    [TearDown]
    public void TearDown()
    {
        AbilityFormSetIdRegistry.Clear();
    }

    [Test]
    public void TagStateInstaller_InstallsCompleteStateAtomically()
    {
        using var world = World.Create();
        Entity entity = world.Create();

        TagStateInstaller.EnsureInstalled(world, entity);

        Assert.That(world.Has<GameplayTagContainer>(entity), Is.True);
        Assert.That(world.Has<TagCountContainer>(entity), Is.True);
        Assert.That(world.Has<DirtyFlags>(entity), Is.True);
    }

    [Test]
    public void TagStateInstaller_WhenTagsAlreadyExist_RebuildsInitialCounts()
    {
        using var world = World.Create();
        var tags = GameplayTagContainer.CreateAttached();
        tags.AddTag(7);
        tags.AddTag(11);
        Entity entity = world.Create(tags);

        TagStateInstaller.EnsureInstalled(world, entity);

        ref TagCountContainer counts = ref world.Get<TagCountContainer>(entity);
        Assert.That(counts.GetCount(7), Is.EqualTo(1));
        Assert.That(counts.GetCount(11), Is.EqualTo(1));
    }

    [Test]
    public void EntityBuilder_AttributeEntity_PreinstallsDirtyFlags()
    {
        using var world = World.Create();
        var template = new EntityTemplate
        {
            Id = "attribute_entity",
            Components =
            {
                ["AttributeBuffer"] = JsonNode.Parse("{ \"base\": { \"Health\": 100 } }")!,
            },
        };
        var templates = new Dictionary<string, EntityTemplate>
        {
            [template.Id] = template,
        };

        Entity entity = new EntityBuilder(world, templates).UseTemplate(template.Id).Build();

        Assert.That(world.Has<AttributeBuffer>(entity), Is.True);
        Assert.That(world.Has<DirtyFlags>(entity), Is.True);
    }

    [Test]
    public void EntityBuilder_PositiveDurationTagAbility_PreinstallsCompleteTimedTagState()
    {
        using var world = World.Create();
        const int abilityId = 7001;
        var definitions = new AbilityDefinitionRegistry();
        var exec = new AbilityExecSpec();
        exec.SetItem(0, ExecItemKind.TagClip, tick: 0, durationTicks: 30, tagId: 41);
        definitions.Register(abilityId, new AbilityDefinition { ExecSpec = exec });

        var authoring = new ComponentAuthoringContext();
        authoring.Set("AbilityDefinitionRegistry", definitions);
        authoring.Set("AbilityFormSetRegistry", new AbilityFormSetRegistry());
        var template = new EntityTemplate
        {
            Id = "timed_tag_ability_actor",
            Components =
            {
                ["AbilityStateBuffer"] = JsonNode.Parse($"{{ \"abilityIds\": [{abilityId}] }}")!,
            },
        };
        var templates = new Dictionary<string, EntityTemplate>
        {
            [template.Id] = template,
        };

        Entity entity = new EntityBuilder(world, templates, authoring)
            .UseTemplate(template.Id)
            .Build();

        Assert.That(world.Has<GameplayTagContainer>(entity), Is.True);
        Assert.That(world.Has<TagCountContainer>(entity), Is.True);
        Assert.That(world.Has<DirtyFlags>(entity), Is.True);
        Assert.That(world.Has<TimedTagBuffer>(entity), Is.True);
    }

    [Test]
    public void EntityBuilder_PositiveDurationTargetTagAbility_DoesNotInstallTargetStateOnOwner()
    {
        using var world = World.Create();
        const int abilityId = 7007;
        var definitions = new AbilityDefinitionRegistry();
        var exec = new AbilityExecSpec();
        exec.SetItem(0, ExecItemKind.TagClipTarget, tick: 0, durationTicks: 30, tagId: 46);
        definitions.Register(abilityId, new AbilityDefinition { ExecSpec = exec });

        var authoring = new ComponentAuthoringContext();
        authoring.Set(ComponentAuthoringServiceKeys.AbilityDefinitionRegistry, definitions);
        var template = new EntityTemplate
        {
            Id = "timed_target_tag_ability_actor",
            Components =
            {
                ["AbilityStateBuffer"] = JsonNode.Parse($"{{ \"abilityIds\": [{abilityId}] }}")!,
            },
        };

        Entity entity = new EntityBuilder(
                world,
                new Dictionary<string, EntityTemplate> { [template.Id] = template },
                authoring)
            .UseTemplate(template.Id)
            .Build();

        Assert.That(world.Has<GameplayTagContainer>(entity), Is.False);
        Assert.That(world.Has<TagCountContainer>(entity), Is.False);
        Assert.That(world.Has<DirtyFlags>(entity), Is.False);
        Assert.That(world.Has<TimedTagBuffer>(entity), Is.False);
    }

    [Test]
    public void EntityBuilder_InstantTagAbility_PreinstallsTagStateWithoutTimedBuffer()
    {
        using var world = World.Create();
        const int abilityId = 7006;
        var definitions = new AbilityDefinitionRegistry();
        var exec = new AbilityExecSpec();
        exec.SetItem(0, ExecItemKind.TagSignal, tick: 0, tagId: 45);
        definitions.Register(abilityId, new AbilityDefinition { ExecSpec = exec });

        var authoring = new ComponentAuthoringContext();
        authoring.Set(ComponentAuthoringServiceKeys.AbilityDefinitionRegistry, definitions);
        var template = new EntityTemplate
        {
            Id = "instant_tag_ability_actor",
            Components =
            {
                ["AbilityStateBuffer"] = JsonNode.Parse($"{{ \"abilityIds\": [{abilityId}] }}")!,
            },
        };

        Entity entity = new EntityBuilder(
                world,
                new Dictionary<string, EntityTemplate> { [template.Id] = template },
                authoring)
            .UseTemplate(template.Id)
            .Build();

        Assert.That(world.Has<GameplayTagContainer>(entity), Is.True);
        Assert.That(world.Has<TagCountContainer>(entity), Is.True);
        Assert.That(world.Has<DirtyFlags>(entity), Is.True);
        Assert.That(world.Has<TimedTagBuffer>(entity), Is.False);
    }

    [Test]
    public void EntityBuilder_TargetTagSignalAbility_DoesNotInstallTargetStateOnOwner()
    {
        using var world = World.Create();
        const int abilityId = 7008;
        var definitions = new AbilityDefinitionRegistry();
        var exec = new AbilityExecSpec();
        exec.SetItem(0, ExecItemKind.TagSignalTarget, tick: 0, tagId: 47);
        definitions.Register(abilityId, new AbilityDefinition { ExecSpec = exec });

        var authoring = new ComponentAuthoringContext();
        authoring.Set(ComponentAuthoringServiceKeys.AbilityDefinitionRegistry, definitions);
        var template = new EntityTemplate
        {
            Id = "target_tag_signal_ability_actor",
            Components =
            {
                ["AbilityStateBuffer"] = JsonNode.Parse($"{{ \"abilityIds\": [{abilityId}] }}")!,
            },
        };

        Entity entity = new EntityBuilder(
                world,
                new Dictionary<string, EntityTemplate> { [template.Id] = template },
                authoring)
            .UseTemplate(template.Id)
            .Build();

        Assert.That(world.Has<GameplayTagContainer>(entity), Is.False);
        Assert.That(world.Has<TagCountContainer>(entity), Is.False);
        Assert.That(world.Has<DirtyFlags>(entity), Is.False);
        Assert.That(world.Has<TimedTagBuffer>(entity), Is.False);
    }

    [TestCase(ExecItemKind.TagClipTarget, 30, 1)]
    [TestCase(ExecItemKind.TagSignalTarget, 0, 0)]
    public void AbilityExec_TargetTagInstruction_WritesToIndependentlyAuthoredGrantReceiver(
        ExecItemKind kind,
        int durationTicks,
        int expectedTimedGrantCount)
    {
        using var world = World.Create();
        const int abilityId = 7009;
        const int grantedTagId = 48;
        var definitions = new AbilityDefinitionRegistry();
        var exec = new AbilityExecSpec { ClockId = GasClockId.Step };
        exec.SetItem(
            0,
            kind,
            tick: 0,
            durationTicks: durationTicks,
            clockId: GasClockId.Step,
            tagId: grantedTagId);
        exec.SetItem(1, ExecItemKind.End, tick: 0);
        definitions.Register(abilityId, new AbilityDefinition { ExecSpec = exec });

        var targetTemplate = new EntityTemplate
        {
            Id = "ability_tag_grant_receiver",
            Components =
            {
                ["AbilityTagGrantReceiver"] = JsonNode.Parse("{}")!,
            },
        };
        Entity target = new EntityBuilder(
                world,
                new Dictionary<string, EntityTemplate> { [targetTemplate.Id] = targetTemplate })
            .UseTemplate(targetTemplate.Id)
            .Build();
        AbilityStateBuffer abilities = default;
        abilities.AddAbility(abilityId);
        Entity actor = world.Create(
            abilities,
            new AbilityExecInstance
            {
                AbilitySlot = 0,
                AbilityId = abilityId,
                State = AbilityExecRunState.Running,
                ActiveClockId = GasClockId.Step,
                Target = target,
            });
        var dirtyEntities = new DirtyEntityQueue(capacity: 8);
        var tagOps = new TagOps(dirtyEntities, new TagRuleRegistry(), new GasBudget());
        var system = new AbilityExecSystem(
            world,
            new DiscreteClock(),
            new InputRequestQueue(),
            new InputResponseBuffer(),
            new EffectRequestQueue(),
            snapshotCapacity: 8,
            abilityDefinitions: definitions,
            tagOps: tagOps);

        Assert.DoesNotThrow(() => system.Update(0f));

        Assert.That(world.Get<GameplayTagContainer>(target).HasTag(grantedTagId), Is.True);
        Assert.That(world.Get<TagCountContainer>(target).GetCount(grantedTagId), Is.EqualTo(1));
        Assert.That(world.Get<TimedTagBuffer>(target).Count, Is.EqualTo(expectedTimedGrantCount));
        Assert.That(world.Has<TimedTagBuffer>(actor), Is.False);
    }

    [Test]
    public void TemplateBatchSpawner_AbilityTagGrantReceiver_InstallsCompleteReceiverStateInArchetype()
    {
        using var world = World.Create();
        var template = new EntityTemplate
        {
            Id = "batch_ability_tag_grant_receiver",
            Components =
            {
                ["Name"] = JsonNode.Parse("{ \"Value\": \"BatchAbilityTagGrantReceiver\" }")!,
                ["WorldPositionCm"] = JsonNode.Parse("{ \"Value\": { \"X\": 0, \"Y\": 0 } }")!,
                ["FacingDirection"] = JsonNode.Parse("{ \"AngleRad\": 0 }")!,
                ["AbilityTagGrantReceiver"] = JsonNode.Parse("{}")!,
            },
        };
        var spawner = new TemplateEntityBatchSpawner(
            world,
            new EntityTemplateKeyRegistry(),
            scratchCapacity: 1);
        var requests = new[]
        {
            new TemplateEntityBatchSpawner.TemplateBatchSpawnRequest(default, hasWorldPosition: false),
        };

        bool created = spawner.TryCreateBatch(
            template.Id,
            template,
            requests,
            TemplateBatchSpawnFeatures.None,
            out ReadOnlySpan<Entity> entities);

        Assert.That(created, Is.True);
        Assert.That(entities.Length, Is.EqualTo(1));
        Assert.That(world.Has<AbilityTagGrantReceiver>(entities[0]), Is.True);
        Assert.That(world.Has<GameplayTagContainer>(entities[0]), Is.True);
        Assert.That(world.Has<TagCountContainer>(entities[0]), Is.True);
        Assert.That(world.Has<DirtyFlags>(entities[0]), Is.True);
        Assert.That(world.Has<TimedTagBuffer>(entities[0]), Is.True);
    }

    [Test]
    public void TemplateBatchSpawner_TagOnlyTemplate_MatchesScalarRequiredStateAssembly()
    {
        using var world = World.Create();
        var template = new EntityTemplate
        {
            Id = "batch_tag_only",
            Components =
            {
                ["Name"] = JsonNode.Parse("{ \"Value\": \"BatchTagOnly\" }")!,
                ["WorldPositionCm"] = JsonNode.Parse("{ \"Value\": { \"X\": 0, \"Y\": 0 } }")!,
                ["FacingDirection"] = JsonNode.Parse("{ \"AngleRad\": 0 }")!,
                ["GameplayTagContainer"] = JsonNode.Parse("{}")!,
            },
        };
        Entity scalar = new EntityBuilder(
                world,
                new Dictionary<string, EntityTemplate> { [template.Id] = template })
            .UseTemplate(template.Id)
            .Build();
        var spawner = new TemplateEntityBatchSpawner(
            world,
            new EntityTemplateKeyRegistry(),
            scratchCapacity: 1);

        bool created = spawner.TryCreateBatch(
            template.Id,
            template,
            new[] { new TemplateEntityBatchSpawner.TemplateBatchSpawnRequest(default, hasWorldPosition: false) },
            TemplateBatchSpawnFeatures.None,
            out ReadOnlySpan<Entity> entities);

        Assert.That(created, Is.True);
        Assert.That(entities.Length, Is.EqualTo(1));
        Entity batched = entities[0];
        Assert.That(world.Has<GameplayTagContainer>(batched), Is.EqualTo(world.Has<GameplayTagContainer>(scalar)));
        Assert.That(world.Has<TagCountContainer>(batched), Is.EqualTo(world.Has<TagCountContainer>(scalar)));
        Assert.That(world.Has<DirtyFlags>(batched), Is.EqualTo(world.Has<DirtyFlags>(scalar)));
        Assert.That(world.Has<TimedTagBuffer>(batched), Is.EqualTo(world.Has<TimedTagBuffer>(scalar)));
        Assert.That(world.Has<BlackboardIntBuffer>(batched), Is.EqualTo(world.Has<BlackboardIntBuffer>(scalar)));
        Assert.That(world.Has<BlackboardSpatialBuffer>(batched), Is.EqualTo(world.Has<BlackboardSpatialBuffer>(scalar)));
        Assert.That(world.Has<BlackboardEntityBuffer>(batched), Is.EqualTo(world.Has<BlackboardEntityBuffer>(scalar)));
    }

    [Test]
    public void EntityBuilder_FormOverrideWithPositiveDurationTagAbility_PreinstallsTimedTagState()
    {
        using var world = World.Create();
        const int baseAbilityId = 7002;
        const int formAbilityId = 7003;
        var definitions = new AbilityDefinitionRegistry();
        definitions.Register(baseAbilityId, new AbilityDefinition());
        var formExec = new AbilityExecSpec();
        formExec.SetItem(0, ExecItemKind.TagClip, tick: 0, durationTicks: 12, tagId: 42);
        definitions.Register(formAbilityId, new AbilityDefinition { ExecSpec = formExec });

        int formSetId = AbilityFormSetIdRegistry.Register($"tag-state.form-set.{Guid.NewGuid():N}");
        var formSets = new AbilityFormSetRegistry();
        formSets.Register(
            formSetId,
            new AbilityFormSetDefinition(
                new[]
                {
                    new AbilityFormRouteDefinition(
                        default,
                        default,
                        priority: 1,
                        new[] { new AbilityFormSlotOverride(slotIndex: 0, abilityId: formAbilityId) }),
                }));

        var authoring = new ComponentAuthoringContext();
        authoring.Set("AbilityDefinitionRegistry", definitions);
        authoring.Set("AbilityFormSetRegistry", formSets);
        string formSetName = AbilityFormSetIdRegistry.GetName(formSetId);
        var template = new EntityTemplate
        {
            Id = "timed_tag_form_actor",
            Components =
            {
                ["AbilityStateBuffer"] = JsonNode.Parse($"{{ \"abilityIds\": [{baseAbilityId}] }}")!,
                ["AbilityFormSetRef"] = JsonNode.Parse($"{{ \"formSetId\": \"{formSetName}\" }}")!,
            },
        };

        Entity entity = new EntityBuilder(
                world,
                new Dictionary<string, EntityTemplate> { [template.Id] = template },
                authoring)
            .UseTemplate(template.Id)
            .Build();

        Assert.That(world.Has<TimedTagBuffer>(entity), Is.True);
    }

    [Test]
    public void EquipmentAbilityGrant_PreinstallsTimedTagStateBeforeGrantBecomesVisible()
    {
        using var world = World.Create();
        const int abilityId = 7005;
        var abilityDefinitions = new AbilityDefinitionRegistry();
        var exec = new AbilityExecSpec();
        exec.SetItem(0, ExecItemKind.TagClip, tick: 0, durationTicks: 24, tagId: 44);
        abilityDefinitions.Register(abilityId, new AbilityDefinition { ExecSpec = exec });

        var relationshipTypes = new RelationshipTypeRegistry();
        var relationshipMetrics = new RelationshipMetricRegistry();
        var relationshipFlags = new RelationshipFlagRegistry();
        var relationshipBands = new RelationshipBandRegistry();
        var relationshipReasons = new RelationshipReasonRegistry();
        var relationships = new RelationshipRuntime(
            world,
            relationshipTypes,
            relationshipMetrics,
            relationshipFlags,
            relationshipBands,
            new RelationshipChangeBuffer(capacity: 8),
            new RelationshipReverseIndex(world));
        RelationshipCatalogInstaller.RegisterCatalog(
            new RelationshipCatalogConfig
            {
                Types = { new RelationshipTypeConfig { Id = "Owns" } },
            },
            relationshipTypes,
            relationshipMetrics,
            relationshipFlags,
            relationshipBands,
            relationshipReasons);
        var ownership = new OwnershipResolver(relationships, relationshipTypes.GetId("Owns"));

        var shapes = new ItemShapeRegistry();
        int shapeId = shapes.Register("tag_state_1x1", new ItemShapeDefinition
        {
            Id = "tag_state_1x1",
            Rotations = new[] { new ItemShapeRotation(1, 1, new[] { true }) },
        });
        var layouts = new ItemLayoutRegistry();
        int layoutId = layouts.Register("tag_state_equipment", new ItemLayoutDefinition
        {
            Id = "tag_state_equipment",
            Purpose = ItemContainerPurpose.Equipment,
            Width = 1,
            Height = 1,
            GrantsEquipmentBonuses = true,
        }.InitializeBlockedMask(new bool[1]));
        var itemDefinitions = new ItemDefinitionRegistry();
        int itemDefinitionId = itemDefinitions.Register("tag_state_timed_grant", new ItemDefinition
        {
            Id = "tag_state_timed_grant",
            ShapeId = shapeId,
            AbilityGrants = new[] { new ItemAbilityGrant { SlotIndex = 0, AbilityId = abilityId } },
        });
        var inventory = new InventoryRuntimeService(world, shapes, layouts, itemDefinitions, ownership);

        Entity actor = world.Create(new AbilityStateBuffer(), new InventoryEquipmentDirtyTag());
        Entity equipment = inventory.CreateContainer(actor, layoutId, ItemContainerPurpose.Equipment);
        Entity item = inventory.CreateItem(itemDefinitionId);
        Assert.That(inventory.TryMoveItemToGrid(item, equipment, 0, 0), Is.True);

        var system = new InventoryEquipmentGrantSyncSystem(
            world,
            inventory,
            new EffectRequestQueue(),
            abilityDefinitions);
        system.Update(0f);

        Assert.That(world.Has<ItemGrantedSlotBuffer>(actor), Is.True);
        Assert.That(world.Get<ItemGrantedSlotBuffer>(actor).GetOverride(0).AbilityId, Is.EqualTo(abilityId));
        Assert.That(world.Has<GameplayTagContainer>(actor), Is.True);
        Assert.That(world.Has<TagCountContainer>(actor), Is.True);
        Assert.That(world.Has<DirtyFlags>(actor), Is.True);
        Assert.That(world.Has<TimedTagBuffer>(actor), Is.True);
    }

    [Test]
    public void TemplateBatchSpawner_ExplicitDirtyFlagsWithoutTags_IsNotSilentlyDropped()
    {
        using var world = World.Create();
        var template = new EntityTemplate
        {
            Id = "dirty_only",
            Components =
            {
                ["Name"] = JsonNode.Parse("{ \"Value\": \"DirtyOnly\" }")!,
                ["WorldPositionCm"] = JsonNode.Parse("{ \"Value\": { \"X\": 0, \"Y\": 0 } }")!,
                ["FacingDirection"] = JsonNode.Parse("{ \"AngleRad\": 0 }")!,
                ["DirtyFlags"] = JsonNode.Parse("{}")!,
            },
        };
        var spawner = new TemplateEntityBatchSpawner(
            world,
            new EntityTemplateKeyRegistry(),
            scratchCapacity: 1);
        var requests = new[]
        {
            new TemplateEntityBatchSpawner.TemplateBatchSpawnRequest(default, hasWorldPosition: false),
        };

        bool created = spawner.TryCreateBatch(
            template.Id,
            template,
            requests,
            TemplateBatchSpawnFeatures.None,
            out ReadOnlySpan<Entity> entities);

        Assert.That(created, Is.True);
        Assert.That(entities.Length, Is.EqualTo(1));
        Assert.That(world.Has<DirtyFlags>(entities[0]), Is.True);
    }

    [Test]
    public void TemplateBatchSpawner_AbilityTemplate_MustUseScalarAssemblyContract()
    {
        using var world = World.Create();
        var template = new EntityTemplate
        {
            Id = "ability_template",
            Components =
            {
                ["AbilityStateBuffer"] = JsonNode.Parse("{ \"abilityIds\": [7001] }")!,
            },
        };
        var spawner = new TemplateEntityBatchSpawner(
            world,
            new EntityTemplateKeyRegistry(),
            scratchCapacity: 1);

        Assert.That(spawner.IsBatchCompatible(template.Id, template), Is.False);
    }
}
