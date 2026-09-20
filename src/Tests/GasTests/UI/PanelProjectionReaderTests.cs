using System;
using Arch.Core;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.Registry;
using Ludots.Core.UI.PanelProjection;
using NUnit.Framework;

namespace Ludots.Tests.Gas.UI;

[TestFixture]
public sealed class PanelProjectionReaderTests
{
    private World _world = null!;
    private Entity _owner;
    private GraphOutputValueStore _store = null!;
    private PanelProjectionReader _reader = null!;

    [SetUp]
    public void SetUp()
    {
        _world = World.Create();
        _owner = _world.Create();
        _store = new GraphOutputValueStore(new StringIntRegistry(), initialCapacity: 8);
        _reader = new PanelProjectionReader(_world, _store);
    }

    [TearDown]
    public void TearDown()
    {
        _world.Dispose();
    }

    [Test]
    public void Resolve_GraphOutput_ReadsMaterializedValue()
    {
        _store.SetFloat(_owner, "panel.hp", 42f);
        PanelProjectionValue value = _reader.Resolve(_owner, new PanelPin("hp", "panel.hp", realtime: true, defaultValue: 0f));

        Assert.That(value.FloatValue, Is.EqualTo(42f));
        Assert.That(value.FromGraph, Is.True);
        Assert.That(value.Revision, Is.GreaterThan(0u));
    }

    [Test]
    public void Resolve_MissingOutput_FallsBackToPinDefault_NoError()
    {
        PanelProjectionValue value = _reader.Resolve(_owner, new PanelPin("hp", "panel.hp", realtime: true, defaultValue: 7.5f));

        Assert.That(value.FloatValue, Is.EqualTo(7.5f));
        Assert.That(value.FromGraph, Is.False);
        Assert.That(value.Revision, Is.EqualTo(0u));
    }

    [Test]
    public void Resolve_OtherOwnersOutput_DoesNotLeakAcrossScopes()
    {
        Entity other = _world.Create();
        _store.SetFloat(other, "panel.hp", 99f);

        PanelProjectionValue value = _reader.Resolve(_owner, new PanelPin("hp", "panel.hp", realtime: true, defaultValue: 1f));
        Assert.That(value.FloatValue, Is.EqualTo(1f), "outputs are owner-scoped; other scopes resolve to defaults");
    }

    [Test]
    public void IsOwnerLive_FalseAfterEntityDeath()
    {
        Assert.That(_reader.IsOwnerLive(_owner), Is.True);
        _world.Destroy(_owner);
        Assert.That(_reader.IsOwnerLive(_owner), Is.False);
    }
}
