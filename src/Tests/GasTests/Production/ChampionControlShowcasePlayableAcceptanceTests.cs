using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.Selection;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using Ludots.UI;
using Ludots.UI.Skia;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production
{
    [NonParallelizable]
    [TestFixture]
    public sealed class ChampionControlShowcasePlayableAcceptanceTests
    {
        private const float DeltaTime = 1f / 60f;
        private const string ControlMapId = "champion_control_showcase";
        private const int ImageWidth = 1600;
        private const int ImageHeight = 900;
        private const float WorldMinX = 2140f;
        private const float WorldMaxX = 3200f;
        private const float WorldMinY = 720f;
        private const float WorldMaxY = 1180f;
        private const int MarshalSlowSlot = 0;
        private const int MarshalSilenceSlot = 1;
        private const int MarshalRootSlot = 2;
        private const int MarshalStunSlot = 3;
        private const int CasterArcPulseSlot = 0;

        private static readonly string[] AcceptanceMods =
        {
            "LudotsCoreMod",
            "CommonControlBuffsMod",
            "CommonControlBuffsPresentationMod",
            "CoreInputMod",
            "CameraProfilesMod",
            "DiagnosticsOverlayMod",
            "EntityCommandPanelMod",
            "ChampionSkillSandboxMod"
        };

        [Test]
        public void ChampionControlShowcase_PlayableAcceptance_WritesArtifactsAndScreens()
        {
            string repoRoot = FindRepoRoot();
            string artifactDir = Path.Combine(repoRoot, "artifacts", "acceptance", "champion-control-showcase");
            string screensDir = Path.Combine(artifactDir, "screens");
            Directory.CreateDirectory(screensDir);

            var timeline = new List<string>();
            var snapshots = new List<ControlShowcaseSnapshot>();
            var captureFrames = new List<CaptureFrame>();
            var frameTimesMs = new List<double>();

            using var engine = CreateEngine();
            var overlay = engine.GetService(CoreServiceKeys.ScreenOverlayBuffer)
                ?? throw new InvalidOperationException("ScreenOverlayBuffer missing.");
            var orderQueue = engine.GetService(CoreServiceKeys.OrderQueue)
                ?? throw new InvalidOperationException("OrderQueue missing.");
            var abilities = engine.GetService(CoreServiceKeys.AbilityDefinitionRegistry)
                ?? throw new InvalidOperationException("AbilityDefinitionRegistry missing.");
            var gameConfig = engine.GetService(CoreServiceKeys.GameConfig)
                ?? throw new InvalidOperationException("GameConfig missing.");

            int moveToOrderTypeId = gameConfig.Constants.OrderTypeIds["moveTo"];
            int castAbilityOrderTypeId = gameConfig.Constants.OrderTypeIds["castAbility"];
            var planner = new CompositeOrderPlanner(engine.World, orderQueue, abilities, castAbilityOrderTypeId, moveToOrderTypeId);

            LoadMap(engine, ControlMapId, frameTimesMs);

            Entity marshal = FindEntityByName(engine.World, "Control Marshal");
            Entity runner = FindEntityByName(engine.World, "Control Runner");
            Entity caster = FindEntityByName(engine.World, "Control Caster");

            SelectEntity(engine, marshal, frameTimesMs);
            CaptureSnapshot(engine, overlay, snapshots, captureFrames, screensDir, 1, "loaded", "Control showcase booted with the marshal selected.");
            timeline.Add("[T+001] Control showcase loaded | marshal selected | overlay exposes Q slow / W silence / E root / R stun");

            SubmitMoveOrder(orderQueue, runner, 2, moveToOrderTypeId, new Vector2(3140f, 1060f));
            Vector2 runnerBaselineStart = ReadPosition(engine.World, "Control Runner");
            Tick(engine, 24, frameTimesMs);
            float baselineRunnerTravel = Vector2.Distance(runnerBaselineStart, ReadPosition(engine.World, "Control Runner"));
            Assert.That(baselineRunnerTravel, Is.GreaterThan(60f));

            Assert.That(SubmitCastOrder(planner, castAbilityOrderTypeId, marshal, runner, 1, MarshalSlowSlot), Is.True);
            Tick(engine, 2, frameTimesMs);
            SelectEntity(engine, runner, frameTimesMs);
            Vector2 runnerSlowStart = ReadPosition(engine.World, "Control Runner");
            Tick(engine, 24, frameTimesMs);
            float slowRunnerTravel = Vector2.Distance(runnerSlowStart, ReadPosition(engine.World, "Control Runner"));
            (float runnerSlowCurrent, float runnerSlowBase) = ReadMoveSpeed(engine.World, runner);
            Assert.That(HasEffectiveTag(engine, runner, "Status.Slowed"), Is.True);
            Assert.That(runnerSlowCurrent, Is.LessThan(runnerSlowBase));
            Assert.That(slowRunnerTravel, Is.LessThan(baselineRunnerTravel * 0.8f));
            CaptureSnapshot(engine, overlay, snapshots, captureFrames, screensDir, 2, "slow", "Marshal Q applies a heavy slow through the MoveSpeed chain and preserves movement.");
            timeline.Add($"[T+002] Marshal Q -> Runner | Slow | MoveSpeed {runnerSlowBase:0}->{runnerSlowCurrent:0} | travel {baselineRunnerTravel:0.#}cm -> {slowRunnerTravel:0.#}cm");

            Assert.That(SubmitCastOrder(planner, castAbilityOrderTypeId, marshal, runner, 1, MarshalRootSlot), Is.True);
            Tick(engine, 2, frameTimesMs);
            Vector2 runnerRootStart = ReadPosition(engine.World, "Control Runner");
            Tick(engine, 18, frameTimesMs);
            Vector2 runnerRootEnd = ReadPosition(engine.World, "Control Runner");
            GameplayControlState rootedState = GameplayControlStateResolver.GetOrDefault(engine.World, runner);
            Assert.That(Vector2.Distance(runnerRootStart, runnerRootEnd), Is.LessThanOrEqualTo(8f));
            Assert.That(HasEffectiveTag(engine, runner, "Status.Rooted"), Is.True);
            Assert.That(rootedState.IsMoveBlocked(), Is.True);
            Assert.That(engine.World.Get<NavKinematics2D>(runner).MaxSpeedCmPerSec.ToFloat(), Is.EqualTo(0f).Within(0.01f));
            CaptureSnapshot(engine, overlay, snapshots, captureFrames, screensDir, 3, "root", "Marshal E projects move-block through the control-state sink.");
            timeline.Add("[T+003] Marshal E -> Runner | Root | MoveBlocked active | control sink drives nav max speed to 0");

            TickUntil(engine, frameTimesMs, () => !HasEffectiveTag(engine, runner, "Status.Rooted"), 180);
            SubmitMoveOrder(orderQueue, runner, 2, moveToOrderTypeId, new Vector2(2180f, 1060f));
            Vector2 runnerRecoverStart = ReadPosition(engine.World, "Control Runner");
            Tick(engine, 24, frameTimesMs);
            float runnerRecoverTravel = Vector2.Distance(runnerRecoverStart, ReadPosition(engine.World, "Control Runner"));
            Assert.That(runnerRecoverTravel, Is.GreaterThan(40f));

            Assert.That(SubmitCastOrder(planner, castAbilityOrderTypeId, marshal, runner, 1, MarshalStunSlot), Is.True);
            Tick(engine, 2, frameTimesMs);
            Vector2 runnerStunStart = ReadPosition(engine.World, "Control Runner");
            Tick(engine, 18, frameTimesMs);
            Vector2 runnerStunEnd = ReadPosition(engine.World, "Control Runner");
            GameplayControlState stunnedRunnerState = GameplayControlStateResolver.GetOrDefault(engine.World, runner);
            Assert.That(Vector2.Distance(runnerStunStart, runnerStunEnd), Is.LessThanOrEqualTo(24f));
            Assert.That(HasEffectiveTag(engine, runner, "Status.Stunned"), Is.True);
            Assert.That(stunnedRunnerState.IsMoveBlocked(), Is.True);
            Assert.That(stunnedRunnerState.ActionBlocked, Is.EqualTo((byte)1));
            Assert.That(engine.World.Get<NavKinematics2D>(runner).MaxSpeedCmPerSec.ToFloat(), Is.EqualTo(0f).Within(0.01f));
            CaptureSnapshot(engine, overlay, snapshots, captureFrames, screensDir, 4, "stun_runner", "Marshal R blocks action and movement through the shared reusable mod.");
            timeline.Add("[T+004] Marshal R -> Runner | Stun | ActionBlocked=1 | movement and action both gated");

            TickUntil(engine, frameTimesMs, () => !HasAbilityExec(engine.World, caster), 48);
            Assert.That(SubmitCastOrder(planner, castAbilityOrderTypeId, caster, marshal, 2, CasterArcPulseSlot), Is.True);
            float marshalHealthBeforeBaselineCast = ReadHealth(engine.World, "Control Marshal");
            TickUntil(engine, frameTimesMs, () => ReadHealth(engine.World, "Control Marshal") < marshalHealthBeforeBaselineCast, 80);
            float marshalHealthAfterBaselineCast = ReadHealth(engine.World, "Control Marshal");
            Assert.That(marshalHealthAfterBaselineCast, Is.EqualTo(marshalHealthBeforeBaselineCast - 10f).Within(0.001f));
            CaptureSnapshot(engine, overlay, snapshots, captureFrames, screensDir, 5, "baseline_cast", "Caster baseline cast lands before control gates are applied.");
            timeline.Add($"[T+005] Caster -> Marshal | Arc Pulse hit | HP {marshalHealthBeforeBaselineCast:0}->{marshalHealthAfterBaselineCast:0}");

            TickUntil(engine, frameTimesMs, () => !HasEffectiveTag(engine, caster, "Cooldown.ControlShowcase.Caster.Q"), 180);
            Assert.That(SubmitCastOrder(planner, castAbilityOrderTypeId, marshal, caster, 1, MarshalSilenceSlot), Is.True);
            Tick(engine, 2, frameTimesMs);
            SelectEntity(engine, caster, frameTimesMs);
            float marshalHealthBeforeSilence = ReadHealth(engine.World, "Control Marshal");
            _ = SubmitCastOrder(planner, castAbilityOrderTypeId, caster, marshal, 2, CasterArcPulseSlot);
            Tick(engine, 8, frameTimesMs);
            GameplayControlState silencedCasterState = GameplayControlStateResolver.GetOrDefault(engine.World, caster);
            Assert.That(HasEffectiveTag(engine, caster, "Status.Silenced"), Is.True);
            Assert.That(HasAbilityExec(engine.World, caster), Is.False);
            Assert.That(HasEffectiveTag(engine, caster, "Cooldown.ControlShowcase.Caster.Q"), Is.False);
            Assert.That(ReadHealth(engine.World, "Control Marshal"), Is.EqualTo(marshalHealthBeforeSilence).Within(0.001f));
            Assert.That(silencedCasterState.ActionBlocked, Is.EqualTo((byte)1));
            Assert.That(silencedCasterState.IsMoveBlocked(), Is.False);
            CaptureSnapshot(engine, overlay, snapshots, captureFrames, screensDir, 6, "silence", "Marshal W projects action-block without affecting movement.");
            timeline.Add("[T+006] Marshal W -> Caster | Silence | cast startup rejected before exec starts");

            TickUntil(engine, frameTimesMs, () => !HasEffectiveTag(engine, caster, "Status.Silenced"), 240);
            TickUntil(engine, frameTimesMs, () => !HasAbilityExec(engine.World, caster), 48);
            TickUntil(engine, frameTimesMs, () => !HasEffectiveTag(engine, caster, "Cooldown.ControlShowcase.Caster.Q"), 180);

            float marshalHealthBeforeStunInterrupt = ReadHealth(engine.World, "Control Marshal");
            Assert.That(SubmitCastOrder(planner, castAbilityOrderTypeId, caster, marshal, 2, CasterArcPulseSlot), Is.True);
            TickUntil(engine, frameTimesMs, () => HasAbilityExec(engine.World, caster), 24);
            Tick(engine, 6, frameTimesMs);
            Assert.That(SubmitCastOrder(planner, castAbilityOrderTypeId, marshal, caster, 1, MarshalStunSlot), Is.True);
            Tick(engine, 2, frameTimesMs);
            Tick(engine, 24, frameTimesMs);
            GameplayControlState stunnedCasterState = GameplayControlStateResolver.GetOrDefault(engine.World, caster);
            Assert.That(HasEffectiveTag(engine, caster, "Status.Stunned"), Is.True);
            Assert.That(HasAbilityExec(engine.World, caster), Is.False);
            Assert.That(ReadHealth(engine.World, "Control Marshal"), Is.EqualTo(marshalHealthBeforeStunInterrupt).Within(0.001f));
            Assert.That(stunnedCasterState.ActionBlocked, Is.EqualTo((byte)1));
            CaptureSnapshot(engine, overlay, snapshots, captureFrames, screensDir, 7, "stun_interrupt", "Marshal R interrupts an active cast and blocks follow-up action.");
            timeline.Add("[T+007] Marshal R -> Caster | Stun mid-cast | active exec interrupted before Arc Pulse damage resolves");

            File.WriteAllText(Path.Combine(artifactDir, "battle-report.md"), BuildBattleReport(timeline, snapshots, frameTimesMs, baselineRunnerTravel, slowRunnerTravel, runnerRecoverTravel));
            File.WriteAllText(Path.Combine(artifactDir, "trace.jsonl"), BuildTraceJsonl(snapshots));
            File.WriteAllText(Path.Combine(artifactDir, "path.mmd"), BuildPathMermaid());
            WriteTimelineSvg(captureFrames, Path.Combine(screensDir, "timeline.svg"));
        }

        private static GameEngine CreateEngine()
        {
            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");
            var modPaths = RepoModPaths.ResolveExplicit(repoRoot, AcceptanceMods);
            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(modPaths, assetsRoot);
            InstallInput(engine);
            InstallUi(engine);
            engine.Start();
            return engine;
        }

        private static void InstallInput(GameEngine engine)
        {
            var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
            var backend = new NullInputBackend();
            var inputHandler = new PlayerInputHandler(backend, inputConfig);
            for (int i = 0; i < engine.MergedConfig.StartupInputContexts.Count; i++)
            {
                inputHandler.PushContext(engine.MergedConfig.StartupInputContexts[i]);
            }

            engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
            engine.SetService(CoreServiceKeys.InputBackend, (IInputBackend)backend);
            engine.SetService(CoreServiceKeys.UiCaptured, false);
        }

        private static void InstallUi(GameEngine engine)
        {
            var uiRoot = new UIRoot(new SkiaUiRenderer());
            uiRoot.Resize(1920f, 1080f);
            engine.SetService(CoreServiceKeys.UIRoot, uiRoot);
            engine.SetService(CoreServiceKeys.UiTextMeasurer, (object)new SkiaTextMeasurer());
            engine.SetService(CoreServiceKeys.UiImageSizeProvider, (object)new SkiaImageSizeProvider());
        }

        private static void LoadMap(GameEngine engine, string mapId, List<double> frameTimesMs, int frames = 12)
        {
            engine.LoadMap(mapId);
            Assert.That(engine.CurrentMapSession, Is.Not.Null, $"{mapId} should create a live map session.");
            Tick(engine, frames, frameTimesMs);
            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));
        }

        private static void Tick(GameEngine engine, int frames, List<double> frameTimesMs)
        {
            for (int i = 0; i < frames; i++)
            {
                long t0 = Stopwatch.GetTimestamp();
                engine.SetService(CoreServiceKeys.UiCaptured, false);
                engine.Tick(DeltaTime);
                frameTimesMs.Add((Stopwatch.GetTimestamp() - t0) * 1000d / Stopwatch.Frequency);
            }
        }

        private static void TickUntil(GameEngine engine, List<double> frameTimesMs, Func<bool> predicate, int maxFrames)
        {
            for (int i = 0; i < maxFrames; i++)
            {
                if (predicate())
                {
                    return;
                }

                Tick(engine, 1, frameTimesMs);
            }

            Assert.That(predicate(), Is.True, $"Predicate was not satisfied within {maxFrames} frames.");
        }

        private static void SelectEntity(GameEngine engine, Entity target, List<double> frameTimesMs)
        {
            SelectionRuntime selection = engine.GetService(CoreServiceKeys.SelectionRuntime)
                ?? throw new InvalidOperationException("SelectionRuntime missing.");
            Entity owner = engine.GetService(CoreServiceKeys.LocalPlayerEntity);
            if (!engine.World.IsAlive(owner))
            {
                owner = target;
                engine.GlobalContext[CoreServiceKeys.LocalPlayerEntity.Name] = owner;
            }

            Span<Entity> next = stackalloc Entity[1];
            next[0] = target;
            selection.ReplaceSelection(owner, SelectionSetKeys.Ambient, next);
            Tick(engine, 1, frameTimesMs);
        }

        private static bool SubmitCastOrder(CompositeOrderPlanner planner, int castAbilityOrderTypeId, Entity actor, Entity target, int playerId, int slotIndex)
        {
            var order = new Order
            {
                OrderTypeId = castAbilityOrderTypeId,
                PlayerId = playerId,
                Actor = actor,
                Target = target,
                SubmitMode = OrderSubmitMode.Immediate,
                Args = new OrderArgs { I0 = slotIndex }
            };
            return planner.TrySubmit(in order);
        }

        private static void SubmitMoveOrder(OrderQueue orderQueue, Entity actor, int playerId, int moveToOrderTypeId, Vector2 targetWorldCm)
        {
            var order = new Order
            {
                OrderTypeId = moveToOrderTypeId,
                PlayerId = playerId,
                Actor = actor,
                SubmitMode = OrderSubmitMode.Immediate,
                Args = new OrderArgs
                {
                    Spatial = new OrderSpatial
                    {
                        Kind = OrderSpatialKind.WorldCm,
                        Mode = OrderCollectionMode.Single,
                        WorldCm = new Vector3(targetWorldCm.X, 0f, targetWorldCm.Y)
                    }
                }
            };

            Assert.That(orderQueue.TryEnqueueAssigned(ref order), Is.True);
        }

        private static void CaptureSnapshot(
            GameEngine engine,
            ScreenOverlayBuffer overlay,
            List<ControlShowcaseSnapshot> snapshots,
            List<CaptureFrame> captureFrames,
            string screensDir,
            int frameIndex,
            string step,
            string note)
        {
            Entity marshal = FindEntityByName(engine.World, "Control Marshal");
            Entity runner = FindEntityByName(engine.World, "Control Runner");
            Entity caster = FindEntityByName(engine.World, "Control Caster");

            var snapshot = new ControlShowcaseSnapshot(
                frameIndex,
                step,
                note,
                GetSelectedEntityName(engine),
                ReadOverlayLines(overlay),
                ReadActorSnapshot(engine, marshal),
                ReadActorSnapshot(engine, runner),
                ReadActorSnapshot(engine, caster));
            snapshots.Add(snapshot);

            string fileName = $"{frameIndex:000}.svg";
            WriteSnapshotSvg(snapshot, Path.Combine(screensDir, fileName));
            captureFrames.Add(new CaptureFrame(frameIndex, step, fileName));
        }

        private static ActorSnapshot ReadActorSnapshot(GameEngine engine, Entity entity)
        {
            string name = engine.World.TryGet(entity, out Name actorName) ? actorName.Value : $"Entity#{entity.Id}";
            Vector2 position = ReadPosition(engine.World, name);
            (float currentMoveSpeed, float baseMoveSpeed) = ReadMoveSpeed(engine.World, entity);
            GameplayControlState controlState = GameplayControlStateResolver.GetOrDefault(engine.World, entity);
            return new ActorSnapshot(
                name,
                position.X,
                position.Y,
                ReadHealth(engine.World, name),
                currentMoveSpeed,
                baseMoveSpeed,
                ReadTagSummary(engine, entity),
                controlState.IsMoveBlocked(),
                controlState.ActionBlocked != 0,
                HasAbilityExec(engine.World, entity),
                ReadExecState(engine.World, entity));
        }

        private static string GetSelectedEntityName(GameEngine engine)
        {
            return SelectionContextRuntime.TryGetCurrentPrimary(engine.World, engine.GlobalContext, out Entity selected) &&
                   engine.World.TryGet(selected, out Name name)
                ? name.Value
                : string.Empty;
        }

        private static IReadOnlyList<string> ReadOverlayLines(ScreenOverlayBuffer overlay)
        {
            var lines = new List<string>(8);
            foreach (ref readonly var item in overlay.GetSpan())
            {
                if (item.Kind != ScreenOverlayItemKind.Text)
                {
                    continue;
                }

                string? text = overlay.GetString(item.StringId);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    lines.Add(text);
                }
            }

            return lines;
        }

        private static string ReadTagSummary(GameEngine engine, Entity entity)
        {
            var labels = new List<string>(6);
            AddTagIfActive(engine, entity, labels, "Status.Slowed", "Slowed");
            AddTagIfActive(engine, entity, labels, "Status.Rooted", "Rooted");
            AddTagIfActive(engine, entity, labels, "Status.Stunned", "Stunned");
            AddTagIfActive(engine, entity, labels, "Status.Silenced", "Silenced");
            AddTagIfActive(engine, entity, labels, "Status.CannotMove", "CannotMove");
            AddTagIfActive(engine, entity, labels, "Status.CannotCast", "CannotCast");
            AddTagIfActive(engine, entity, labels, "Cooldown.ControlShowcase.Caster.Q", "CooldownQ");
            return labels.Count == 0 ? "(none)" : string.Join(", ", labels);
        }

        private static void AddTagIfActive(GameEngine engine, Entity entity, List<string> labels, string tagName, string label)
        {
            if (HasEffectiveTag(engine, entity, tagName))
            {
                labels.Add(label);
            }
        }

        private static bool HasEffectiveTag(GameEngine engine, Entity entity, string tagName)
        {
            int tagId = TagRegistry.GetId(tagName);
            if (tagId <= 0 || !engine.World.TryGet(entity, out GameplayTagContainer tags))
            {
                return false;
            }

            TagOps? tagOps = engine.GetService(CoreServiceKeys.TagOps);
            return tagOps != null
                ? tagOps.HasTag(ref tags, tagId, TagSense.Effective)
                : tags.HasTag(tagId);
        }

        private static bool HasAbilityExec(World world, Entity entity)
        {
            return world.IsAlive(entity) && world.Has<AbilityExecInstance>(entity);
        }

        private static string ReadExecState(World world, Entity entity)
        {
            return world.TryGet(entity, out AbilityExecInstance exec)
                ? exec.State.ToString()
                : "Idle";
        }

        private static (float Current, float Base) ReadMoveSpeed(World world, Entity entity)
        {
            int moveSpeedId = AttributeRegistry.Register("MoveSpeed");
            if (!world.TryGet(entity, out AttributeBuffer attributes))
            {
                return (0f, 0f);
            }

            return (attributes.GetCurrent(moveSpeedId), attributes.GetBase(moveSpeedId));
        }

        private static Vector2 ReadPosition(World world, string entityName)
        {
            Entity entity = FindEntityByName(world, entityName);
            Assert.That(world.TryGet(entity, out WorldPositionCm position), Is.True);
            var worldCm = position.ToWorldCmInt2();
            return new Vector2(worldCm.X, worldCm.Y);
        }

        private static float ReadHealth(World world, string entityName)
        {
            Entity entity = FindEntityByName(world, entityName);
            int healthId = AttributeRegistry.GetId("Health");
            Assert.That(healthId, Is.GreaterThanOrEqualTo(0));
            Assert.That(world.TryGet(entity, out AttributeBuffer attributes), Is.True);
            return attributes.GetCurrent(healthId);
        }

        private static Entity FindEntityByName(World world, string entityName)
        {
            Entity found = Entity.Null;
            var query = new QueryDescription().WithAll<Name>();
            world.Query(in query, (Entity entity, ref Name name) =>
            {
                if (found == Entity.Null && string.Equals(name.Value, entityName, StringComparison.Ordinal))
                {
                    found = entity;
                }
            });

            Assert.That(found, Is.Not.EqualTo(Entity.Null), $"Entity '{entityName}' should exist on {ControlMapId}.");
            return found;
        }

        private static void WriteSnapshotSvg(ControlShowcaseSnapshot snapshot, string path)
        {
            Vector2 marshalPoint = ToStagePoint(new Vector2(snapshot.Marshal.PositionX, snapshot.Marshal.PositionY));
            Vector2 runnerPoint = ToStagePoint(new Vector2(snapshot.Runner.PositionX, snapshot.Runner.PositionY));
            Vector2 casterPoint = ToStagePoint(new Vector2(snapshot.Caster.PositionX, snapshot.Caster.PositionY));

            string overlayLines = string.Join(
                Environment.NewLine,
                snapshot.OverlayLines.Take(12).Select((line, index) =>
                    $"""<text x="950" y="{188 + index * 22}" fill="#e0e8f0" font-size="18" font-family="Consolas, monospace">{EscapeSvg(TrimForPaint(line, 74))}</text>"""));

            string svg = $$"""
<svg xmlns="http://www.w3.org/2000/svg" width="{{ImageWidth}}" height="{{ImageHeight}}" viewBox="0 0 {{ImageWidth}} {{ImageHeight}}">
  <rect width="{{ImageWidth}}" height="{{ImageHeight}}" fill="#0b1018" />
  <text x="40" y="42" fill="#ffffff" font-size="30" font-family="Consolas, monospace">Champion Control Showcase | {{snapshot.FrameIndex:000}} {{EscapeSvg(snapshot.Step)}}</text>
  <text x="40" y="70" fill="#f4d074" font-size="18" font-family="Consolas, monospace">{{EscapeSvg(snapshot.Note)}}</text>
  <rect x="40" y="90" width="860" height="760" rx="18" fill="#152130" stroke="#5b7fa0" stroke-width="2" />
  <circle cx="{{marshalPoint.X:0.##}}" cy="{{marshalPoint.Y:0.##}}" r="18" fill="#62baff" />
  <text x="{{marshalPoint.X + 24:0.##}}" y="{{marshalPoint.Y + 6:0.##}}" fill="#ffffff" font-size="18" font-family="Consolas, monospace">Marshal</text>
  <circle cx="{{runnerPoint.X:0.##}}" cy="{{runnerPoint.Y:0.##}}" r="18" fill="#76e88c" />
  <text x="{{runnerPoint.X + 24:0.##}}" y="{{runnerPoint.Y + 6:0.##}}" fill="#ffffff" font-size="18" font-family="Consolas, monospace">Runner</text>
  <circle cx="{{casterPoint.X:0.##}}" cy="{{casterPoint.Y:0.##}}" r="18" fill="#ff926a" />
  <text x="{{casterPoint.X + 24:0.##}}" y="{{casterPoint.Y + 6:0.##}}" fill="#ffffff" font-size="18" font-family="Consolas, monospace">Caster</text>
  {{RenderActorBlock(snapshot.SelectedEntity, snapshot.Marshal, snapshot.Runner, snapshot.Caster)}}
  <text x="950" y="166" fill="#ffffff" font-size="20" font-family="Consolas, monospace">Overlay</text>
  {{overlayLines}}
</svg>
""";

            File.WriteAllText(path, svg);
        }

        private static string RenderActorBlock(string selectedEntity, ActorSnapshot marshal, ActorSnapshot runner, ActorSnapshot caster)
        {
            return string.Join(
                Environment.NewLine,
                RenderActorLines(selectedEntity, marshal, 96),
                RenderActorLines(selectedEntity, runner, 246),
                RenderActorLines(selectedEntity, caster, 396));
        }

        private static IEnumerable<string> RenderActorLines(string selectedEntity, ActorSnapshot actor, int top)
        {
            yield return $"""<text x="950" y="{top}" fill="#ffffff" font-size="20" font-family="Consolas, monospace">{EscapeSvg(actor.Name)}{(string.Equals(actor.Name, selectedEntity, StringComparison.Ordinal) ? " [Selected]" : string.Empty)}</text>""";
            yield return $"""<text x="950" y="{top + 26}" fill="#e0e8f0" font-size="18" font-family="Consolas, monospace">HP {actor.Health:0} | Pos ({actor.PositionX:0}, {actor.PositionY:0})</text>""";
            yield return $"""<text x="950" y="{top + 48}" fill="#e0e8f0" font-size="18" font-family="Consolas, monospace">MoveSpeed {actor.MoveSpeedCurrent:0.#}/{actor.MoveSpeedBase:0.#} | moveBlocked={actor.MoveBlocked} | actionBlocked={actor.ActionBlocked}</text>""";
            yield return $"""<text x="950" y="{top + 70}" fill="#e0e8f0" font-size="18" font-family="Consolas, monospace">Tags {EscapeSvg(TrimForPaint(actor.Tags, 70))}</text>""";
            yield return $"""<text x="950" y="{top + 92}" fill="#e0e8f0" font-size="18" font-family="Consolas, monospace">Exec {(actor.HasExec ? EscapeSvg(actor.ExecState) : "Idle")}</text>""";
        }

        private static void WriteTimelineSvg(IReadOnlyList<CaptureFrame> frames, string path)
        {
            if (frames.Count == 0)
            {
                return;
            }

            string lines = string.Join(
                Environment.NewLine,
                frames.Select((frame, index) =>
                    $"""<text x="40" y="{100 + index * 36}" fill="#e0e8f0" font-size="22" font-family="Consolas, monospace">{frame.FrameIndex:000} | {EscapeSvg(frame.Step)} | {EscapeSvg(frame.FileName)}</text>"""));

            string svg = $$"""
<svg xmlns="http://www.w3.org/2000/svg" width="1600" height="{{Math.Max(240, 140 + frames.Count * 36)}}" viewBox="0 0 1600 {{Math.Max(240, 140 + frames.Count * 36)}}">
  <rect width="1600" height="{{Math.Max(240, 140 + frames.Count * 36)}}" fill="#081018" />
  <text x="40" y="56" fill="#ffffff" font-size="30" font-family="Consolas, monospace">Champion control showcase screenshot timeline</text>
  {{lines}}
</svg>
""";

            File.WriteAllText(path, svg);
        }

        private static string BuildTraceJsonl(IReadOnlyList<ControlShowcaseSnapshot> snapshots)
        {
            var lines = new List<string>(snapshots.Count);
            for (int i = 0; i < snapshots.Count; i++)
            {
                ControlShowcaseSnapshot snapshot = snapshots[i];
                lines.Add(JsonSerializer.Serialize(new
                {
                    event_id = $"champion-control-{i + 1:000}",
                    frame = snapshot.FrameIndex,
                    step = snapshot.Step,
                    note = snapshot.Note,
                    selected_entity = snapshot.SelectedEntity,
                    marshal = snapshot.Marshal,
                    runner = snapshot.Runner,
                    caster = snapshot.Caster,
                    overlay = snapshot.OverlayLines
                }));
            }

            return string.Join(Environment.NewLine, lines);
        }

        private static string BuildBattleReport(IReadOnlyList<string> timeline, IReadOnlyList<ControlShowcaseSnapshot> snapshots, IReadOnlyList<double> frameTimesMs, float baselineRunnerTravel, float slowRunnerTravel, float runnerRecoverTravel)
        {
            double medianTickMs = Median(frameTimesMs);
            double maxTickMs = frameTimesMs.Count == 0 ? 0d : frameTimesMs.Max();
            ControlShowcaseSnapshot final = snapshots[^1];

            var sb = new StringBuilder();
            sb.AppendLine("# Scenario Card: champion-control-showcase");
            sb.AppendLine();
            sb.AppendLine("## Intent");
            sb.AppendLine("- Player goal: inspect and play a reusable control-buff showcase with visible slow, silence, root, and stun behavior.");
            sb.AppendLine("- Gameplay domain: real `ChampionSkillSandboxMod` map runtime plus reusable `CommonControlBuffsMod` effect/tag/sink infrastructure.");
            sb.AppendLine();
            sb.AppendLine("## Determinism Inputs");
            sb.AppendLine("- Seed: none");
            sb.AppendLine("- Map: `mods/showcases/champion_skill_sandbox/ChampionSkillSandboxMod/assets/Maps/champion_control_showcase.json`");
            sb.AppendLine("- Clock profile: fixed `1/60s`");
            sb.AppendLine("- Initial entities: `Control Marshal`, `Control Runner`, `Control Caster`");
            sb.AppendLine("- Evidence images: `artifacts/acceptance/champion-control-showcase/screens/*.svg`, `artifacts/acceptance/champion-control-showcase/screens/timeline.svg`");
            sb.AppendLine();
            sb.AppendLine("## Action Script");
            sb.AppendLine("1. Load the playable control showcase map and verify the overlay and marshal loadout.");
            sb.AppendLine("2. Drive the runner through the real move-order path, then fire the marshal's Q/E/R control skills through cast orders.");
            sb.AppendLine("3. Submit a real hostile cast from the caster, then fire the marshal's W/R control skills to prove startup rejection and active-cast interrupt.");
            sb.AppendLine("4. Write trace, path, battle report, and screenshot frames for human review.");
            sb.AppendLine();
            sb.AppendLine("## Timeline");
            foreach (string entry in timeline)
            {
                sb.AppendLine($"- {entry}");
            }

            sb.AppendLine();
            sb.AppendLine("## Outcome");
            sb.AppendLine("- result: success");
            sb.AppendLine($"- runner_baseline_travel_cm: {baselineRunnerTravel:0.#}");
            sb.AppendLine($"- runner_slowed_travel_cm: {slowRunnerTravel:0.#}");
            sb.AppendLine($"- runner_recovery_travel_cm: {runnerRecoverTravel:0.#}");
            sb.AppendLine($"- final_selected_entity: {final.SelectedEntity}");
            sb.AppendLine($"- final_runner_tags: {final.Runner.Tags}");
            sb.AppendLine($"- final_caster_tags: {final.Caster.Tags}");
            sb.AppendLine($"- final_caster_exec: {(final.Caster.HasExec ? final.Caster.ExecState : "Idle")}");
            sb.AppendLine();
            sb.AppendLine("## Summary Stats");
            sb.AppendLine($"- total_actions: {timeline.Count}");
            sb.AppendLine($"- screenshot_captures: {snapshots.Count}");
            sb.AppendLine("- reusable_effects_proven: slow, silence, root, stun");
            sb.AppendLine("- sink_projection_proven: move-block and action-block");
            sb.AppendLine("- cast_gate_reuse_proven: silence startup rejection, stun interrupt");
            sb.AppendLine($"- median_tick_ms: {medianTickMs:0.###}");
            sb.AppendLine($"- max_tick_ms: {maxTickMs:0.###}");
            return sb.ToString();
        }

        private static string BuildPathMermaid()
        {
            return string.Join(Environment.NewLine, new[]
            {
                "flowchart TD",
                "    A[Load champion_control_showcase] --> B[Submit runner move order]",
                "    B --> C[Apply Slow -> MoveSpeed drops but runner still moves]",
                "    C --> D[Apply Root -> move blocked and nav speed becomes 0]",
                "    D --> E[Root expires -> runner movement recovers]",
                "    E --> F[Apply Stun on runner -> ActionBlocked and move blocked]",
                "    F --> G[Submit baseline Arc Pulse cast -> marshal takes damage]",
                "    G --> H[Apply Silence on caster -> manual cast blocked before exec]",
                "    H --> I{Exec prevented and cooldown unchanged?}",
                "    I -->|no| X[Fail: silence wiring broken]",
                "    I -->|yes| J[Submit cast again and wait for active exec]",
                "    J --> K[Apply Stun mid-cast -> exec interrupted]",
                "    K --> L{Marshal HP unchanged after stun?}",
                "    L -->|no| Y[Fail: stun did not interrupt active cast]",
                "    L -->|yes| M[Write trace, battle report, path, SVG timeline]"
            }) + Environment.NewLine;
        }

        private static string TrimForPaint(string value, int maxChars)
        {
            return string.IsNullOrEmpty(value) || value.Length <= maxChars ? value : value[..Math.Max(0, maxChars - 3)] + "...";
        }

        private static Vector2 ToStagePoint(Vector2 world)
        {
            float x = 40f + (world.X - WorldMinX) / (WorldMaxX - WorldMinX) * 870f;
            float y = 840f - (world.Y - WorldMinY) / (WorldMaxY - WorldMinY) * 760f;
            return new Vector2(x, y);
        }

        private static string EscapeSvg(string value)
        {
            return value
                .Replace("&", "&amp;", StringComparison.Ordinal)
                .Replace("<", "&lt;", StringComparison.Ordinal)
                .Replace(">", "&gt;", StringComparison.Ordinal)
                .Replace("\"", "&quot;", StringComparison.Ordinal);
        }

        private static double Median(IReadOnlyList<double> values)
        {
            if (values.Count == 0)
            {
                return 0d;
            }

            double[] copy = values.ToArray();
            Array.Sort(copy);
            int mid = copy.Length / 2;
            return (copy.Length & 1) != 0 ? copy[mid] : (copy[mid - 1] + copy[mid]) * 0.5d;
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 10 && dir != null; i++)
            {
                string srcDir = Path.Combine(dir.FullName, "src");
                string assetsDir = Path.Combine(dir.FullName, "assets");
                if (Directory.Exists(srcDir) && Directory.Exists(assetsDir))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }

            throw new DirectoryNotFoundException("Failed to locate repository root from test output directory.");
        }

        private sealed record ControlShowcaseSnapshot(int FrameIndex, string Step, string Note, string SelectedEntity, IReadOnlyList<string> OverlayLines, ActorSnapshot Marshal, ActorSnapshot Runner, ActorSnapshot Caster);
        private sealed record ActorSnapshot(string Name, float PositionX, float PositionY, float Health, float MoveSpeedCurrent, float MoveSpeedBase, string Tags, bool MoveBlocked, bool ActionBlocked, bool HasExec, string ExecState);
        private sealed record CaptureFrame(int FrameIndex, string Step, string FileName);

        private sealed class NullInputBackend : IInputBackend
        {
            public float GetAxis(string devicePath) => 0f;
            public bool GetButton(string devicePath) => false;
            public Vector2 GetMousePosition() => Vector2.Zero;
            public float GetMouseWheel() => 0f;
            public void EnableIME(bool enable) { }
            public void SetIMECandidatePosition(int x, int y) { }
            public string GetCharBuffer() => string.Empty;
        }
    }
}
