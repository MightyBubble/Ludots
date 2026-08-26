using System.Text.Json.Nodes;
using Ludots.Core.Config;
using NUnit.Framework;

namespace Ludots.Tests.GasTests.Config;

[TestFixture]
public sealed class DataSchemaModAssetWriterTests
{
    [Test]
    public void Save_ValidDraft_WritesSchemasRecordsAndPanels()
    {
        string root = Path.Combine(Path.GetTempPath(), "LudotsDataSchemaWriter", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var writer = new DataSchemaModAssetWriter();
            JsonArray schemas = JsonNode.Parse("""
            [ { "id": "label", "kind": "struct", "fields": [
              { "name": "text", "type": "string", "required": true }
            ] } ]
            """)!.AsArray();
            JsonArray records = JsonNode.Parse("""
            [ { "id": "label.a", "schema": "label", "value": { "text": "A" } } ]
            """)!.AsArray();
            JsonArray panels = JsonNode.Parse("""
            [ { "id": "panel.label", "pins": [
              { "name": "text", "source": "data", "record": "label.a", "path": "text" }
            ] } ]
            """)!.AsArray();

            DataSchemaModAssetWriteResult result = writer.Save(root, schemas, records, panels);
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.WrittenRelativePaths, Does.Contain(DataSchemaModAssetWriter.SchemasRelativePath));
            Assert.That(File.Exists(Path.Combine(root, "assets", "Data", "data_schemas.json")), Is.True);
            Assert.That(File.Exists(Path.Combine(root, "assets", "Panels", "panel_templates.json")), Is.True);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Test]
    public void Save_InvalidRecord_DoesNotWriteFiles()
    {
        string root = Path.Combine(Path.GetTempPath(), "LudotsDataSchemaWriter", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var writer = new DataSchemaModAssetWriter();
            JsonArray schemas = JsonNode.Parse("""
            [ { "id": "label", "kind": "struct", "fields": [
              { "name": "text", "type": "string", "required": true }
            ] } ]
            """)!.AsArray();
            JsonArray records = JsonNode.Parse("""
            [ { "id": "label.a", "schema": "label", "value": { } } ]
            """)!.AsArray();

            DataSchemaModAssetWriteResult result = writer.Save(root, schemas, records, panelTemplates: null);
            Assert.That(result.Succeeded, Is.False);
            Assert.That(result.Diagnostics, Is.Not.Empty);
            Assert.That(File.Exists(Path.Combine(root, "assets", "Data", "data_schemas.json")), Is.False);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
