using System.Numerics;
using CrowdSimulationTests.Parity;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav.Pathing;
using Ludots.Core.CrowdSimulation.Presentation;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Engine;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Utils;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace CrowdSimulationTests.Presentation;

/// <summary>
/// 指令在 tick 之外改了组或单位时（暂停时冲刷、等路径答复停摆的 tick、现场提交），
/// 调试视图按模板、队伍着色读到的运动内核镜像必须跟上：组号指向在册的组，稠密下标落在单位数以内。
/// </summary>
[TestFixture]
public sealed class CrowdDebugUnitMirrorTests
{
    private const int SpawnXCm = 480000;
    private const int SpawnYCm = 560000;

    [Test]
    public void OrderWhilePlaybackHeld_TemplateColoringSeesLiveGroups()
    {
        using var scene = Scene.Create(TimeSpan.Zero);
        var session = scene.Session;
        Assert.That(session.Step(out _), Is.True);

        session.PlaybackHeld = true;
        session.Commands.Schedule(session, new[] { new CrowdCommand(session.TickCount, Order(session, 700000)) });
        session.FlushDueCommands();

        AssertMirrorMatchesWorld(session);
        Assert.DoesNotThrow(() => scene.Draw(CrowdSimulationDebugViewConfig.ColorTemplate));
    }

    [Test]
    public void OrderDuringPathWaitStall_TemplateColoringSeesLiveGroups()
    {
        using var scene = Scene.Create(TimeSpan.FromSeconds(3));
        var session = scene.Session;
        session.Commands.Schedule(session, new[] { new CrowdCommand(session.TickCount, Order(session, 700000)) });
        bool stalled = false;
        for (int i = 0; i < 64 && !stalled; i++) stalled = !session.Step(out _);
        Assert.That(stalled, Is.True, "答复延迟 3 秒，到期 tick 应停摆");

        session.Commands.Schedule(session, new[] { new CrowdCommand(session.TickCount, Order(session, 400000)) });
        Assert.That(session.Step(out _), Is.False, "第一条指令的答复还没到，仍停摆");

        AssertMirrorMatchesWorld(session);
        Assert.DoesNotThrow(() => scene.Draw(CrowdSimulationDebugViewConfig.ColorTemplate));
    }

    [Test]
    public void LiveKillBetweenTicks_TeamColoringStaysInsideDenseRange()
    {
        using var scene = Scene.Create(TimeSpan.Zero);
        var session = scene.Session;
        Assert.That(session.Step(out _), Is.True);
        int before = session.Units.Count;
        var first = session.World.Get<Ludots.Core.Components.WorldPositionCm>(session.Units.EntityAt(0)).Value;

        session.Commands.Submit<object?>(session, new CrowdSimCommand
        {
            Kind = CrowdSimCommandKind.Kill,
            XCm = first.X.ToInt(),
            YCm = first.Y.ToInt(),
            RadiusCm = 600,
            RingScale = Fix64.FromInt(2),
            Impulse = Fix64.Zero,
        }, CrowdSimCommands.Exec);
        Assert.That(session.Units.Count, Is.LessThan(before), "爆炸半径内应有单位被移除");

        AssertMirrorMatchesWorld(session);
        Assert.DoesNotThrow(() => scene.Draw(CrowdSimulationDebugViewConfig.ColorTeam));
    }

    private static CrowdSimCommand Order(CrowdSimSession session, int xCm)
    {
        int members = session.Units.CopyOwnHandles(1, session.MemberCapture);
        return new CrowdSimCommand
        {
            Kind = CrowdSimCommandKind.Order,
            Player = 1,
            XCm = xCm,
            YCm = SpawnYCm,
            ShapeId = "box",
            Members = session.MemberCapture.AsSpan(0, members).ToArray(),
        };
    }

    private static void AssertMirrorMatchesWorld(CrowdSimSession session)
    {
        var movement = session.Movement!;
        for (int i = 0; i < session.Units.Count; i++)
        {
            var live = session.World.Get<CrowdSimulationUnitState>(session.Units.EntityAt(i));
            Assert.That(movement.States[i].GroupId, Is.EqualTo(live.GroupId), $"稠密下标 {i} 的镜像组号");
            Assert.That(session.Groups.TryGet(movement.States[i].GroupId, out _), Is.True, $"稠密下标 {i} 的组 {movement.States[i].GroupId} 不在册");
        }
    }

    private sealed class Scene : IDisposable
    {
        private readonly PathQueryService _service;
        private readonly GameEngine _engine;
        private readonly CrowdDebugViewFrame _frame;

        private Scene(CrowdSimSession session, PathQueryService service, GameEngine engine, CrowdDebugViewFrame frame)
        {
            Session = session;
            _service = service;
            _engine = engine;
            _frame = frame;
        }

        public CrowdSimSession Session { get; }

        public static Scene Create(TimeSpan replyDelay)
        {
            var (runtime, session) = S4DeployTruthTests.BuildSession("s7");
            var service = new PathQueryService(session.Navs, runtime, 1, TimeSpan.FromSeconds(30), session.ResolveNavContext)
            {
                TestDelayPerJob = replyDelay,
            };
            session.EnableMovement(CrowdMovementKernel.Create(session), new CrowdSimPlanner(session, service));
            Assert.That(CrowdDeployment.SpawnAt(session, Fix64.FromInt(SpawnXCm), Fix64.FromInt(SpawnYCm), 24, 1, 0, 0), Is.EqualTo(24));
            session.FlushDueCommands();

            var engine = new GameEngine();
            var ground = new FlatGround();
            engine.SetService(CoreServiceKeys.ScreenProjector, ground);
            engine.SetService(CoreServiceKeys.ScreenRayProvider, ground);
            engine.SetService(CoreServiceKeys.ContinuousHeightmap, ground);
            engine.SetService(CoreServiceKeys.ViewController, ground);
            engine.SetService(CoreServiceKeys.TeamColorPalette, new TeamColorPalette(new[]
            {
                default,
                TeamColorPalette.ParseHex("#4DB2FF", "test"),
                TeamColorPalette.ParseHex("#7ED957", "test"),
                TeamColorPalette.ParseHex("#FFC53D", "test"),
                TeamColorPalette.ParseHex("#C792FF", "test"),
            }, new[] { 1, 2, 3, 4 }));
            var frame = CrowdHudSamplingTests.ShippedDebugView();
            frame.BindEngine(engine);
            frame.SetViewport(SpawnXCm - 20000f, SpawnYCm - 20000f, SpawnXCm + 20000f, SpawnYCm + 20000f, 20f);
            return new Scene(session, service, engine, frame);
        }

        public void Draw(int colorMode)
        {
            _frame.UnitColorMode = colorMode;
            var overlay = new ScreenOverlayBuffer();
            _frame.Draw(Session, overlay);
            Assert.That(overlay.Count, Is.GreaterThan(0), "视口里应画出单位标记");
        }

        public void Dispose()
        {
            _service.Dispose();
            _engine.Dispose();
        }
    }

    private sealed class FlatGround : IScreenProjector, IScreenRayProvider, IContinuousHeightmap, IViewController
    {
        public Vector2 Resolution { get; } = new(1280, 720);
        public float Fov => 60f;
        public float AspectRatio => 1280f / 720f;

        public Vector2 WorldToScreen(Vector3 worldPosition) => new(640f, 360f);

        public ScreenRay GetRay(Vector2 screenPosition) => new(Vector3.Zero, -Vector3.UnitY);

        public bool TrySampleHeightCm(float worldXCm, float worldYCm, out float heightCm, int layerIndex = -1)
        {
            heightCm = 0f;
            return true;
        }

        public bool SampleHeightsCm(ReadOnlySpan<float> worldXCm, ReadOnlySpan<float> worldYCm, Span<float> outHeightCm, int layerIndex = -1)
        {
            outHeightCm.Clear();
            return true;
        }

        public bool TryRaycastGround(in ScreenRay ray, out VisualGroundHit hit, int layerIndex = -1)
        {
            hit = default;
            return false;
        }

        public bool RaycastGroundBatch(
            ReadOnlySpan<float> originXMeters, ReadOnlySpan<float> originYMeters, ReadOnlySpan<float> originZMeters,
            ReadOnlySpan<float> directionX, ReadOnlySpan<float> directionY, ReadOnlySpan<float> directionZ,
            Span<float> outWorldXCm, Span<float> outWorldYCm, Span<float> outHeightCm, Span<float> outDistanceMeters,
            Span<float> outNormalX, Span<float> outNormalY, Span<float> outNormalZ, Span<int> outLayerIndex, Span<byte> outHitMask,
            int layerIndex = -1) => false;
    }
}
