using System.Numerics;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.Systems;
using Ludots.Core.Persistence;
using NUnit.Framework;

namespace Ludots.Tests.GAS;

[TestFixture]
public sealed class InputActionPresenceTests
{
    [Test]
    public void UnboundInjection_IsStoredButNotPresent()
    {
        PlayerInputHandler handler = NewHandler(out StubInputBackend backend);
        handler.InjectAction("Stick", new Vector3(1f, 2f, 0f));
        handler.InjectAction("Other", new Vector3(3f, 4f, 0f));
        var snapshot = Capture(handler);

        Assert.That(snapshot.ReadAction<Vector2>("Stick"), Is.EqualTo(new Vector2(1f, 2f)));
        Assert.That(snapshot.TryReadPresentVector2("Stick", out _), Is.False);
        Assert.That(snapshot.ReadAction<Vector2>("Other"), Is.EqualTo(new Vector2(3f, 4f)));
        Assert.That(snapshot.TryReadPresentVector2("Other", out _), Is.False);
        Assert.That(backend, Is.Not.Null);
    }

    [Test]
    public void ActiveContext_PresentsBoundZero_AndReplayRestoresTheBit()
    {
        PlayerInputHandler handler = NewHandler(out StubInputBackend backend);
        handler.PushContext("Aim");
        backend.MousePosition = Vector2.Zero;
        FrozenInputActionReader snapshot = Capture(handler);

        Assert.That(snapshot.TryReadPresentVector2("Stick", out Vector2 stick), Is.True);
        Assert.That(stick, Is.EqualTo(Vector2.Zero));
        Assert.That(snapshot.ReadAction<Vector2>("Stick"), Is.EqualTo(stick));
        Assert.That(snapshot.TryReadPresentVector2("Other", out _), Is.False);

        var recorded = new List<AuthoritativeAction>();
        snapshot.CopyAuthoritativeActions(recorded);
        AuthoritativeAction recordedStick = recorded.Single(action => action.ActionId == "Stick");
        AuthoritativeAction recordedOther = recorded.Single(action => action.ActionId == "Other");
        Assert.That(recordedStick.Present, Is.True);
        Assert.That(recordedOther.Present, Is.False);

        var replay = new FrozenInputActionReader();
        replay.QueueReplayActions(recorded);
        new AuthoritativeInputSnapshotSystem(replay, new AuthoritativeInputAccumulator()).Update(1f / 60f);

        Assert.That(replay.TryReadPresentVector2("Stick", out Vector2 replayed), Is.True);
        Assert.That(replayed, Is.EqualTo(Vector2.Zero));
        Assert.That(replay.TryReadPresentVector2("Other", out _), Is.False);
    }

    [Test]
    public void PoppingTheContext_ClearsPresence()
    {
        PlayerInputHandler handler = NewHandler(out StubInputBackend backend);
        handler.PushContext("Aim");
        backend.MousePosition = new Vector2(8f, 9f);
        FrozenInputActionReader snapshot = Capture(handler);
        Assert.That(snapshot.TryReadPresentVector2("Stick", out Vector2 stick), Is.True);
        Assert.That(stick, Is.EqualTo(new Vector2(8f, 9f)));

        handler.PopContext("Aim");
        snapshot = Capture(handler);

        Assert.That(snapshot.TryReadPresentVector2("Stick", out _), Is.False);
        Assert.That(snapshot.ReadAction<Vector2>("Stick"), Is.EqualTo(Vector2.Zero));
    }

    private static PlayerInputHandler NewHandler(out StubInputBackend backend)
    {
        backend = new StubInputBackend();
        return new PlayerInputHandler(backend, new InputConfigRoot
        {
            Actions =
            {
                new InputActionDef
                {
                    Id = "Stick",
                    Type = InputActionType.Axis2D,
                    FiresOn = InputActionDef.FiresOnChange,
                },
                new InputActionDef { Id = "Other", Type = InputActionType.Axis2D },
            },
            Contexts =
            {
                new InputContextDef
                {
                    Id = "Aim",
                    Bindings = { new InputBindingDef { ActionId = "Stick", Path = "<Mouse>/Pos" } },
                },
            },
        });
    }

    private static FrozenInputActionReader Capture(PlayerInputHandler handler)
    {
        handler.Update(1f / 60f);
        var accumulator = new AuthoritativeInputAccumulator();
        handler.CaptureFrame(accumulator);
        var snapshot = new FrozenInputActionReader();
        accumulator.BuildTickSnapshot(snapshot);
        return snapshot;
    }

    private sealed class StubInputBackend : IInputBackend
    {
        public Vector2 MousePosition { get; set; }

        public float GetAxis(string devicePath) => 0f;
        public bool GetButton(string devicePath) => false;
        public Vector2 GetMousePosition() => MousePosition;
        public float GetMouseWheel() => 0f;
        public void EnableIME(bool enable) { }
        public void SetIMECandidatePosition(int x, int y) { }
        public string GetCharBuffer() => string.Empty;
    }
}
