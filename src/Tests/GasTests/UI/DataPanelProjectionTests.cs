using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.Registry;
using Ludots.Core.UI.PanelProjection;
using NUnit.Framework;

namespace Ludots.Tests.GasTests.UI;

[TestFixture]
public sealed class DataPanelProjectionTests
{
    [Test]
    public void DataPin_ReadsNestedRecordWithoutGraphOrSkinCoupling()
    {
        DataSchemaRegistry registry = CreateRegistry();
        PanelTemplate template = PanelTemplateLoader.Load("""
        {
          "id": "tests.panel.data",
          "pins": [
            { "name": "label", "source": "data", "record": "unit.scout", "path": "name" },
            { "name": "x", "source": "data", "record": "unit.scout", "path": "position.x" }
          ],
          "skin": "web"
        }
        """);

        using World world = World.Create();
        Entity owner = world.Create();
        var outputs = new GraphOutputValueStore(new StringIntRegistry(), initialCapacity: 8);
        PanelVariableSet values = new PanelInstance(
            template,
            owner).Evaluate(new PanelProjectionReader(world, outputs, registry));

        Assert.That(template.Graph, Is.Null);
        Assert.That(values.GetNode("label").GetValue<string>(), Is.EqualTo("Scout"));
        Assert.That(values.Get("x"), Is.EqualTo(12.5f));
        Assert.That(values.GetValue("label").ToString(), Is.EqualTo("Scout"));
        Assert.That(values.GetDisplayText("label"), Is.EqualTo("Scout"));
    }

    [Test]
    public void MixedGraphAndDataPins_ReadFromIndependentSources()
    {
        DataSchemaRegistry registry = CreateRegistry();
        PanelTemplate template = PanelTemplateLoader.Load("""
        {
          "id": "tests.panel.mixed",
          "graph": "tests.graph.mixed",
          "pins": [
            { "name": "score", "key": "panel.score" },
            { "name": "label", "source": "data", "record": "unit.scout", "path": "name" }
          ]
        }
        """);

        using World world = World.Create();
        Entity owner = world.Create();
        var keys = new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: System.StringComparer.Ordinal);
        var outputs = new GraphOutputValueStore(keys, initialCapacity: 8);
        outputs.SetFloat(owner, "panel.score", 42f);
        PanelVariableSet values = new PanelInstance(template, owner)
            .Evaluate(new PanelProjectionReader(world, outputs, registry));

        Assert.That(values.Get("score"), Is.EqualTo(42f));
        Assert.That(values.GetNode("label").GetValue<string>(), Is.EqualTo("Scout"));
    }

    [Test]
    public void DataPin_MissingPath_FailsWithContext()
    {
        DataSchemaRegistry registry = CreateRegistry();
        PanelTemplate template = PanelTemplateLoader.Load("""
        {
          "id": "tests.panel.missing",
          "pins": [ { "name": "bad", "source": "data", "record": "unit.scout", "path": "missing.value" } ]
        }
        """);
        using World world = World.Create();
        Entity owner = world.Create();
        var outputs = new GraphOutputValueStore(new StringIntRegistry(), initialCapacity: 8);

        Assert.That(
            () => new PanelInstance(template, owner).Evaluate(new PanelProjectionReader(world, outputs, registry)),
            Throws.InvalidOperationException.With.Message.Contains("missing.value"));
    }

    private static DataSchemaRegistry CreateRegistry()
    {
        DataSchemaCatalog catalog = DataSchemaCatalog.Load("""
        [
          { "id": "point", "kind": "struct", "fields": [
            { "name": "x", "type": "float", "required": true },
            { "name": "y", "type": "float", "required": true }
          ] },
          { "id": "unit", "kind": "struct", "fields": [
            { "name": "name", "type": "string", "required": true },
            { "name": "position", "type": { "kind": "struct", "ref": "point" }, "required": true }
          ] }
        ]
        """);
        return DataSchemaRegistry.Load(catalog, """
        [ { "id": "unit.scout", "schema": "unit", "value": {
          "name": "Scout", "position": { "x": 12.5, "y": -3 }
        } } ]
        """);
    }
}
