using System;
using System.IO;
using Ludots.Core.Config;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.GasTests.Config;

[TestFixture]
public sealed class DataSchemaTests
{
    private const string Schemas = """
    [
      { "id": "rarity", "kind": "enum", "values": [
        { "name": "Common", "value": 1 }, { "name": "Rare", "value": 5 }
      ] },
      { "id": "point", "kind": "struct", "fields": [
        { "name": "x", "type": "float", "required": true },
        { "name": "y", "type": "float", "required": true }
      ] },
      { "id": "unit", "kind": "struct", "fields": [
        { "name": "name", "type": "string", "required": true },
        { "name": "position", "type": { "kind": "struct", "ref": "point" }, "required": true },
        { "name": "rarity", "type": { "kind": "enum", "ref": "rarity" }, "required": true },
        { "name": "tags", "type": { "kind": "array", "items": "string" }, "required": true }
      ] }
    ]
    """;

    [Test]
    public void Load_NestedStructArrayAndEnum_ProvidesValidatedRecordAndPath()
    {
        DataSchemaCatalog catalog = DataSchemaCatalog.Load(Schemas);
        DataSchemaRegistry registry = DataSchemaRegistry.Load(catalog, """
        [
          { "id": "unit.scout", "schema": "unit", "value": {
            "name": "Scout", "position": { "x": 12.5, "y": -3 },
            "rarity": "Rare", "tags": ["fast", "visible"]
          } }
        ]
        """);

        Assert.That(registry.TryGetNode("unit.scout", "position.x", out var x), Is.True);
        Assert.That(x!.GetValue<double>(), Is.EqualTo(12.5));
        Assert.That(registry.TryGetNode("unit.scout", "tags", out var tags), Is.True);
        Assert.That(tags!.AsArray().Count, Is.EqualTo(2));
    }

    [TestCase("unknown field")]
    [TestCase("missing required field")]
    [TestCase("unknown enum")]
    public void Load_InvalidRecord_FailsFast(string kind)
    {
        string value = kind switch
        {
            "unknown field" => "{ \"name\": \"Scout\", \"position\": { \"x\": 1, \"y\": 2 }, \"rarity\": \"Common\", \"tags\": [], \"extra\": true }",
            "missing required field" => "{ \"name\": \"Scout\", \"position\": { \"x\": 1, \"y\": 2 }, \"tags\": [] }",
            _ => "{ \"name\": \"Scout\", \"position\": { \"x\": 1, \"y\": 2 }, \"rarity\": \"Legendary\", \"tags\": [] }",
        };

        DataSchemaCatalog catalog = DataSchemaCatalog.Load(Schemas);
        Assert.That(
            () => DataSchemaRegistry.Load(catalog, $"[{{ \"id\": \"bad\", \"schema\": \"unit\", \"value\": {value} }}]"),
            Throws.InvalidOperationException);
    }

    [Test]
    public void Catalog_CyclicStructReference_IsRejected()
    {
        Assert.That(
            () => DataSchemaCatalog.Load("""
            [
              { "id": "a", "kind": "struct", "fields": [ { "name": "b", "type": { "kind": "struct", "ref": "b" } } ] },
              { "id": "b", "kind": "struct", "fields": [ { "name": "a", "type": { "kind": "struct", "ref": "a" } } ] }
            ]
            """),
            Throws.InvalidOperationException.With.Message.Contains("Cyclic"));
    }

    [Test]
    public void Catalog_DuplicateEnumValue_IsRejected()
    {
        Assert.That(
            () => DataSchemaCatalog.Load("""
            [ { "id": "state", "kind": "enum", "values": [
              { "name": "A", "value": 1 }, { "name": "B", "value": 1 }
            ] } ]
            """),
            Throws.InvalidOperationException.With.Message.Contains("duplicate"));
    }

    [Test]
    public void ConfigLoader_MergesSchemaAndRecordsThroughConfigPipeline()
    {
        string root = Path.Combine(Path.GetTempPath(), "LudotsDataSchema", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Core"));
        try
        {
            File.WriteAllText(Path.Combine(root, "Core", "config_catalog.json"), """
            [
              { "Path": "Data/data_schemas.json", "Policy": "ArrayById", "IdField": "id" },
              { "Path": "Data/data_records.json", "Policy": "ArrayById", "IdField": "id" }
            ]
            """);
            Directory.CreateDirectory(Path.Combine(root, "Core", "Data"));
            File.WriteAllText(Path.Combine(root, "Core", "Data", "data_schemas.json"), """
            [ { "id": "label", "kind": "struct", "fields": [
              { "name": "text", "type": "string", "required": true }
            ] } ]
            """);
            File.WriteAllText(Path.Combine(root, "Core", "Data", "data_records.json"), """
            [ { "id": "label.scout", "schema": "label", "value": { "text": "Scout" } } ]
            """ );

            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", Path.Combine(root, "Core"));
            var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
            var pipeline = new ConfigPipeline(vfs, modLoader);
            ConfigCatalog catalog = ConfigCatalogLoader.Load(pipeline);
            DataSchemaRegistry registry = new DataSchemaConfigLoader(pipeline).Load(catalog);

            Assert.That(registry.TryGetNode("label.scout", "text", out var text), Is.True);
            Assert.That(text!.GetValue<string>(), Is.EqualTo("Scout"));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
