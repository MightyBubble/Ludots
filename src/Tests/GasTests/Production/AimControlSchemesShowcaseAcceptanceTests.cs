using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Client;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Map;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using Ludots.Tests;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production;

/// <summary>
/// Headless acceptance for the zero-C# aim control scheme showcase: every scheme × cast mode
/// submits a slot at an entity or a ground point, indicator collections fill only while aiming,
/// cancel submits nothing, and a runtime scheme switch changes which device moves the aim point.
/// </summary>
[NonParallelizable]
[TestFixture]
public sealed class AimControlSchemesShowcaseAcceptanceTests
{
    private const string MapIdValue = "aim_control_schemes";
    private const float DeltaTime = 1f / 60f;
    private const float RangeCm = 800f;

    [Test]
    public void LoLKeyboard_CursorAim_SubmitsEachCastMode()
    {
        using Session session = Session.Boot("scheme.aim.kbm");
        session.RunCastMatrix(AimSurface.KeyboardCursor, slotEntity: 0, slotGround: 3);
    }

    [Test]
    public void LoLGamepad_RightStick_SubmitsEachCastMode()
    {
        using Session session = Session.Boot("scheme.aim.gamepad");
        session.RunCastMatrix(AimSurface.GamepadRight, slotEntity: 0, slotGround: 3);
    }

    [Test]
    public void DiabloKeyboard_SmartCursor_AndPrimaryClickMovesOrAttacks()
    {
        using Session session = Session.Boot("scheme.aim.kbm_skills");
        session.AssertPrimaryClickMoveAndAttack();
        session.RunCastMatrix(AimSurface.KeyboardCursor, slotEntity: 0, slotGround: 3, confirm: ConfirmControl.Space);
    }

    [Test]
    public void DiabloGamepad_LeftStick_SubmitsEachCastModeAlongHeading()
    {
        using Session session = Session.Boot("scheme.aim.gamepad_left");
        session.RunCastMatrix(AimSurface.GamepadLeft, slotEntity: 0, slotGround: 3);
    }

    [Test]
    public void SchemeSwitch_ToGamepad_MouseStopsDrivingAimAndStickStarts()
    {
        using Session session = Session.Boot("scheme.aim.kbm");
        Vector2 hero = session.Position(session.Hero);
        Vector2 first = hero + new Vector2(700f, 0f);
        Vector2 second = hero + new Vector2(-400f, 180f);
        session.AimMouse(first);
        Vector2 aimed = session.ReadPoint();
        Assert.That(Vector2.Distance(aimed, first), Is.LessThanOrEqualTo(40f), session.Diag("first mouse aim"));

        session.AimMouse(second);
        Vector2 moved = session.ReadPoint();
        Assert.That(Vector2.Distance(moved, second), Is.LessThanOrEqualTo(40f), session.Diag("second mouse aim"));

        session.SwitchScheme("scheme.aim.gamepad");
        session.AimMouse(hero + new Vector2(900f, 600f));
        Vector2 afterMouse = session.ReadPoint();
        Assert.That(Vector2.Distance(afterMouse, moved), Is.LessThanOrEqualTo(5f), session.Diag("mouse after gamepad switch"));

        session.AimStick("rightStick", new Vector2(0.5f, 0f));
        Vector2 afterStick = session.ReadPoint();
        Vector2 expected = session.Position(session.Hero) + new Vector2(RangeCm * 0.5f, 0f);
        Assert.That(Vector2.Distance(afterStick, expected), Is.LessThanOrEqualTo(40f), session.Diag("stick after gamepad switch"));
    }

    private enum AimSurface
    {
        KeyboardCursor,
        GamepadRight,
        GamepadLeft,
    }

    private enum ConfirmControl
    {
        MouseLeft,
        Space,
        RightShoulder,
    }

    private sealed class Session : IDisposable
    {
        private readonly GameEngine _engine;
        private readonly SyntheticInputDevice _device;
        private readonly string _inputContextId;
        private readonly AimSurface _surface;

        private Session(GameEngine engine, SyntheticInputDevice device, string schemeId)
        {
            _engine = engine;
            _device = device;
            SchemeId = schemeId;
            Hero = Require("aim-schemes-hero");
            Near = Require("aim-schemes-dummy-near");
            StickDummy = Require("aim-schemes-dummy-stick");
            _inputContextId = schemeId switch
            {
                "scheme.aim.kbm" => "Aim.Kbm",
                "scheme.aim.gamepad" => "Aim.Gamepad",
                "scheme.aim.kbm_skills" => "Aim.KbmSkills",
                "scheme.aim.gamepad_left" => "Aim.GamepadLeft",
                _ => throw new ArgumentException(schemeId, nameof(schemeId)),
            };
            _surface = schemeId switch
            {
                "scheme.aim.kbm" or "scheme.aim.kbm_skills" => AimSurface.KeyboardCursor,
                "scheme.aim.gamepad" => AimSurface.GamepadRight,
                "scheme.aim.gamepad_left" => AimSurface.GamepadLeft,
                _ => throw new ArgumentException(schemeId, nameof(schemeId)),
            };
        }

        public string SchemeId { get; }
        public Entity Hero { get; }
        public Entity Near { get; }
        public Entity StickDummy { get; }

        public static Session Boot(string schemeId)
        {
            string repoRoot = FindRepoRoot();
            var device = new SyntheticInputDevice();
            var backend = new SyntheticBackend(device);
            GameEngine? engine = null;
            try
            {
                engine = new GameEngine();
                engine.InitializeWithConfigPipeline(
                    RepoModPaths.ResolveExplicit(
                        repoRoot,
                        new[] { "LudotsCoreMod", "AimInteractionMod", "AimControlSchemesShowcaseMod" }),
                    Path.Combine(repoRoot, "assets"));
                InputConfigRoot inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
                var input = new PlayerInputHandler(backend, inputConfig);
                for (int i = 0; i < engine.MergedConfig.StartupInputContexts.Count; i++)
                {
                    input.PushContext(engine.MergedConfig.StartupInputContexts[i]);
                }

                engine.SetService(CoreServiceKeys.InputHandler, input);
                engine.SetService(CoreServiceKeys.InputBackend, (IInputBackend)backend);
                engine.SetService(CoreServiceKeys.UiCaptured, false);
                var projector = new CmScreenProjector();
                engine.SetService(CoreServiceKeys.ViewController, (IViewController)new HeadlessView(4096f, 4096f));
                engine.SetService(CoreServiceKeys.ScreenRayProvider, (IScreenRayProvider)projector);
                engine.SetService(CoreServiceKeys.ScreenProjector, (IScreenProjector)projector);
                engine.Start();
                engine.LoadMap(new MapLoadRequest(
                    new MapId(MapIdValue),
                    MapLaunchContext.Create(new[] { new LocalSeatLaunchBinding("seat.0", 1, schemeId) })));
                engine.SetService(CoreServiceKeys.ScreenRayProvider, (IScreenRayProvider)projector);
                engine.SetService(CoreServiceKeys.ScreenProjector, (IScreenProjector)projector);
                var session = new Session(engine, device, schemeId);
                session.Tick(8);
                session.AssertBootstrapped();
                return session;
            }
            catch
            {
                engine?.Dispose();
                throw;
            }
        }

        public void Dispose() => _engine.Dispose();

        public void RunCastMatrix(AimSurface surface, int slotEntity, int slotGround, ConfirmControl? confirm = null)
        {
            Assert.That(surface, Is.EqualTo(_surface));
            ConfirmControl confirmControl = confirm ?? (surface == AimSurface.KeyboardCursor
                ? ConfirmControl.MouseLeft
                : ConfirmControl.RightShoulder);
            RunMode("castMode.smart", "f1", surface, confirmControl, slotEntity, slotGround, indicating: false);
            RunMode("castMode.aim_release", "f2", surface, confirmControl, slotEntity, slotGround, indicating: true);
            RunMode("castMode.aim_click", "f3", surface, confirmControl, slotEntity, slotGround, indicating: true);
        }

        public void AssertPrimaryClickMoveAndAttack()
        {
            SetMode("castMode.smart", "f1");
            Vector2 ground = Position(Hero) + new Vector2(700f, 0f);
            AimMouse(ground);
            ClickLeft();
            Order move = RequireActiveOrder("moveTo");
            Vector2 moveAt = new(move.Args.Spatial.WorldCm.X, move.Args.Spatial.WorldCm.Z);
            Assert.That(Vector2.Distance(moveAt, ground), Is.LessThanOrEqualTo(40f), Diag("primary ground move"));

            AimMouse(Position(Near));
            ClickLeft();
            Order attack = RequireActiveOrder("attackTarget");
            Assert.That(attack.Target, Is.EqualTo(Near), Diag("primary hostile attack"));
        }

        public void SwitchScheme(string schemeId)
        {
            ControlSchemeRuntime schemes = _engine.GetService(CoreServiceKeys.ControlSchemeRuntime)
                ?? throw new InvalidOperationException("ControlSchemeRuntime missing.");
            Assert.That(schemes.SchemeIdRegistry.TryGetId(schemeId, out int id), Is.True, schemeId);
            Assert.That(schemes.TrySwitch(id), Is.True, Diag("switch " + schemeId));
            Tick(1);
            Assert.That(schemes.SchemeIdRegistry.GetName(schemes.ActiveSchemeId), Is.EqualTo(schemeId));
        }

        public Vector2 Position(Entity entity) =>
            _engine.World.Get<WorldPositionCm>(entity).Value.ToVector2();

        public Vector2 ReadPoint()
        {
            Assert.That(ReadInt("aim.point.valid"), Is.EqualTo(1), Diag("aim point"));
            return new Vector2(ReadFloat("aim.point.x_cm"), ReadFloat("aim.point.y_cm"));
        }

        public void AimMouse(Vector2 screenCm)
        {
            _device.MovePointer(screenCm.X + 40f, screenCm.Y + 40f);
            AdvanceTick();
            _device.MovePointer(screenCm.X, screenCm.Y);
            AdvanceTick();
        }

        public void AimStick(string stick, Vector2 axis)
        {
            SetAxis(stick, Vector2.Zero);
            AdvanceTick();
            SetAxis(stick, axis);
            AdvanceTick();
        }

        public string Diag(string step)
        {
            string point = "<missing>";
            try
            {
                point = $"({ReadFloat("aim.point.x_cm"):0},{ReadFloat("aim.point.y_cm"):0}) valid={ReadInt("aim.point.valid")} target={DescribeTarget()}";
            }
            catch (InvalidOperationException ex)
            {
                point = ex.Message;
            }

            return $"{step} || scheme={SchemeId} mode={ReadCastMode()} contexts=[{Contexts()}] input={_inputContextId} " +
                   $"range={TryReadFloat("aim.range_cm")} point={point} indicating={CollectionCount("aim.indicating")} " +
                   $"hover={CollectionCount("aim.hover")} actors={CollectionCount("aim.actors")} triggers={TriggerErrors()} " +
                   $"exec={DescribeExec()} order={DescribeActiveOrder()}";
        }

        private void AssertBootstrapped()
        {
            PlayerInputHandler input = _engine.GetService(CoreServiceKeys.InputHandler)
                ?? throw new InvalidOperationException("InputHandler missing.");
            Assert.That(input.HasContext("AimSchemes.Controls"), Is.True, Diag("startup controls"));
            Assert.That(input.HasContext(_inputContextId), Is.True, Diag("scheme context"));
            Assert.That(Contexts(), Does.Contain("interaction.context.aim.cast"), Diag("aim cast context"));
            Assert.That(ReadCastMode(), Is.EqualTo("castMode.smart"), Diag("seeded cast mode"));
            Assert.That(ReadFloat("aim.range_cm"), Is.EqualTo(RangeCm).Within(0.1f), Diag("seeded range"));
            Assert.That(CollectionCount("aim.actors"), Is.EqualTo(1), Diag("aim actor"));
            Assert.That(TriggerErrors(), Is.EqualTo("<none>"), Diag("boot"));
            float near = Vector2.Distance(Position(Hero), Position(Near));
            float stick = Vector2.Distance(Position(Hero), Position(StickDummy));
            Assert.That(near, Is.LessThanOrEqualTo(150f), "near dummy must sit inside the 160cm pointer assist");
            Assert.That(stick, Is.EqualTo(400f).Within(1f), "stick dummy sits at half of the 800cm cast range");
        }

        private void RunMode(
            string modeId,
            string modeKey,
            AimSurface surface,
            ConfirmControl confirm,
            int slotEntity,
            int slotGround,
            bool indicating)
        {
            SetMode(modeId, modeKey);
            if (indicating)
            {
                RunIndicating(surface, confirm, slotEntity, target: Near, cancel: true);
                RunIndicating(surface, confirm, slotGround, target: null, cancel: false);
                RunIndicating(surface, confirm, slotEntity, target: Near, cancel: false);
                RunIndicating(surface, confirm, slotGround, target: null, cancel: true);
            }
            else
            {
                RunSmart(surface, slotEntity, Near);
                RunSmart(surface, slotGround, null);
            }
        }

        private void RunSmart(AimSurface surface, int slot, Entity? target)
        {
            AimFor(surface, target);
            if (target == null)
            {
                Assert.That(TargetAlive(), Is.False, Diag($"smart slot {slot} ground target"));
            }
            else
            {
                Assert.That(ReadTarget(), Is.EqualTo(target.Value), Diag($"smart slot {slot} entity target"));
            }

            PressSlot(surface, slot);
            AssertCast(slot, target, $"smart slot {slot}");
            ReleaseSlot(surface, slot);
            Settle();
        }

        private void RunIndicating(AimSurface surface, ConfirmControl confirm, int slot, Entity? target, bool cancel)
        {
            AimFor(surface, target);
            PressSlot(surface, slot);
            Tick(1);
            Assert.That(CollectionCount("aim.indicating"), Is.GreaterThan(0), Diag($"indicate slot {slot}"));
            Assert.That(CollectionCount("aim.hover"), Is.GreaterThan(0), Diag($"hover slot {slot}"));
            Assert.That(TriggerErrors(), Is.EqualTo("<none>"), Diag($"indicate quiet slot {slot}"));
            if (cancel)
            {
                Cancel(surface);
                ReleaseSlot(surface, slot);
                Assert.That(_engine.World.Has<AbilityExecInstance>(Hero), Is.False, Diag($"cancel slot {slot}"));
                Assert.That(CollectionCount("aim.indicating"), Is.EqualTo(0), Diag($"cancel indicating slot {slot}"));
                Assert.That(CollectionCount("aim.hover"), Is.EqualTo(0), Diag($"cancel hover slot {slot}"));
                Settle();
                return;
            }

            if (ReadCastMode() == "castMode.aim_release")
            {
                ReleaseSlot(surface, slot);
            }
            else
            {
                Confirm(confirm);
                ReleaseSlot(surface, slot);
            }

            AssertCast(slot, target, $"aimed slot {slot}");
            Settle();
        }

        private void AimFor(AimSurface surface, Entity? target)
        {
            if (surface == AimSurface.KeyboardCursor)
            {
                Vector2 screen = target == null
                    ? Position(Hero) + new Vector2(700f, 0f)
                    : Position(target.Value);
                AimMouse(screen);
                return;
            }

            string stick = surface == AimSurface.GamepadLeft ? "leftStick" : "rightStick";
            Vector2 axis = target == null
                ? new Vector2(-1f, 0f)
                : (Position(target.Value) - Position(Hero)) / RangeCm;
            AimStick(stick, axis);
        }

        private void AssertCast(int slot, Entity? target, string step)
        {
            Assert.That(_engine.World.Has<AbilityExecInstance>(Hero), Is.True, Diag(step));
            AbilityExecInstance exec = _engine.World.Get<AbilityExecInstance>(Hero);
            Assert.That(exec.AbilitySlot, Is.EqualTo(slot), Diag(step));
            if (target == null)
            {
                Assert.That(_engine.World.IsAlive(exec.Target), Is.False, Diag(step));
                Assert.That(exec.HasTargetPos, Is.EqualTo((byte)1), Diag(step));
                Vector2 actual = exec.TargetPosCm.ToVector2();
                Vector2 expected = _surface == AimSurface.KeyboardCursor
                    ? Position(Hero) + new Vector2(700f, 0f)
                    : Position(Hero) + new Vector2(-RangeCm, 0f);
                Assert.That(Vector2.Distance(actual, expected), Is.LessThanOrEqualTo(40f), Diag(step + $" actual={actual} expected={expected}"));
            }
            else
            {
                Assert.That(exec.Target, Is.EqualTo(target.Value), Diag(step));
            }

            Assert.That(TriggerErrors(), Is.EqualTo("<none>"), Diag(step));
        }

        private void SetMode(string modeId, string key)
        {
            TapKey(key);
            Assert.That(ReadCastMode(), Is.EqualTo(modeId), Diag("set " + modeId));
            Assert.That(TriggerErrors(), Is.EqualTo("<none>"), Diag("set " + modeId));
        }

        private void PressSlot(AimSurface surface, int slot)
        {
            if (surface == AimSurface.KeyboardCursor)
            {
                _device.KeyDown(SlotKey(slot));
            }
            else
            {
                _device.GamepadButtonDown(SlotButton(slot));
            }

            AdvanceTick();
        }

        private void ReleaseSlot(AimSurface surface, int slot)
        {
            if (surface == AimSurface.KeyboardCursor)
            {
                _device.KeyUp(SlotKey(slot));
            }
            else
            {
                _device.GamepadButtonUp(SlotButton(slot));
            }

            AdvanceTick();
        }

        private void Confirm(ConfirmControl confirm)
        {
            switch (confirm)
            {
                case ConfirmControl.MouseLeft:
                    ClickLeft();
                    break;
                case ConfirmControl.Space:
                    TapKey("space");
                    break;
                case ConfirmControl.RightShoulder:
                    _device.GamepadButtonDown("<Gamepad>/rightShoulder");
                    AdvanceTick();
                    _device.GamepadButtonUp("<Gamepad>/rightShoulder");
                    AdvanceTick();
                    break;
            }
        }

        private void Cancel(AimSurface surface)
        {
            if (surface == AimSurface.KeyboardCursor)
            {
                TapKey("escape");
                return;
            }

            _device.GamepadButtonDown("<Gamepad>/start");
            AdvanceTick();
            _device.GamepadButtonUp("<Gamepad>/start");
            AdvanceTick();
        }

        private void ClickLeft()
        {
            _device.PointerDown(SyntheticPointerButton.Left);
            AdvanceTick();
            _device.PointerUp(SyntheticPointerButton.Left);
            AdvanceTick();
        }

        private void TapKey(string key)
        {
            _device.KeyDown(key);
            AdvanceTick();
            _device.KeyUp(key);
            AdvanceTick();
        }

        private void Settle()
        {
            for (int i = 0; i < 12 && _engine.World.Has<AbilityExecInstance>(Hero); i++)
            {
                Tick(1);
            }
        }

        private void AdvanceTick()
        {
            _device.AdvanceFrame();
            Tick(1);
        }

        private void Tick(int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                _engine.SetService(CoreServiceKeys.UiCaptured, false);
                _engine.Tick(DeltaTime);
            }
        }

        private void SetAxis(string stick, Vector2 axis)
        {
            _device.SetGamepadAxis($"<Gamepad>/{stick}/x", axis.X);
            _device.SetGamepadAxis($"<Gamepad>/{stick}/y", axis.Y);
        }

        private Order RequireActiveOrder(string orderTypeKey)
        {
            OrderTypeRegistry orderTypes = _engine.GetService(CoreServiceKeys.OrderTypeRegistry)
                ?? throw new InvalidOperationException("OrderTypeRegistry missing.");
            int typeId = orderTypes.GetId(orderTypeKey);
            ref OrderBuffer buffer = ref _engine.World.Get<OrderBuffer>(Hero);
            Assert.That(buffer.HasActive, Is.True, Diag(orderTypeKey));
            Order order = buffer.ActiveOrder.Order;
            Assert.That(order.OrderTypeId, Is.EqualTo(typeId), Diag(orderTypeKey));
            return order;
        }

        private Entity Require(string instanceId)
        {
            MapSession session = _engine.CurrentMapSession ?? throw new InvalidOperationException("map not loaded");
            return session.EntityIndex.GetRequired(session.MapId.Value, instanceId, "AimControlSchemes");
        }

        private string ReadCastMode()
        {
            var registry = _engine.GetService(CoreServiceKeys.CastModeIdRegistry)
                ?? throw new InvalidOperationException("CastModeIdRegistry missing.");
            if (!_engine.World.TryGet(Hero, out InteractionPref pref) || pref.DefaultCastModeId <= 0)
            {
                return "<none>";
            }

            return registry.GetName(pref.DefaultCastModeId);
        }

        private string Contexts()
        {
            InteractionContextProfileRegistry? profiles = _engine.GetService(CoreServiceKeys.InteractionContextProfileRegistry);
            if (profiles == null)
            {
                return "<no profiles>";
            }

            Span<int> ids = stackalloc int[16];
            int count = InteractionContextInstanceRuntime.CopyActiveContextIdsNewestFirst(_engine.World, Hero, ids);
            var names = new List<string>(count);
            for (int i = 0; i < count; i++)
            {
                names.Add(profiles.ProfileIdRegistry.GetName(ids[i]) ?? ids[i].ToString());
            }

            return string.Join(",", names);
        }

        private int CollectionCount(string key)
        {
            if (_engine.GetService(CoreServiceKeys.EntityCollectionStore) is not EntityCollectionStore collections)
            {
                return -1;
            }

            Span<EntityCollectionHandle> handles = stackalloc EntityCollectionHandle[Math.Max(collections.CollectionCount, 1)];
            int count = collections.CopyActiveHandles(handles);
            for (int i = 0; i < count; i++)
            {
                if (!collections.TryGetView(handles[i], out EntityCollectionView view) ||
                    !string.Equals(view.Key, key, StringComparison.Ordinal))
                {
                    continue;
                }

                return view.Count;
            }

            return 0;
        }

        private float ReadFloat(string key)
        {
            int keyId = OrderBlackboardKeyRegistry.Resolve(key);
            if (!_engine.World.TryGet(Hero, out BlackboardFloatBuffer floats) || !floats.TryGet(keyId, out float value))
            {
                throw new InvalidOperationException($"blackboard float '{key}' missing");
            }

            return value;
        }

        private string TryReadFloat(string key)
        {
            try
            {
                return ReadFloat(key).ToString("0.##");
            }
            catch (InvalidOperationException)
            {
                return "<missing>";
            }
        }

        private int ReadInt(string key)
        {
            int keyId = OrderBlackboardKeyRegistry.Resolve(key);
            if (!_engine.World.TryGet(Hero, out BlackboardIntBuffer ints) || !ints.TryGet(keyId, out int value))
            {
                throw new InvalidOperationException($"blackboard int '{key}' missing");
            }

            return value;
        }

        private Entity ReadTarget()
        {
            int keyId = OrderBlackboardKeyRegistry.Resolve("aim.target");
            if (!_engine.World.TryGet(Hero, out BlackboardEntityBuffer entities) || !entities.TryGet(keyId, out Entity value))
            {
                return Entity.Null;
            }

            return value;
        }

        private bool TargetAlive()
        {
            Entity target = ReadTarget();
            return target != Entity.Null && _engine.World.IsAlive(target);
        }

        private string DescribeTarget()
        {
            Entity target = ReadTarget();
            if (target == Entity.Null || !_engine.World.IsAlive(target))
            {
                return "<none>";
            }

            return _engine.World.TryGet(target, out Name name) ? name.Value : target.Id.ToString();
        }

        private string DescribeExec()
        {
            if (!_engine.World.Has<AbilityExecInstance>(Hero))
            {
                return "<none>";
            }

            AbilityExecInstance exec = _engine.World.Get<AbilityExecInstance>(Hero);
            return $"slot={exec.AbilitySlot} hasPos={exec.HasTargetPos} target={exec.Target.Id}";
        }

        private string DescribeActiveOrder()
        {
            if (!_engine.World.TryGet(Hero, out OrderBuffer buffer) || !buffer.HasActive)
            {
                return "<none>";
            }

            return buffer.ActiveOrder.Order.OrderTypeId.ToString();
        }

        private string TriggerErrors()
        {
            if (_engine.TriggerManager.Errors.Count == 0)
            {
                return "<none>";
            }

            var parts = new List<string>();
            int limit = Math.Min(4, _engine.TriggerManager.Errors.Count);
            for (int i = 0; i < limit; i++)
            {
                var error = _engine.TriggerManager.Errors[i];
                parts.Add($"{error.TriggerName}: {error.Exception.Message}");
            }

            return string.Join(" | ", parts);
        }

        private static string SlotKey(int slot) => slot switch
        {
            0 => "q",
            1 => "w",
            2 => "e",
            3 => "r",
            _ => throw new ArgumentOutOfRangeException(nameof(slot)),
        };

        private static string SlotButton(int slot) => slot switch
        {
            0 => "<Gamepad>/buttonSouth",
            1 => "<Gamepad>/buttonWest",
            2 => "<Gamepad>/buttonEast",
            3 => "<Gamepad>/buttonNorth",
            _ => throw new ArgumentOutOfRangeException(nameof(slot)),
        };

        private static string FindRepoRoot()
        {
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (directory != null)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, "assets")) &&
                    Directory.Exists(Path.Combine(directory.FullName, "mods")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repository root.");
        }

        private sealed class SyntheticBackend : IInputBackend
        {
            private readonly SyntheticInputDevice _device;

            public SyntheticBackend(SyntheticInputDevice device) => _device = device;

            public float GetAxis(string devicePath)
            {
                if (!GamepadControlPath.TryParse(devicePath, out GamepadControlPath parsed))
                {
                    return 0f;
                }

                return _device.TryGetGamepadAxis(parsed, out float value) ? value : 0f;
            }

            public bool GetButton(string devicePath)
            {
                if (GamepadControlPath.TryParse(devicePath, out GamepadControlPath gamepad) &&
                    gamepad.Kind is GamepadControlKind.Button or GamepadControlKind.Trigger)
                {
                    return _device.IsGamepadButtonDown(gamepad);
                }

                if (devicePath.StartsWith("<Mouse>/", StringComparison.OrdinalIgnoreCase))
                {
                    string button = devicePath.Substring(8).ToUpperInvariant();
                    return button switch
                    {
                        "LEFTBUTTON" => _device.IsButtonDown(SyntheticPointerButton.Left),
                        "RIGHTBUTTON" => _device.IsButtonDown(SyntheticPointerButton.Right),
                        "MIDDLEBUTTON" => _device.IsButtonDown(SyntheticPointerButton.Middle),
                        _ => false,
                    };
                }

                if (devicePath.StartsWith("<Keyboard>/", StringComparison.OrdinalIgnoreCase))
                {
                    return _device.IsKeyDown(SyntheticInputDevice.NormalizeKey(devicePath.Substring(11)));
                }

                return false;
            }

            public Vector2 GetMousePosition() =>
                _device.HasPointerOverride ? _device.PointerPosition : new Vector2(-1f, -1f);

            public float GetMouseWheel() => _device.WheelDeltaThisFrame;

            public void EnableIME(bool enable) { }

            public void SetIMECandidatePosition(int x, int y) { }

            public string GetCharBuffer() => string.Empty;
        }

        private sealed class CmScreenProjector : IScreenRayProvider, IScreenProjector
        {
            public Vector2 WorldToScreen(Vector3 positionMeters) =>
                new(positionMeters.X * 100f, positionMeters.Z * 100f);

            public ScreenRay GetRay(Vector2 screenPosition) =>
                new(new Vector3(screenPosition.X / 100f, 1000f, screenPosition.Y / 100f), -Vector3.UnitY);
        }

        private sealed class HeadlessView : IViewController
        {
            public HeadlessView(float width, float height) => Resolution = new Vector2(width, height);

            public Vector2 Resolution { get; }
            public float Fov => 50f;
            public float AspectRatio => Resolution.Y <= 0f ? 1f : Resolution.X / Resolution.Y;
        }
    }
}
