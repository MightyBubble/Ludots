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
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Presentation;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.Selection;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production
{
    [NonParallelizable]
    [TestFixture]
    public sealed class ResponseChainShowcasePlayableAcceptanceTests
    {
        private const float DeltaTime = 1f / 60f;
        private const string MapId = "response_chain_showcase";
        private const string TestInputBackendKey = "Tests.ResponseChainShowcase.InputBackend";

        private static readonly string[] AcceptanceMods =
        {
            "LudotsCoreMod",
            "CoreInputMod",
            "CameraProfilesMod",
            "ResponseChainShowcaseMod"
        };

        [Test]
        public void ResponseChainShowcase_PlayableFlow_WritesAcceptanceArtifacts()
        {
            string repoRoot = FindRepoRoot();
            string artifactDir = Path.Combine(repoRoot, "artifacts", "acceptance", "response-chain-showcase");
            Directory.CreateDirectory(artifactDir);

            var frameTimesMs = new List<double>();
            var timeline = new List<string>();
            var snapshots = new List<AcceptanceSnapshot>();

            using var engine = CreateEngine();
            var backend = GetInputBackend(engine);

            LoadMap(engine, MapId, frameTimesMs);
            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));
            Assert.That(GetSelectedEntityName(engine), Is.EqualTo("Conductor"));
            CaptureSnapshot(engine, snapshots, "map_loaded");
            timeline.Add("[T+001] response_chain_showcase loaded | Conductor auto-selected | HUD and response-window stack live");

            float comboBefore = ReadHealth(engine.World, "Combo Raider");
            SetMouseWorld(engine, backend, GetEntityScreen(engine, "Combo Raider"), frameTimesMs);
            PressButton(engine, backend, "<Keyboard>/q", frameTimesMs);
            TickUntil(engine, frameTimesMs, () => GetUiState(engine).Visible, maxFrames: 18);
            PressButton(engine, backend, "<Keyboard>/1", frameTimesMs);
            PressButton(engine, backend, "<Keyboard>/space", frameTimesMs);
            PressButton(engine, backend, "<Keyboard>/space", frameTimesMs);
            TickUntil(
                engine,
                frameTimesMs,
                () => ReadHealth(engine.World, "Combo Raider") < comboBefore,
                maxFrames: 24,
                failureMessageFactory: () => BuildResponseDiagnostics(engine));
            float comboAfter = ReadHealth(engine.World, "Combo Raider");
            Assert.That(comboAfter, Is.LessThan(comboBefore));
            CaptureSnapshot(engine, snapshots, "combo_finished");
            timeline.Add($"[T+002] Q on Combo Raider -> 1 follow-up -> pass-pass resolve | HP {comboBefore:0} -> {comboAfter:0}");

            PressButton(engine, backend, "<Keyboard>/f4", frameTimesMs);
            TickUntil(engine, frameTimesMs, () => Math.Abs(ReadHealth(engine.World, "Combo Raider") - comboBefore) < 0.01f, maxFrames: 18);
            CaptureSnapshot(engine, snapshots, "after_reset_one");
            timeline.Add("[T+003] F4 reset restored the drill board to its baseline state");

            float conductorBeforeCounter = ReadHealth(engine.World, "Conductor");
            float counterBefore = ReadHealth(engine.World, "Counter Raider");
            PressButton(engine, backend, "<Keyboard>/w", frameTimesMs);
            TickUntil(engine, frameTimesMs, () => GetUiState(engine).Visible, maxFrames: 18);
            PressButton(engine, backend, "<Keyboard>/n", frameTimesMs);
            PressButton(engine, backend, "<Keyboard>/space", frameTimesMs);
            PressButton(engine, backend, "<Keyboard>/space", frameTimesMs);
            TickUntil(
                engine,
                frameTimesMs,
                () => !GetUiState(engine).Visible &&
                      ReadHealth(engine.World, "Counter Raider") < counterBefore,
                maxFrames: 32,
                failureMessageFactory: () => BuildResponseDiagnostics(engine));
            float conductorAfterCounter = ReadHealth(engine.World, "Conductor");
            float counterAfter = ReadHealth(engine.World, "Counter Raider");
            Assert.That(counterAfter, Is.LessThan(counterBefore));
            Assert.That(conductorAfterCounter, Is.EqualTo(conductorBeforeCounter).Within(0.01f));
            CaptureSnapshot(engine, snapshots, "counter_parried");
            timeline.Add($"[T+004] W self-window -> N parry -> pass-pass close | Conductor {conductorBeforeCounter:0} stays intact, Counter Raider {counterBefore:0} -> {counterAfter:0}");

            PressButton(engine, backend, "<Keyboard>/f4", frameTimesMs);
            TickUntil(
                engine,
                frameTimesMs,
                () => Math.Abs(ReadHealth(engine.World, "Counter Raider") - counterBefore) < 0.01f &&
                      Math.Abs(ReadHealth(engine.World, "Conductor") - conductorBeforeCounter) < 0.01f,
                maxFrames: 18);
            CaptureSnapshot(engine, snapshots, "after_reset_two");
            timeline.Add("[T+005] F4 reset restored both the duelist and the counter lane");

            float scholarBefore = ReadHealth(engine.World, "Scholar");
            float protectorBefore = ReadHealth(engine.World, "Protector");
            SetMouseWorld(engine, backend, GetEntityScreen(engine, "Scholar"), frameTimesMs);
            PressButton(engine, backend, "<Keyboard>/e", frameTimesMs);
            TickUntil(engine, frameTimesMs, () => GetUiState(engine).Visible, maxFrames: 18);
            PressButton(engine, backend, "<Keyboard>/n", frameTimesMs);
            PressButton(engine, backend, "<Keyboard>/space", frameTimesMs);
            PressButton(engine, backend, "<Keyboard>/space", frameTimesMs);
            TickUntil(
                engine,
                frameTimesMs,
                () => !GetUiState(engine).Visible &&
                      ReadHealth(engine.World, "Protector") < protectorBefore,
                maxFrames: 32,
                failureMessageFactory: () => BuildResponseDiagnostics(engine));
            float scholarAfter = ReadHealth(engine.World, "Scholar");
            float protectorAfter = ReadHealth(engine.World, "Protector");
            Assert.That(scholarAfter, Is.EqualTo(scholarBefore).Within(0.01f));
            Assert.That(protectorAfter, Is.LessThan(protectorBefore));
            CaptureSnapshot(engine, snapshots, "redirect_intercepted");
            timeline.Add($"[T+006] E on Scholar -> N fixed intercept -> pass-pass close | Scholar {scholarBefore:0} stays clean, Protector {protectorBefore:0} -> {protectorAfter:0}");

            File.WriteAllText(Path.Combine(artifactDir, "trace.jsonl"), BuildTraceJsonl(snapshots));
            File.WriteAllText(Path.Combine(artifactDir, "battle-report.md"), BuildBattleReport(timeline, snapshots, frameTimesMs));
            File.WriteAllText(Path.Combine(artifactDir, "path.mmd"), BuildPathMermaid());
        }

        private static GameEngine CreateEngine()
        {
            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");
            var modPaths = RepoModPaths.ResolveExplicit(repoRoot, AcceptanceMods);

            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(modPaths, assetsRoot);
            InstallInput(engine);

            var view = new StubViewController(1920f, 1080f);
            engine.SetService(CoreServiceKeys.ViewController, view);
            engine.SetService(CoreServiceKeys.ScreenRayProvider, new WorldMappedScreenRayProvider());
            engine.SetService(CoreServiceKeys.ScreenProjector, new WorldMappedScreenProjector());

            var culling = new CameraCullingSystem(engine.World, engine.GameSession.Camera, engine.SpatialQueries, view);
            engine.RegisterPresentationSystem(culling);
            engine.SetService(CoreServiceKeys.CameraCullingDebugState, culling.DebugState);

            engine.Start();
            return engine;
        }

        private static void InstallInput(GameEngine engine)
        {
            var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
            var backend = new TestInputBackend();
            var inputHandler = new PlayerInputHandler(backend, inputConfig);
            for (int i = 0; i < engine.MergedConfig.StartupInputContexts.Count; i++)
            {
                inputHandler.PushContext(engine.MergedConfig.StartupInputContexts[i]);
            }

            engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
            engine.SetService(CoreServiceKeys.InputBackend, (IInputBackend)backend);
            engine.SetService(CoreServiceKeys.UiCaptured, false);
            engine.GlobalContext[TestInputBackendKey] = backend;
        }

        private static void LoadMap(GameEngine engine, string mapId, List<double> frameTimesMs, int frames = 6)
        {
            engine.LoadMap(mapId);
            Assert.That(engine.CurrentMapSession, Is.Not.Null, $"{mapId} should create a live map session.");
            Tick(engine, frames, frameTimesMs);
        }

        private static void Tick(GameEngine engine, int frames, List<double> frameTimesMs)
        {
            for (int i = 0; i < frames; i++)
            {
                long t0 = Stopwatch.GetTimestamp();
                engine.Tick(DeltaTime);
                frameTimesMs.Add((Stopwatch.GetTimestamp() - t0) * 1000d / Stopwatch.Frequency);
            }
        }

        private static void TickUntil(
            GameEngine engine,
            List<double> frameTimesMs,
            Func<bool> predicate,
            int maxFrames,
            Func<string>? failureMessageFactory = null)
        {
            for (int i = 0; i < maxFrames; i++)
            {
                if (predicate())
                {
                    return;
                }

                Tick(engine, 1, frameTimesMs);
            }

            string detail = failureMessageFactory?.Invoke() ?? string.Empty;
            Assert.That(
                predicate(),
                Is.True,
                $"Predicate was not satisfied within {maxFrames} frames.{(string.IsNullOrWhiteSpace(detail) ? string.Empty : $" {detail}")}");
        }

        private static TestInputBackend GetInputBackend(GameEngine engine)
        {
            return engine.GlobalContext[TestInputBackendKey] as TestInputBackend
                ?? throw new InvalidOperationException("Response-chain showcase test input backend is missing.");
        }

        private static void PressButton(GameEngine engine, TestInputBackend backend, string path, List<double> frameTimesMs)
        {
            backend.SetButton(path, true);
            Tick(engine, 2, frameTimesMs);
            backend.SetButton(path, false);
            Tick(engine, 2, frameTimesMs);
        }

        private static void SetMouseWorld(GameEngine engine, TestInputBackend backend, Vector2 screenPosition, List<double> frameTimesMs)
        {
            backend.SetMousePosition(screenPosition);
            Tick(engine, 1, frameTimesMs);
        }

        private static Vector2 GetEntityScreen(GameEngine engine, string name)
        {
            Entity entity = FindEntityByName(engine.World, name);
            Assert.That(entity, Is.Not.EqualTo(Entity.Null), $"Entity '{name}' was not found.");

            var projector = engine.GetService(CoreServiceKeys.ScreenProjector)
                ?? throw new InvalidOperationException("ScreenProjector was not installed.");
            ref var position = ref engine.World.Get<WorldPositionCm>(entity);
            return projector.WorldToScreen(WorldUnits.WorldCmToVisualMeters(position.Value, yMeters: 0f));
        }

        private static string GetSelectedEntityName(GameEngine engine)
        {
            if (SelectionContextRuntime.TryGetCurrentPrimary(engine.World, engine.GlobalContext, out Entity selected) &&
                engine.World.TryGet(selected, out Name name))
            {
                return name.Value;
            }

            return string.Empty;
        }

        private static ResponseChainUiState GetUiState(GameEngine engine)
        {
            return engine.GetService(CoreServiceKeys.ResponseChainUiState)
                ?? throw new InvalidOperationException("ResponseChainUiState service missing.");
        }

        private static Entity FindEntityByName(World world, string name)
        {
            Entity result = Entity.Null;
            var query = new QueryDescription().WithAll<Name>();
            world.Query(in query, (Entity entity, ref Name entityName) =>
            {
                if (result == Entity.Null &&
                    string.Equals(entityName.Value, name, StringComparison.OrdinalIgnoreCase))
                {
                    result = entity;
                }
            });
            return result;
        }

        private static float ReadHealth(World world, string name)
        {
            Entity entity = FindEntityByName(world, name);
            Assert.That(entity, Is.Not.EqualTo(Entity.Null), $"Entity '{name}' was not found.");

            int healthId = AttributeRegistry.GetId("Health");
            if (healthId < 0 || !world.TryGet(entity, out AttributeBuffer attributes))
            {
                return 0f;
            }

            return attributes.GetCurrent(healthId);
        }

        private static string BuildResponseDiagnostics(GameEngine engine)
        {
            string selected = GetSelectedEntityName(engine);
            string hovered = GetHoveredEntityName(engine);
            ResponseChainUiState ui = GetUiState(engine);
            string telemetry = BuildResponseTelemetryDiagnostics(engine);
            string gas = BuildGasEventDiagnostics(engine);

            return string.Join(
                " || ",
                $"selected={selected}",
                $"hovered={hovered}",
                $"uiVisible={ui.Visible}",
                $"promptTagId={ui.PromptTagId}",
                $"combo={ReadHealth(engine.World, "Combo Raider"):0.##}",
                $"counter={ReadHealth(engine.World, "Counter Raider"):0.##}",
                $"scholar={ReadHealth(engine.World, "Scholar"):0.##}",
                $"protector={ReadHealth(engine.World, "Protector"):0.##}",
                telemetry,
                gas);
        }

        private static string GetHoveredEntityName(GameEngine engine)
        {
            if (engine.GlobalContext.TryGetValue(CoreServiceKeys.HoveredEntity.Name, out object? hoveredObj) &&
                hoveredObj is Entity hovered &&
                engine.World.IsAlive(hovered) &&
                engine.World.TryGet(hovered, out Name name))
            {
                return name.Value;
            }

            return string.Empty;
        }

        private static string BuildResponseTelemetryDiagnostics(GameEngine engine)
        {
            ResponseChainTelemetryBuffer? telemetry = engine.GetService(CoreServiceKeys.ResponseChainTelemetryBuffer);
            if (telemetry == null || telemetry.Count == 0)
            {
                return "responseTelemetry=<none>";
            }

            var parts = new List<string>();
            int start = Math.Max(0, telemetry.Count - 8);
            for (int i = start; i < telemetry.Count; i++)
            {
                ResponseChainTelemetryEvent evt = telemetry[i];
                parts.Add(
                    $"{evt.Kind}/tpl:{evt.TemplateId}/tag:{evt.TagId}/proposal:{evt.ProposalIndex}/prompt:{evt.PromptTagId}/order:{evt.OrderTypeId}/outcome:{evt.Outcome}/target:{ReadEntityName(engine.World, evt.Target)}");
            }

            return $"responseTelemetry={string.Join(",", parts)}";
        }

        private static string BuildGasEventDiagnostics(GameEngine engine)
        {
            GasPresentationEventBuffer? buffer = engine.GetService(CoreServiceKeys.GasPresentationEventBuffer);
            if (buffer == null || buffer.Count == 0)
            {
                return "gasEvents=<none>";
            }

            var parts = new List<string>();
            ReadOnlySpan<GasPresentationEvent> events = buffer.Events;
            int start = Math.Max(0, events.Length - 8);
            for (int i = start; i < events.Length; i++)
            {
                ref readonly GasPresentationEvent evt = ref events[i];
                parts.Add(
                    $"{evt.Kind}/slot:{evt.AbilitySlot}/effect:{evt.EffectTemplateId}/delta:{evt.Delta:0.##}/actor:{ReadEntityName(engine.World, evt.Actor)}/target:{ReadEntityName(engine.World, evt.Target)}/fail:{evt.FailReason}");
            }

            return $"gasEvents={string.Join(",", parts)}";
        }

        private static string ReadEntityName(World world, Entity entity)
        {
            return world.IsAlive(entity) && world.TryGet(entity, out Name name) ? name.Value : $"#{entity.Id}";
        }

        private static void CaptureSnapshot(GameEngine engine, List<AcceptanceSnapshot> snapshots, string step)
        {
            var ui = GetUiState(engine);
            snapshots.Add(new AcceptanceSnapshot(
                Step: step,
                Selected: GetSelectedEntityName(engine),
                WindowVisible: ui.Visible,
                PromptTagId: ui.PromptTagId,
                ConductorHealth: ReadHealth(engine.World, "Conductor"),
                ComboHealth: ReadHealth(engine.World, "Combo Raider"),
                CounterHealth: ReadHealth(engine.World, "Counter Raider"),
                ScholarHealth: ReadHealth(engine.World, "Scholar"),
                ProtectorHealth: ReadHealth(engine.World, "Protector")));
        }

        private static string BuildTraceJsonl(IReadOnlyList<AcceptanceSnapshot> snapshots)
        {
            var lines = new List<string>(snapshots.Count);
            for (int i = 0; i < snapshots.Count; i++)
            {
                AcceptanceSnapshot snapshot = snapshots[i];
                lines.Add(JsonSerializer.Serialize(new
                {
                    event_id = $"response-chain-showcase-{i + 1:000}",
                    step = snapshot.Step,
                    selected = snapshot.Selected,
                    window_visible = snapshot.WindowVisible,
                    prompt_tag_id = snapshot.PromptTagId,
                    conductor_hp = snapshot.ConductorHealth,
                    combo_hp = snapshot.ComboHealth,
                    counter_hp = snapshot.CounterHealth,
                    scholar_hp = snapshot.ScholarHealth,
                    protector_hp = snapshot.ProtectorHealth,
                    status = "done"
                }));
            }

            return string.Join(Environment.NewLine, lines) + Environment.NewLine;
        }

        private static string BuildBattleReport(
            IReadOnlyList<string> timeline,
            IReadOnlyList<AcceptanceSnapshot> snapshots,
            IReadOnlyList<double> frameTimesMs)
        {
            double medianTickMs = Median(frameTimesMs);
            double maxTickMs = frameTimesMs.Count == 0 ? 0d : frameTimesMs.Max();
            AcceptanceSnapshot final = snapshots[^1];

            var sb = new StringBuilder();
            sb.AppendLine("# Scenario Card: response-chain-showcase");
            sb.AppendLine();
            sb.AppendLine("## Intent");
            sb.AppendLine("- Player goal: play one visible combo finish, one negate-driven counter, and one fixed-guard redirect using the production response-window stack.");
            sb.AppendLine("- Gameplay domain: GAS effect requests, response chain collection, response window order input, fixed-target dispatch remapping, and showcase reset flow.");
            sb.AppendLine();
            sb.AppendLine("## Determinism Inputs");
            sb.AppendLine("- Map: `response_chain_showcase`");
            sb.AppendLine($"- Mods: `{string.Join("`, `", AcceptanceMods)}`");
            sb.AppendLine("- Clock: fixed `1/60s` headless tick loop");
            sb.AppendLine("- Input source: real input config + deterministic backend");
            sb.AppendLine();
            sb.AppendLine("## Action Script");
            sb.AppendLine("1. Load the showcase map and confirm Conductor becomes the active player unit.");
            sb.AppendLine("2. Hover Combo Raider, cast Q, press `1`, then `Space` twice so the combo follow-up resolves.");
            sb.AppendLine("3. Press `F4` to restore the board.");
            sb.AppendLine("4. Cast W, press `N`, then `Space` twice so the incoming hit is negated while the riposte remains.");
            sb.AppendLine("5. Press `F4` again.");
            sb.AppendLine("6. Hover Scholar, cast E, press `N`, then `Space` twice so Scholar's hit is negated while Protector intercepts.");
            sb.AppendLine();
            sb.AppendLine("## Evidence Artifacts");
            sb.AppendLine("- `artifacts/acceptance/response-chain-showcase/trace.jsonl`");
            sb.AppendLine("- `artifacts/acceptance/response-chain-showcase/battle-report.md`");
            sb.AppendLine("- `artifacts/acceptance/response-chain-showcase/path.mmd`");
            sb.AppendLine();
            sb.AppendLine("## Timeline");
            for (int i = 0; i < timeline.Count; i++)
            {
                sb.AppendLine($"- {timeline[i]}");
            }

            sb.AppendLine();
            sb.AppendLine("## Outcome");
            sb.AppendLine("- success: yes");
            sb.AppendLine("- verdict: combo, counter, redirect, and reset all stayed on the shared gameplay/input/response-window path.");
            sb.AppendLine($"- final state: conductor={final.ConductorHealth:0}, combo={final.ComboHealth:0}, counter={final.CounterHealth:0}, scholar={final.ScholarHealth:0}, protector={final.ProtectorHealth:0}");
            sb.AppendLine();
            sb.AppendLine("## Summary Stats");
            sb.AppendLine($"- snapshots captured: `{snapshots.Count}`");
            sb.AppendLine($"- median headless tick: `{medianTickMs:F3}ms`");
            sb.AppendLine($"- max headless tick: `{maxTickMs:F3}ms`");
            return sb.ToString();
        }

        private static string BuildPathMermaid()
        {
            return string.Join(Environment.NewLine, new[]
            {
                "flowchart TD",
                "    A[Load map -> Conductor selected] --> B[Q on Combo Raider]",
                "    B --> C[Response window -> press 1 -> pass -> pass -> follow-up damage]",
                "    C --> D[F4 reset restores board]",
                "    D --> E[W self-window]",
                "    E --> F[N negates take-hit chain]",
                "    F --> G[Space then Space closes window -> riposte lands]",
                "    G --> H[F4 reset restores board]",
                "    H --> I[E on Scholar]",
                "    I --> J[N negates scholar-hit chain]",
                "    J --> K[Space then Space closes window -> Protector intercepts]"
            }) + Environment.NewLine;
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

        private static double Median(IReadOnlyList<double> values)
        {
            if (values.Count == 0)
            {
                return 0d;
            }

            var ordered = values.OrderBy(v => v).ToArray();
            int middle = ordered.Length / 2;
            if ((ordered.Length & 1) == 0)
            {
                return (ordered[middle - 1] + ordered[middle]) * 0.5d;
            }

            return ordered[middle];
        }

        private sealed record AcceptanceSnapshot(
            string Step,
            string Selected,
            bool WindowVisible,
            int PromptTagId,
            float ConductorHealth,
            float ComboHealth,
            float CounterHealth,
            float ScholarHealth,
            float ProtectorHealth);

        private sealed class TestInputBackend : IInputBackend
        {
            private readonly Dictionary<string, bool> _buttons = new(StringComparer.Ordinal);
            private Vector2 _mousePosition;
            private float _mouseWheel;

            public void SetButton(string path, bool isDown)
            {
                _buttons[path] = isDown;
            }

            public void SetMousePosition(Vector2 position)
            {
                _mousePosition = position;
            }

            public float GetAxis(string devicePath) => 0f;
            public bool GetButton(string devicePath) => _buttons.TryGetValue(devicePath, out bool isDown) && isDown;
            public Vector2 GetMousePosition() => _mousePosition;
            public float GetMouseWheel() => _mouseWheel;
            public void EnableIME(bool enable) { }
            public void SetIMECandidatePosition(int x, int y) { }
            public string GetCharBuffer() => string.Empty;
        }

        private sealed class StubViewController : IViewController
        {
            public StubViewController(float width, float height)
            {
                Resolution = new Vector2(width, height);
            }

            public Vector2 Resolution { get; }
            public float Fov => 60f;
            public float AspectRatio => Resolution.Y <= 0f ? 1f : Resolution.X / Resolution.Y;
        }

        private sealed class WorldMappedScreenRayProvider : IScreenRayProvider
        {
            public ScreenRay GetRay(Vector2 screenPosition)
            {
                return new ScreenRay(
                    new Vector3(screenPosition.X / 100f, 10f, screenPosition.Y / 100f),
                    -Vector3.UnitY);
            }
        }

        private sealed class WorldMappedScreenProjector : IScreenProjector
        {
            public Vector2 WorldToScreen(Vector3 worldPosition)
            {
                return new Vector2(worldPosition.X * 100f, worldPosition.Z * 100f);
            }
        }
    }
}
