using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Registry;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS.Config;

[TestFixture]
[NonParallelizable]
public sealed class ResponseChainListenerAuthoringTests
{
    private int _spellCategory;
    private int _burnCategory;
    private int _counterTemplate;

    [SetUp]
    public void SetUp()
    {
        ModRegistryAmbient.Reset();
        ModRegistryAmbient.Bind(new ModRegistrySet());
        _spellCategory = EffectCategoryRegistry.Register("Effect.Test.Spell");
        _burnCategory = EffectCategoryRegistry.Register("Effect.Test.Burn");
        _counterTemplate = EffectTemplateIdRegistry.Register("Effect.Test.Counter");
    }

    [TearDown]
    public void TearDown() => ModRegistryAmbient.Reset();

    [Test]
    public unsafe void TemplateDeclaresTrapAndShield_ResolvesNamesIntoTheListener()
    {
        using World world = World.Create();
        Entity hero = world.Create();

        Ludots.Core.Config.ComponentRegistry.Apply(hero, "ResponseChainListener", JsonNode.Parse("""
            {
              "responses": [
                { "category": "Effect.Test.Spell", "type": "PromptInput", "priority": 100, "effect": "Effect.Test.Counter" },
                { "category": "Effect.Test.Burn", "type": "Modify", "priority": 10, "modifyValue": 0.5, "modifyOp": "Multiply" },
                { "category": "Effect.Test.Burn", "type": "Hook", "priority": 1 }
              ]
            }
            """)!);

        ref ResponseChainListener listener = ref world.Get<ResponseChainListener>(hero);
        That(listener.Count, Is.EqualTo(3));
        That(listener.EffectCategoryIds[0], Is.EqualTo(_spellCategory));
        That((ResponseType)listener.ResponseTypes[0], Is.EqualTo(ResponseType.PromptInput));
        That(listener.Priorities[0], Is.EqualTo(100));
        That(listener.EffectTemplateIds[0], Is.EqualTo(_counterTemplate));
        That(listener.EffectCategoryIds[1], Is.EqualTo(_burnCategory));
        That((ResponseType)listener.ResponseTypes[1], Is.EqualTo(ResponseType.Modify));
        That(listener.ModifyValues[1], Is.EqualTo(0.5f));
        That((ModifierOp)listener.ModifyOps[1], Is.EqualTo(ModifierOp.Multiply));
        That((ResponseType)listener.ResponseTypes[2], Is.EqualTo(ResponseType.Hook));
    }

    [TestCase("""{ "responses": [] }""", "1..8")]
    [TestCase("""{ "responses": [ { "category": "Effect.Test.Missing", "type": "Hook", "priority": 1 } ] }""", "unknown effect category 'Effect.Test.Missing'")]
    [TestCase("""{ "responses": [ { "category": "Effect.Test.Spell", "type": "Counter", "priority": 1 } ] }""", "type 'Counter' is unknown")]
    [TestCase("""{ "responses": [ { "category": "Effect.Test.Spell", "type": "PromptInput", "priority": 1 } ] }""", "requires explicit 'effect'")]
    [TestCase("""{ "responses": [ { "category": "Effect.Test.Spell", "type": "Chain", "priority": 1, "effect": "Effect.Test.Nope" } ] }""", "unknown effect template 'Effect.Test.Nope'")]
    [TestCase("""{ "responses": [ { "category": "Effect.Test.Spell", "type": "Hook", "priority": 1, "effect": "Effect.Test.Counter" } ] }""", "must not author 'effect'")]
    [TestCase("""{ "responses": [ { "category": "Effect.Test.Spell", "type": "Chain", "priority": 1, "effect": "Effect.Test.Counter", "modifyValue": 1 } ] }""", "must not author 'modifyValue'")]
    [TestCase("""{ "responses": [ { "category": "Effect.Test.Spell", "type": "Modify", "priority": 1, "modifyValue": 1, "modifyOp": "Subtract" } ] }""", "modifyOp 'Subtract' is unknown")]
    [TestCase("""{ "responses": [ { "category": "Effect.Test.Spell", "type": "Hook" } ] }""", "requires explicit 'priority'")]
    [TestCase("""{ "responses": [ { "category": "Effect.Test.Spell", "type": "Hook", "priority": 1, "graph": 3 } ] }""", "unsupported property 'graph'")]
    [TestCase("""{ "listeners": [] }""", "unsupported property 'listeners'")]
    public void InvalidAuthoring_FailsAtLoadAndNamesTheField(string json, string expected)
    {
        using World world = World.Create();
        Entity hero = world.Create();

        var error = Throws<System.InvalidOperationException>(
            () => Ludots.Core.Config.ComponentRegistry.Apply(hero, "ResponseChainListener", JsonNode.Parse(json)!));

        That(error!.Message, Does.Contain(expected));
        That(world.Has<ResponseChainListener>(hero), Is.False);
    }

    [Test]
    public void MoreResponsesThanCapacity_FailsAtLoad()
    {
        var responses = new JsonArray();
        for (int i = 0; i <= ResponseChainListener.CAPACITY; i++)
        {
            responses.Add(JsonNode.Parse("""{ "category": "Effect.Test.Spell", "type": "Hook", "priority": 1 }"""));
        }

        using World world = World.Create();
        Entity hero = world.Create();

        var error = Throws<System.InvalidOperationException>(
            () => Ludots.Core.Config.ComponentRegistry.Apply(hero, "ResponseChainListener", new JsonObject { ["responses"] = responses }));

        That(error!.Message, Does.Contain("1..8"));
    }

    [Test]
    public void ListenerAddedOrRemovedOutsideListenerOps_StillRefreshesTheResponseCache()
    {
        using World world = World.Create();
        var queue = new EffectRequestQueue();
        queue.TrackResponseChainListenerLifecycle(world);
        Entity hero = world.Create();
        int before = queue.ResponseChainListenerRevision;

        Ludots.Core.Config.ComponentRegistry.Apply(hero, "ResponseChainListener", JsonNode.Parse("""
            { "responses": [ { "category": "Effect.Test.Spell", "type": "Hook", "priority": 1 } ] }
            """)!);
        int afterAdd = queue.ResponseChainListenerRevision;
        world.Remove<ResponseChainListener>(hero);
        int afterRemove = queue.ResponseChainListenerRevision;

        That(afterAdd, Is.GreaterThan(before), "a template-spawned listener must be seen by the next response window");
        That(afterRemove, Is.GreaterThan(afterAdd));
    }
}
