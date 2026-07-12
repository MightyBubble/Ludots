using System.Text.Json.Nodes;
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
