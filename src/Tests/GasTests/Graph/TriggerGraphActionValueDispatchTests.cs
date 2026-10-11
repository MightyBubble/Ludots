using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Config;
using Ludots.Core.Gameplay.MapTriggers;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Map;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.Gas.Graph;

[TestFixture]
public sealed class TriggerGraphActionValueDispatchTests
{
    private World _world = null!;
    private MapSession _session = null!;
    private Entity _rep;
    private FrozenInputActionReader _input = null!;
    private AuthoritativePointerButtonSnapshot _buttons = null!;
    private TriggerGraphActionBindingIndex _index = null!;
    private TriggerManager _triggers = null!;
    private List<ScriptContext> _contexts = null!;

    [SetUp]
    public void SetUp()
    {
        _world = World.Create();
        _rep = _world.Create();
        _session = new MapSession(new MapId("map_action_value"), new MapConfig());
        _input = new FrozenInputActionReader();
        _buttons = new AuthoritativePointerButtonSnapshot();
        _index = new TriggerGraphActionBindingIndex();
        _triggers = new TriggerManager { EventSchemas = new EventSchemaRegistry() };
        _contexts = new List<ScriptContext>();
    }

    [TearDown]
    public void TearDown()
    {
        _session.Dispose();
        _world.Dispose();
    }

    [Test]
    public void Change_FiresOncePerDistinctValue()
    {
        TriggerGraphActionBindingSystem system = NewSystem(Axis("Stick"));
        Mount("Stick");
        Present("Stick", 1f, 0f);

        system.Update(0f);
        system.Update(0f);
        Assert.That(_contexts, Has.Count.EqualTo(1));
        AssertActionValue(_contexts[0], 1f, 0f);

        Present("Stick", 1f, 2f);
        system.Update(0f);
        Assert.That(_contexts, Has.Count.EqualTo(2));
        AssertActionValue(_contexts[1], 1f, 2f);

        Present("Stick", 0f, 0f);
        system.Update(0f);
        system.Update(0f);
        Assert.That(_contexts, Has.Count.EqualTo(3));
        AssertActionValue(_contexts[2], 0f, 0f);
    }

    [Test]
    public void FreshMount_FiresInitialSampleWhenBoundIncludingZero()
    {
        TriggerGraphActionBindingSystem system = NewSystem(Axis("Stick"));
        Present("Stick", 0f, 0f);
        Mount("Stick");

        system.Update(0f);
        system.Update(0f);

        Assert.That(_contexts, Has.Count.EqualTo(1));
        AssertActionValue(_contexts[0], 0f, 0f);
    }

    [Test]
    public void AbsentAction_NeverFiresUntilItBecomesPresent()
    {
        TriggerGraphActionBindingSystem system = NewSystem(Axis("Stick"));
        Mount("Stick");
        Present("Stick", 1f, 2f, present: false);
        system.Update(0f);
        Present("Stick", 3f, 4f, present: false);
        system.Update(0f);
        Assert.That(_contexts, Is.Empty);

        Present("Stick", 3f, 4f, present: true);
        system.Update(0f);
        system.Update(0f);

        Assert.That(_contexts, Has.Count.EqualTo(1));
        AssertActionValue(_contexts[0], 3f, 4f);
    }

    [Test]
    public void ChangePayload_StampsWatchedVector_AndLeavesPointerAsPointerPos()
    {
        TriggerGraphActionBindingSystem system = NewSystem(Axis("Stick"));
        Mount("Stick");
        Present("Stick", 5f, 6f);
        Present(ReservedInputActionIds.PointerPos, 9f, 8f);

        system.Update(0f);

        Assert.That(_contexts, Has.Count.EqualTo(1));
        AssertActionValue(_contexts[0], 5f, 6f);
        AssertPointer(_contexts[0], 9f, 8f);
    }

    [Test]
    public void ChangePayload_PointerIsZeroWhenPointerPosIsAbsent()
    {
        TriggerGraphActionBindingSystem system = NewSystem(Axis("Stick"));
        Mount("Stick");
        Present("Stick", 5f, 6f);

        system.Update(0f);

        Assert.That(_contexts, Has.Count.EqualTo(1));
        AssertActionValue(_contexts[0], 5f, 6f);
        AssertPointer(_contexts[0], 0f, 0f);
    }

    [Test]
    public void PointerMoved_StampsPointerAsActionValue_AndSkipsUnchanged()
    {
        TriggerGraphActionBindingSystem system = NewSystem();
        Mount(ReservedInputActionIds.PointerMoved);
        Present(ReservedInputActionIds.PointerPos, 10f, 20f);

        system.Update(0f);
        system.Update(0f);

        Assert.That(_contexts, Has.Count.EqualTo(1));
        Assert.That(_contexts[0].Get<string>(MapTriggerEventPayloadKeys.Action),
            Is.EqualTo(ReservedInputActionIds.PointerMoved));
        AssertActionValue(_contexts[0], 10f, 20f);
        AssertPointer(_contexts[0], 10f, 20f);

        Present(ReservedInputActionIds.PointerPos, 11f, 20f);
        system.Update(0f);
        Assert.That(_contexts, Has.Count.EqualTo(2));
        AssertActionValue(_contexts[1], 11f, 20f);
        AssertPointer(_contexts[1], 11f, 20f);
    }

    [Test]
    public void PointerMoved_DoesNotFireWhenPointerPosIsAbsent()
    {
        TriggerGraphActionBindingSystem system = NewSystem();
        Mount(ReservedInputActionIds.PointerMoved);
        Present(ReservedInputActionIds.PointerPos, 10f, 20f, present: false);
        Present(ReservedInputActionIds.PointerMoved, 10f, 20f, present: true);

        system.Update(0f);

        Assert.That(_contexts, Is.Empty);
    }

    [Test]
    public void ButtonPress_DoesNotStampActionValue()
    {
        TriggerGraphActionBindingSystem system = NewSystem(Button("Click", InputActionDef.FiresOnPress));
        Mount("Click");
        _input.SetActionState("Click", Vector3.One, isDown: true, pressedThisFrame: true, releasedThisFrame: false, present: true);
        _buttons.SetState("Click", new PointerButtonState(
            pointer: new Vector2(1f, 1f),
            pressPointer: new Vector2(4f, 5f),
            releasePointer: default,
            lastDownPointer: new Vector2(4f, 5f),
            isDown: true,
            pressedThisFrame: true,
            releasedThisFrame: false,
            hasPressPointer: true,
            hasReleasePointer: false,
            hasLastDownPointer: true));

        system.Update(0f);
        _input.SetActionState(
            "Click",
            Vector3.One,
            isDown: true,
            pressedThisFrame: false,
            releasedThisFrame: false,
            present: true);
        system.Update(0f);

        Assert.That(_contexts, Has.Count.EqualTo(1));
        AssertPointer(_contexts[0], 4f, 5f);
        Assert.That(_contexts[0].Contains(MapTriggerEventPayloadKeys.ActionValueX), Is.False);
        Assert.That(_contexts[0].Contains(MapTriggerEventPayloadKeys.ActionValueY), Is.False);
        Assert.That(_contexts[0].Get<string>(MapTriggerEventPayloadKeys.Action), Is.EqualTo("Click"));
    }

    [Test]
    public void SecondMount_FiresItsOwnInitialSampleAtTheSameValue()
    {
        TriggerGraphActionBindingSystem system = NewSystem();
        Mount(ReservedInputActionIds.PointerMoved, "first");
        Present(ReservedInputActionIds.PointerPos, 3f, 4f);
        system.Update(0f);
        Assert.That(_contexts, Has.Count.EqualTo(1));

        Mount(ReservedInputActionIds.PointerMoved, "second");
        system.Update(0f);

        Assert.That(_contexts, Has.Count.EqualTo(2));
        AssertActionValue(_contexts[1], 3f, 4f);
        Assert.That(_contexts[1].Get<string>(MapTriggerEventPayloadKeys.Action),
            Is.EqualTo(ReservedInputActionIds.PointerMoved));
    }

    [Test]
    public void ReregisteredMount_FiresInitialSampleAgain()
    {
        TriggerGraphActionBindingSystem system = NewSystem(Axis("Stick"));
        TriggerGraphMountTrigger mount = Mount("Stick");
        Present("Stick", 7f, 7f);
        system.Update(0f);
        system.Update(0f);
        Assert.That(_contexts, Has.Count.EqualTo(1));

        _index.Remove(mount);
        _index.Add(mount);
        system.Update(0f);

        Assert.That(_contexts, Has.Count.EqualTo(2));
        AssertActionValue(_contexts[1], 7f, 7f);
    }

    [Test]
    public void BlankFiresOn_FailsFastNamingTheAction()
    {
        TriggerGraphActionBindingSystem system = NewSystem(new InputActionDef
        {
            Id = "Zoom",
            Type = InputActionType.Axis1D,
        });
        Mount("Zoom");

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => system.Update(0f))!;
        Assert.That(ex.Message, Does.Contain("LUDOTS_INPUT_ACTION_FIRES_ON_INVALID"));
        Assert.That(ex.Message, Does.Contain("Zoom"));
    }

    private TriggerGraphActionBindingSystem NewSystem(params InputActionDef[] actions)
    {
        var config = new InputConfigRoot();
        for (int i = 0; i < actions.Length; i++) config.Actions.Add(actions[i]);
        InputConfigPipelineLoader.Validate(config, "test.input.json");
        return new TriggerGraphActionBindingSystem(
            () => _session,
            _triggers,
            () =>
            {
                var context = new ScriptContext();
                _contexts.Add(context);
                return context;
            },
            () => _input,
            () => _buttons,
            _index,
            config,
            () => null,
            () => null,
            _ => { });
    }

    private TriggerGraphMountTrigger Mount(string actionId, string label = "track")
    {
        var mount = new TriggerGraphMountTrigger(
            graphId: 1,
            graphName: "graph.action.value",
            entry: new TriggerGraphEntry(
                label,
                TriggerGraphEntry.InputActionSchemaEventName,
                startPc: 0,
                once: false,
                filters: default,
                refire: TriggerGraphEntry.RefireIgnore,
                actionId: actionId),
            scope: _rep);
        _index.Add(mount);
        return mount;
    }

    private void Present(string actionId, float x, float y, bool present = true)
    {
        _input.SetActionState(
            actionId,
            new Vector3(x, y, 0f),
            isDown: false,
            pressedThisFrame: false,
            releasedThisFrame: false,
            present: present);
    }

    private static InputActionDef Axis(string id)
    {
        return new InputActionDef
        {
            Id = id,
            Type = InputActionType.Axis2D,
            FiresOn = InputActionDef.FiresOnChange,
        };
    }

    private static InputActionDef Button(string id, string firesOn)
    {
        return new InputActionDef
        {
            Id = id,
            Type = InputActionType.Button,
            FiresOn = firesOn,
        };
    }

    private static void AssertActionValue(ScriptContext context, float x, float y)
    {
        Assert.That(context.Get<float>(MapTriggerEventPayloadKeys.ActionValueX), Is.EqualTo(x));
        Assert.That(context.Get<float>(MapTriggerEventPayloadKeys.ActionValueY), Is.EqualTo(y));
    }

    private static void AssertPointer(ScriptContext context, float x, float y)
    {
        Assert.That(context.Get<float>(MapTriggerEventPayloadKeys.PointerScreenX), Is.EqualTo(x));
        Assert.That(context.Get<float>(MapTriggerEventPayloadKeys.PointerScreenY), Is.EqualTo(y));
    }
}
