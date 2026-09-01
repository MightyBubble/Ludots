using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Networking.Replication;
using NUnit.Framework;
using System.Text.Json.Nodes;

namespace Ludots.Tests.GAS.Networking;

[TestFixture]
public sealed class ReplicationSchemaRefAuthoringTests
{
    [Test]
    public void ComponentRegistry_AuthorsPositiveReplicationSchemaId()
    {
        using World world = World.Create();
        Entity entity = world.Create();

        Ludots.Core.Config.ComponentRegistry.Apply(
            entity,
            "ReplicationSchemaRef",
            JsonNode.Parse("""{ "SchemaId": 701 }""")!);

        Assert.That(world.Get<ReplicationSchemaRef>(entity).SchemaId, Is.EqualTo(701));
    }

    [TestCase("{}")]
    [TestCase("{ \"SchemaId\": 0 }")]
    [TestCase("{ \"SchemaId\": -1 }")]
    [TestCase("{ \"SchemaId\": \"701\" }")]
    [TestCase("{ \"SchemaId\": 701, \"Extra\": true }")]
    public void ComponentRegistry_RejectsInvalidReplicationSchemaAuthoring(string json)
    {
        using World world = World.Create();
        Entity entity = world.Create();

        Assert.That(
            () => Ludots.Core.Config.ComponentRegistry.Apply(entity, "ReplicationSchemaRef", JsonNode.Parse(json)!),
            Throws.Exception);
        Assert.That(world.Has<ReplicationSchemaRef>(entity), Is.False);
    }
}
