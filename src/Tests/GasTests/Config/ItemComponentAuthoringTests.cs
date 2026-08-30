using System;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.Items;
using NUnit.Framework;
using ComponentRegistry = Ludots.Core.Config.ComponentRegistry;

namespace Ludots.Tests.GAS.Config;

[TestFixture]
public sealed class ItemComponentAuthoringTests
{
    [Test]
    public void ItemInstanceCm_ResolvesDefinitionStringThroughAuthoringContext()
    {
        using World world = World.Create();
        Entity entity = world.Create();
        var definitions = new ItemDefinitionRegistry();
        int expectedDefinitionId = definitions.Register(
            "Item.Test.Potion",
            new ItemDefinition { Id = "Item.Test.Potion", DisplayName = "Potion", MaxStack = 10 });
        var context = new ComponentAuthoringContext();
        context.Set(ComponentAuthoringServiceKeys.ItemDefinitionRegistry, definitions);

        ComponentRegistry.Apply(
            entity,
            "ItemInstanceCm",
            JsonNode.Parse("""{ "definitionId": "Item.Test.Potion", "stackCount": 3 }""")!,
            context);

        ItemInstanceCm item = world.Get<ItemInstanceCm>(entity);
        Assert.That(item.DefinitionId, Is.EqualTo(expectedDefinitionId));
        Assert.That(item.StackCount, Is.EqualTo(3));
    }

    [TestCase("""{ "definitionId": 1 }""")]
    [TestCase("""{ "definitionId": "Item.Missing" }""")]
    public void ItemInstanceCm_RejectsUnresolvedDefinitionAuthoring(string json)
    {
        using World world = World.Create();
        Entity entity = world.Create();
        var context = new ComponentAuthoringContext();
        context.Set(ComponentAuthoringServiceKeys.ItemDefinitionRegistry, new ItemDefinitionRegistry());

        Assert.That(
            () => ComponentRegistry.Apply(entity, "ItemInstanceCm", JsonNode.Parse(json)!, context),
            Throws.TypeOf<InvalidOperationException>());
        Assert.That(world.Has<ItemInstanceCm>(entity), Is.False);
    }

    [Test]
    public void ItemInstanceCm_RequiresDefinitionRegistryInAuthoringContext()
    {
        using World world = World.Create();
        Entity entity = world.Create();

        Assert.That(
            () => ComponentRegistry.Apply(
                entity,
                "ItemInstanceCm",
                JsonNode.Parse("""{ "definitionId": "Item.Test.Potion" }""")!),
            Throws.TypeOf<InvalidOperationException>());
        Assert.That(world.Has<ItemInstanceCm>(entity), Is.False);
    }
}
