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
    public void Resolve_FloatOutput_MaterializesFloatValue()
    {
        _store.SetFloat(_owner, "panel.hp", 42.5f);
        PanelProjectionValue value = _reader.Resolve(_owner, new PanelPin("hp", "panel.hp", realtime: true, kind: PanelValueKind.Float));

        Assert.That(value.Kind, Is.EqualTo(PanelValueKind.Float));
        Assert.That(value.FloatValue, Is.EqualTo(42.5f));
        Assert.That(value.Revision, Is.GreaterThan(0u));
    }

    [Test]
    public void Resolve_IntOutput_MaterializesIntValue()
    {
        _store.SetInt(_owner, "panel.tier", 7);
        PanelProjectionValue value = _reader.Resolve(_owner, new PanelPin("tier", "panel.tier", realtime: true, kind: PanelValueKind.Int));

        Assert.That(value.Kind, Is.EqualTo(PanelValueKind.Int));
        Assert.That(value.IntValue, Is.EqualTo(7));
        Assert.That(value.NumericValue, Is.EqualTo(7f), "Int widens to float only through the explicit numeric projection");
    }

    [Test]
    public void Resolve_BoolOutput_MaterializesBoolValue()
    {
        _store.SetBool(_owner, "panel.ready", true);
        PanelProjectionValue value = _reader.Resolve(_owner, new PanelPin("ready", "panel.ready", realtime: true, kind: PanelValueKind.Bool));

        Assert.That(value.Kind, Is.EqualTo(PanelValueKind.Bool));
        Assert.That(value.BoolValue, Is.True);
        Assert.That(
            () => value.NumericValue,
            Throws.InvalidOperationException, "Bool has no numeric form; no silent 1f/0f coercion");
    }

    [Test]
    public void Resolve_EntityOutput_MaterializesEntityValue()
    {
        Entity selected = _world.Create();
        _store.SetEntity(_owner, "panel.selection", selected);
        PanelProjectionValue value = _reader.Resolve(_owner, new PanelPin("selection", "panel.selection", realtime: true, kind: PanelValueKind.Entity));

        Assert.That(value.Kind, Is.EqualTo(PanelValueKind.Entity));
        Assert.That(value.EntityValue, Is.EqualTo(selected));
        Assert.That(
            () => value.NumericValue,
            Throws.InvalidOperationException, "Entity has no numeric form; no silent id coercion");
    }

    [Test]
    public void Resolve_KindFollowsGraphOutput_NoCoercion()
    {
        // Int graph output stays Int even when the pin declares Float (legacy templates
        // omit "type"); the value carries the graph's actual kind, never a flattened float.
        _store.SetInt(_owner, "panel.stage", 5);
        PanelProjectionValue value = _reader.Resolve(_owner, new PanelPin("stage", "panel.stage", realtime: true, kind: PanelValueKind.Float));

        Assert.That(value.Kind, Is.EqualTo(PanelValueKind.Int));
        Assert.That(value.IntValue, Is.EqualTo(5));
    }

    [Test]
    public void Resolve_MissingOutput_FailsLoudly_NamingPinAndKey()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => _reader.Resolve(_owner, new PanelPin("hp", "panel.hp", realtime: true, kind: PanelValueKind.Float)))!;

        Assert.That(error.Message, Does.Contain("hp"));
        Assert.That(error.Message, Does.Contain("panel.hp"));
        Assert.That(error.Message, Does.Contain(_owner.Id.ToString()), "the failing owner is named");
    }

    [Test]
    public void Resolve_OtherOwnersOutput_DoesNotLeakAcrossScopes()
    {
        Entity other = _world.Create();
        _store.SetFloat(other, "panel.hp", 99f);

        Assert.That(
            () => _reader.Resolve(_owner, new PanelPin("hp", "panel.hp", realtime: true, kind: PanelValueKind.Float)),
            Throws.InvalidOperationException, "outputs are owner-scoped; the owner without output fails explicitly");
    }

    [Test]
    public void Resolve_RemovedOwnerOutput_FailsLoudly()
    {
        _store.SetFloat(_owner, "panel.hp", 42f);
        Assert.That(_reader.Resolve(_owner, new PanelPin("hp", "panel.hp", realtime: true, kind: PanelValueKind.Float)).FloatValue, Is.EqualTo(42f));

        _store.RemoveOwner(_owner);
        Assert.That(
            () => _reader.Resolve(_owner, new PanelPin("hp", "panel.hp", realtime: true, kind: PanelValueKind.Float)),
            Throws.InvalidOperationException, "owner output retirement is an explicit failure, not a default");
    }

    [Test]
    public void IsOwnerLive_FalseAfterEntityDeath()
    {
        Assert.That(_reader.IsOwnerLive(_owner), Is.True);
        _world.Destroy(_owner);
        Assert.That(_reader.IsOwnerLive(_owner), Is.False);
    }
}
