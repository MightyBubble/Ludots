using System.Text.Json.Nodes;
using Ludots.Core.Config;
using NUnit.Framework;

namespace Ludots.Tests.GasTests.Config;

[TestFixture]
public sealed class DataSchemaProjectionSessionTests
{
    [Test]
    public void TryPublishRecordDraft_ValidDraft_SwitchesPreviewAndBumpsRevision()
    {
        DataSchemaCatalog catalog = DataSchemaCatalog.Load("""
        [
          { "id": "label", "kind": "struct", "fields": [
            { "name": "text", "type": "string", "required": true }
          ] }
        ]
        """);
        DataSchemaRegistry startup = DataSchemaRegistry.Load(catalog, """
        [ { "id": "label.a", "schema": "label", "value": { "text": "A" } } ]
        """);
        var session = new DataSchemaProjectionSession(startup);
        uint before = session.Revision;

        Assert.That(
            session.TryPublishRecordDraft("label.a", "label", new JsonObject { ["text"] = "B" }, out string error),
            Is.True);
        Assert.That(error, Is.Empty);
        Assert.That(session.IsPreview, Is.True);
        Assert.That(session.Revision, Is.EqualTo(before + 1));
        Assert.That(session.TryGetNode("label.a", "text", out JsonNode? text), Is.True);
        Assert.That(text!.GetValue<string>(), Is.EqualTo("B"));
        Assert.That(startup.TryGetNode("label.a", "text", out JsonNode? startupText), Is.True);
        Assert.That(startupText!.GetValue<string>(), Is.EqualTo("A"));
    }

    [Test]
    public void TryPublishRecordDraft_InvalidDraft_LeavesActiveUnchanged()
    {
        DataSchemaCatalog catalog = DataSchemaCatalog.Load("""
        [
          { "id": "label", "kind": "struct", "fields": [
            { "name": "text", "type": "string", "required": true }
          ] }
        ]
        """);
        DataSchemaRegistry startup = DataSchemaRegistry.Load(catalog, """
        [ { "id": "label.a", "schema": "label", "value": { "text": "A" } } ]
        """);
        var session = new DataSchemaProjectionSession(startup);

        Assert.That(
            session.TryPublishRecordDraft("label.a", "label", new JsonObject(), out string error),
            Is.False);
        Assert.That(error, Does.Contain("text"));
        Assert.That(session.IsPreview, Is.False);
        Assert.That(session.TryGetNode("label.a", "text", out JsonNode? text), Is.True);
        Assert.That(text!.GetValue<string>(), Is.EqualTo("A"));
    }
}
