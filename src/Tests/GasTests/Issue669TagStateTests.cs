using System.Text.Json.Nodes;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Spawning;
using NUnit.Framework;

namespace Ludots.Tests.GAS;

[TestFixture]
public sealed class Issue669TagStateTests
{
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
        var tags = new GameplayTagContainer();
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
}
