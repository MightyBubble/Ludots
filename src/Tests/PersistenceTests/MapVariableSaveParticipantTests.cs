using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.MapTriggers;
using Ludots.Core.Map;
using Ludots.Core.Persistence;
using NUnit.Framework;

namespace Ludots.Tests.Persistence;

[TestFixture]
public sealed class MapVariableSaveParticipantTests
{
    private const string MapIdValue = "mapvar_save_probe";

    private static MapConfig CreateConfig()
    {
        return new MapConfig
        {
            Variables =
            {
                new MapVariableDeclaration { Name = "phase", Type = MapVariableType.Int, Initial = 0 },
                new MapVariableDeclaration { Name = "kills", Type = MapVariableType.Int, Initial = 0 },
                new MapVariableDeclaration { Name = "ammo", Type = MapVariableType.Float, Initial = 1.5 },
            }
        };
    }

    private static MapSessionManager CreateManagerWithSession()
    {
        var manager = new MapSessionManager();
        manager.CreateSession(new MapId(MapIdValue), CreateConfig());
        manager.PushFocused(new MapId(MapIdValue));
        return manager;
    }

    private static JsonObject CaptureDomain(MapSessionManager manager)
    {
        return CoreSaveParticipants.CreateMapSessionsParticipant(manager).CaptureState().AsObject();
    }

    private static JsonObject SessionVariables(JsonObject domain)
    {
        return domain["sessions"]!.AsArray()[0]!.AsObject()["variables"]!.AsObject();
    }

    [Test]
    public void RoundTrip_RestoresValuesExactly_AndKeepsRevisionsAtZero()
    {
        MapSessionManager source = CreateManagerWithSession();
        MapVariableStore sourceStore = source.FocusedSession!.Variables!;
        sourceStore.WriteInt("phase", 2);
        sourceStore.WriteInt("kills", 7);
        sourceStore.WriteFloat("ammo", 3.25f);

        JsonObject domain = CaptureDomain(source);

        MapSessionManager target = CreateManagerWithSession();
        MapVariableStore targetStore = target.FocusedSession!.Variables!;
        CoreSaveParticipants.CreateMapSessionsParticipant(target).RestoreState(domain);

        Assert.That(targetStore.ReadInt("phase"), Is.EqualTo(2));
        Assert.That(targetStore.ReadInt("kills"), Is.EqualTo(7));
        Assert.That(targetStore.ReadFloat("ammo"), Is.EqualTo(3.25f));
        Assert.That(targetStore.GetRevision("phase"), Is.EqualTo(0u), "revisions are not persisted; restore leaves them at zero");
        Assert.That(targetStore.GetRevision("kills"), Is.EqualTo(0u));
        Assert.That(targetStore.GetRevision("ammo"), Is.EqualTo(0u));
    }

    [Test]
    public void Restore_DoesNotFireVariableChanged_FirstWriteDiffsAgainstRestoredValue()
    {
        MapSessionManager source = CreateManagerWithSession();
        source.FocusedSession!.Variables!.WriteInt("phase", 2);
        JsonObject domain = CaptureDomain(source);

        MapSessionManager target = CreateManagerWithSession();
        MapVariableStore targetStore = target.FocusedSession!.Variables!;
        var changes = new List<(string Name, int Value)>();
        targetStore.VariableChangedDispatcher = (_, name, type, oldInt, newInt, _, _) =>
        {
            if (type == MapVariableType.Int)
            {
                changes.Add((name, newInt));
            }
        };

        CoreSaveParticipants.CreateMapSessionsParticipant(target).RestoreState(domain);

        Assert.That(changes, Is.Empty, "restore must not dispatch VariableChanged");

        targetStore.WriteInt("phase", 2);
        Assert.That(changes, Is.Empty, "same-value write after restore must not fire");

        targetStore.WriteInt("phase", 3);
        Assert.That(changes, Is.EqualTo(new[] { ("phase", 3) }));
    }

    [Test]
    public void Restore_MissingDeclaredVariable_RejectedNamingVariable()
    {
        MapSessionManager source = CreateManagerWithSession();
        JsonObject domain = CaptureDomain(source);
        SessionVariables(domain).Remove("kills");

        MapSessionManager target = CreateManagerWithSession();
        MapVariableStore targetStore = target.FocusedSession!.Variables!;

        SaveContextException? error = Assert.Throws<SaveContextException>(
            () => CoreSaveParticipants.CreateMapSessionsParticipant(target).RestoreState(domain));

        Assert.That(error!.Message, Does.Contain("kills"));
        Assert.That(error.Message, Does.Contain(MapIdValue));
        Assert.That(targetStore.ReadInt("phase"), Is.EqualTo(0), "rejected restore must not mutate values");
        Assert.That(targetStore.ReadFloat("ammo"), Is.EqualTo(1.5f));
    }

    [Test]
    public void Restore_UndeclaredVariable_RejectedNamingVariable()
    {
        MapSessionManager source = CreateManagerWithSession();
        JsonObject domain = CaptureDomain(source);
        SessionVariables(domain)["smuggled"] = new JsonObject
        {
            ["type"] = MapVariableType.Int.ToString(),
            ["value"] = 1
        };

        MapSessionManager target = CreateManagerWithSession();

        SaveContextException? error = Assert.Throws<SaveContextException>(
            () => CoreSaveParticipants.CreateMapSessionsParticipant(target).RestoreState(domain));

        Assert.That(error!.Message, Does.Contain("smuggled"));
        Assert.That(error.Message, Does.Contain(MapIdValue));
    }

    [Test]
    public void Restore_TypeMismatch_RejectedNamingVariable()
    {
        MapSessionManager source = CreateManagerWithSession();
        JsonObject domain = CaptureDomain(source);
        SessionVariables(domain)["kills"] = new JsonObject
        {
            ["type"] = MapVariableType.Float.ToString(),
            ["value"] = 7.0
        };

        MapSessionManager target = CreateManagerWithSession();

        SaveContextException? error = Assert.Throws<SaveContextException>(
            () => CoreSaveParticipants.CreateMapSessionsParticipant(target).RestoreState(domain));

        Assert.That(error!.Message, Does.Contain("kills"));
        Assert.That(error.Message, Does.Contain(MapIdValue));
    }

    [Test]
    public void Restore_FractionalIntValue_RejectedNamingVariable()
    {
        MapSessionManager source = CreateManagerWithSession();
        JsonObject domain = CaptureDomain(source);
        SessionVariables(domain)["kills"]!["value"] = 1.5;

        MapSessionManager target = CreateManagerWithSession();

        SaveContextException? error = Assert.Throws<SaveContextException>(
            () => CoreSaveParticipants.CreateMapSessionsParticipant(target).RestoreState(domain));

        Assert.That(error!.Message, Does.Contain("kills"));
    }

    [Test]
    public void Restore_DuplicateVariable_RejectedNamingVariable()
    {
        MapVariableStore store = MapVariableStore.Create(new MapId(MapIdValue), CreateConfig().Variables);
        var snapshot = new MapVariableStoreSnapshot(new MapVariableValueSnapshot[]
        {
            new("phase", MapVariableType.Int, 1, 0f),
            new("phase", MapVariableType.Int, 2, 0f),
            new("kills", MapVariableType.Int, 0, 0f),
            new("ammo", MapVariableType.Float, 0, 1.5f),
        });

        Assert.That(
            () => store.RestoreSnapshot(snapshot),
            Throws.InvalidOperationException.With.Message.Contains("phase"));
        Assert.That(store.ReadInt("phase"), Is.EqualTo(0), "rejected restore must not mutate values");
    }

    [Test]
    public void Capture_SessionWithoutVariableStore_Fails()
    {
        var manager = new MapSessionManager();
        MapSession session = manager.CreateSession(new MapId(MapIdValue), CreateConfig());
        session.Dispose();

        Assert.That(
            () => manager.CaptureSnapshot(),
            Throws.InvalidOperationException.With.Message.Contains(MapIdValue));
    }
}
