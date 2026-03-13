using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;
using System.Text.Json;
using InteractionShowcaseMod;
using Ludots.Core.Engine;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production
{
    [TestFixture]
    [NonParallelizable]
    public sealed class C3AnyUnitConditionalAcceptanceTests
    {
        private const float DeltaTime = 1f / 60f;
        private const int TotalTicks = 480;

        [Test]
        public void C3AnyUnitConditional_ProducesHeadlessAcceptanceArtifacts()
        {
            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");
            string artifactDir = Path.Combine(repoRoot, "artifacts", "acceptance", "interaction-c3-any-unit-conditional");
            Directory.CreateDirectory(artifactDir);

            var timeline = new List<C3Snapshot>(TotalTicks + 1);
            var engine = new GameEngine();
            try
            {
                var modPaths = RepoModPaths.ResolveExplicit(repoRoot, new[]
                {
                    "LudotsCoreMod",
                    "ArpgDemoMod",
                    "InteractionShowcaseMod"
                });

                engine.InitializeWithConfigPipeline(modPaths, assetsRoot);
                InstallDummyInput(engine);
                engine.Start();
                engine.LoadMap(InteractionShowcaseIds.C3AnyUnitConditionalMapId);

                Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0), "Map load should not emit trigger errors.");

                timeline.Add(Sample(engine, "start"));
                for (int tick = 1; tick <= TotalTicks; tick++)
                {
                    engine.Tick(DeltaTime);
                    timeline.Add(Sample(engine, $"t{tick:000}"));
                }

                Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0), "Scenario should not emit trigger errors.");
            }
            finally
            {
                engine.Dispose();
            }

            File.WriteAllText(Path.Combine(artifactDir, "trace.jsonl"), BuildTraceJsonl(timeline), Encoding.UTF8);

            C3Snapshot start = FindFirst(
                timeline,
                snapshot => string.Equals(snapshot.Stage, "warmup", StringComparison.OrdinalIgnoreCase) &&
                    Math.Abs(snapshot.HostileMoveSpeed - 200f) <= 0.001f &&
                    Math.Abs(snapshot.FriendlyMoveSpeed - 180f) <= 0.001f &&
                    !snapshot.HostilePolymorphActive &&
                    !snapshot.FriendlyHasteActive,
                "Expected autoplay warmup to initialize hostile/friendly MoveSpeed values.");
            C3Snapshot hostileSubmitted = FindFirst(
                timeline,
                snapshot => string.Equals(snapshot.Stage, "hostile_order_submitted", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(snapshot.LastAttemptTargetName, InteractionShowcaseIds.C3HostileTargetName, StringComparison.OrdinalIgnoreCase),
                "Expected autoplay to submit the first C3 cast at the hostile target.");
            C3Snapshot hostileApplied = FindFirst(
                timeline,
                snapshot => string.Equals(snapshot.Stage, "hostile_polymorph_applied", StringComparison.OrdinalIgnoreCase) &&
                    snapshot.HostilePolymorphApplied &&
                    snapshot.HostilePolymorphActive &&
                    snapshot.HostilePolymorphCount > 0 &&
                    snapshot.HostileMoveSpeed <= 80.001f,
                "Expected hostile target branch to apply polymorph and reduce MoveSpeed to 80.");
            C3Snapshot friendlySubmitted = FindFirst(
                timeline,
                snapshot => string.Equals(snapshot.Stage, "friendly_order_submitted", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(snapshot.LastAttemptTargetName, InteractionShowcaseIds.C3FriendlyTargetName, StringComparison.OrdinalIgnoreCase),
                "Expected autoplay to submit the second C3 cast at the friendly target.");
            C3Snapshot friendlyApplied = FindFirst(
                timeline,
                snapshot => string.Equals(snapshot.Stage, "friendly_haste_applied", StringComparison.OrdinalIgnoreCase) &&
                    snapshot.FriendlyHasteApplied &&
                    snapshot.FriendlyHasteActive &&
                    snapshot.FriendlyHasteCount > 0 &&
                    snapshot.FriendlyMoveSpeed >= 259.999f,
                "Expected friendly target branch to apply haste and raise MoveSpeed to 260.");

            Assert.That(start.Mana, Is.EqualTo(100f).Within(0.001f), "Hero should start with Mana=100.");
            Assert.That(hostileSubmitted.Tick, Is.GreaterThanOrEqualTo(1), "Autoplay should submit the hostile cast after warmup.");
            Assert.That(hostileApplied.HostileMoveSpeed, Is.EqualTo(80f).Within(0.001f), "Hostile target should drop from 200 to 80 MoveSpeed.");
            Assert.That(hostileApplied.FriendlyMoveSpeed, Is.EqualTo(180f).Within(0.001f), "Friendly target should remain unchanged during hostile branch.");
            Assert.That(hostileApplied.FriendlyHasteActive, Is.False, "Friendly target must not receive haste during hostile branch.");
            Assert.That(friendlyApplied.FriendlyMoveSpeed, Is.EqualTo(260f).Within(0.001f), "Friendly target should rise from 180 to 260 MoveSpeed.");
            Assert.That(friendlyApplied.HostileMoveSpeed, Is.EqualTo(80f).Within(0.001f), "Hostile target should retain the polymorph slow after the second cast.");
            Assert.That(friendlyApplied.HostilePolymorphActive, Is.True, "Hostile target polymorph tag should remain active during the capture window.");
            Assert.That(friendlyApplied.LastAttemptTargetName, Is.EqualTo(InteractionShowcaseIds.C3FriendlyTargetName));

            File.WriteAllText(
                Path.Combine(artifactDir, "battle-report.md"),
                BuildBattleReport(start, hostileSubmitted, hostileApplied, friendlySubmitted, friendlyApplied),
                Encoding.UTF8);
            File.WriteAllText(Path.Combine(artifactDir, "path.mmd"), BuildPathMermaid(), Encoding.UTF8);
        }

        private static C3Snapshot Sample(GameEngine engine, string step)
        {
            return new C3Snapshot(
                Tick: engine.GameSession.CurrentTick,
                Step: step,
                ScriptTick: ReadInt(engine, InteractionShowcaseRuntimeKeys.ScriptTick, 0),
                Stage: ReadString(engine, InteractionShowcaseRuntimeKeys.Stage, "not_started"),
                CastSubmitted: ReadBool(engine, InteractionShowcaseRuntimeKeys.CastSubmitted),
                Mana: ReadFloat(engine, InteractionShowcaseRuntimeKeys.HeroMana),
                HostileMoveSpeed: ReadFloat(engine, InteractionShowcaseRuntimeKeys.C3HostileTargetMoveSpeed),
                FriendlyMoveSpeed: ReadFloat(engine, InteractionShowcaseRuntimeKeys.C3FriendlyTargetMoveSpeed),
                HostilePolymorphActive: ReadBool(engine, InteractionShowcaseRuntimeKeys.C3HostilePolymorphActive),
                HostilePolymorphCount: ReadInt(engine, InteractionShowcaseRuntimeKeys.C3HostilePolymorphCount, 0),
                HostilePolymorphApplied: ReadBool(engine, InteractionShowcaseRuntimeKeys.C3HostilePolymorphApplied),
                HostilePolymorphAppliedTick: ReadInt(engine, InteractionShowcaseRuntimeKeys.C3HostilePolymorphAppliedTick, -1),
                FriendlyHasteActive: ReadBool(engine, InteractionShowcaseRuntimeKeys.C3FriendlyHasteActive),
                FriendlyHasteCount: ReadInt(engine, InteractionShowcaseRuntimeKeys.C3FriendlyHasteCount, 0),
                FriendlyHasteApplied: ReadBool(engine, InteractionShowcaseRuntimeKeys.C3FriendlyHasteApplied),
                FriendlyHasteAppliedTick: ReadInt(engine, InteractionShowcaseRuntimeKeys.C3FriendlyHasteAppliedTick, -1),
                LastAttemptTargetName: ReadString(engine, InteractionShowcaseRuntimeKeys.LastAttemptTargetName, string.Empty),
                OverlayLines: TakeOverlayFrame(engine.GetService(CoreServiceKeys.ScreenOverlayBuffer) as ScreenOverlayBuffer));
        }

        private static C3Snapshot FindFirst(
            IReadOnlyList<C3Snapshot> timeline,
            Func<C3Snapshot, bool> predicate,
            string failureMessage)
        {
            for (int i = 0; i < timeline.Count; i++)
            {
                if (predicate(timeline[i]))
                {
                    return timeline[i];
                }
            }

            Assert.Fail(failureMessage);
            return default;
        }

        private static List<string> TakeOverlayFrame(ScreenOverlayBuffer? overlay)
        {
            var lines = new List<string>();
            if (overlay == null)
            {
                return lines;
            }

            foreach (var item in overlay.GetSpan())
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

            overlay.Clear();
            return lines;
        }

        private static string BuildBattleReport(
            C3Snapshot start,
            C3Snapshot hostileSubmitted,
            C3Snapshot hostileApplied,
            C3Snapshot friendlySubmitted,
            C3Snapshot friendlyApplied)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Interaction Showcase Acceptance");
            sb.AppendLine();
            sb.AppendLine("- scenario: `unit_target/c3_any_unit_conditional`");
            sb.AppendLine("- doc source: `docs/architecture/interaction/features/unit_target/c3_any_unit_conditional.md`");
            sb.AppendLine("- runtime wiring: `Ability.Interaction.C3AnyUnitConditional` emits `Effect.Interaction.C3HostileConditionalSearch` and `Effect.Interaction.C3FriendlyConditionalSearch` at the same tick; each search reuses native spatial fan-out + `relationFilter` around the explicit target and dispatches payload effects `Effect.Interaction.C3HostilePolymorph` / `Effect.Interaction.C3FriendlyHaste`");
            sb.AppendLine("- reuse note: no new Core graph node or relation-branching runtime was introduced; C3 stays inside standard ability/effect config plus showcase autoplay");
            sb.AppendLine("- tech-debt: `TD-2026-03-13-C3-DirectExplicitTargetRelationFilterGap` -> `artifacts/techdebt/2026-03-13-c3-direct-explicit-target-relation-filter-gap.md`");
            sb.AppendLine($"- acceptance window: `{TotalTicks}` engine frames");
            sb.AppendLine();
            sb.AppendLine("## Verdict");
            sb.AppendLine();
            sb.AppendLine("- pass: the same skill first polymorphs a hostile target (`MoveSpeed 200 -> 80`), then hastes a friendly target (`MoveSpeed 180 -> 260`) without cross-applying the wrong branch.");
            sb.AppendLine();
            sb.AppendLine("## Timeline");
            sb.AppendLine();
            sb.AppendLine($"- start: tick `{start.Tick}` stage=`{start.Stage}` mana=`{start.Mana:F1}` hostileMS=`{start.HostileMoveSpeed:F1}` friendlyMS=`{start.FriendlyMoveSpeed:F1}`");
            sb.AppendLine($"- hostile order submitted: tick `{hostileSubmitted.Tick}` scriptTick=`{hostileSubmitted.ScriptTick}` target=`{hostileSubmitted.LastAttemptTargetName}`");
            sb.AppendLine($"- hostile polymorph applied: tick `{hostileApplied.Tick}` hostileMS=`{hostileApplied.HostileMoveSpeed:F1}` hostileTagCount=`{hostileApplied.HostilePolymorphCount}` hostileTick=`{hostileApplied.HostilePolymorphAppliedTick}`");
            sb.AppendLine($"- friendly order submitted: tick `{friendlySubmitted.Tick}` scriptTick=`{friendlySubmitted.ScriptTick}` target=`{friendlySubmitted.LastAttemptTargetName}`");
            sb.AppendLine($"- friendly haste applied: tick `{friendlyApplied.Tick}` friendlyMS=`{friendlyApplied.FriendlyMoveSpeed:F1}` friendlyTagCount=`{friendlyApplied.FriendlyHasteCount}` friendlyTick=`{friendlyApplied.FriendlyHasteAppliedTick}`");
            sb.AppendLine();
            sb.AppendLine("## Reuse");
            sb.AppendLine();
            sb.AppendLine("- `AbilityDefinitionRegistry` and `EffectTemplateRegistry`: reuse the standard mod asset merge path for `assets/GAS/abilities.json` and `assets/GAS/effects.json`.");
            sb.AppendLine("- `EffectPresetType.Buff`: reuse the core preset mapping in `assets/Configs/GAS/preset_types.json`.");
            sb.AppendLine("- `targetFilter.relationFilter`: reuse native GAS relation filtering through `Search + targetDispatch` fan-out instead of adding custom graph branching nodes.");
            sb.AppendLine("- `OrderQueue` -> `OrderBufferSystem` -> `AbilityExecSystem` -> `EffectProcessingLoopSystem`: both C3 branches execute in the standard GAS path.");
            sb.AppendLine("- validation-scope note: C3 proves same-ability hostile/friendly branching on two positive paths via the search-wrapper workaround; it does not rely on showcase-local relation guards.");
            sb.AppendLine("- validation-scope note: direct explicit-target effects currently do not honor `targetFilter.relationFilter`; the workaround keeps the feature in Mod scope while the lower-layer gap remains open.");
            sb.AppendLine();
            sb.AppendLine("## Overlay Sample");
            sb.AppendLine();
            if (friendlyApplied.OverlayLines.Count == 0)
            {
                sb.AppendLine("- no overlay text captured");
            }
            else
            {
                for (int i = 0; i < friendlyApplied.OverlayLines.Count; i++)
                {
                    sb.AppendLine($"- {friendlyApplied.OverlayLines[i]}");
                }
            }

            sb.AppendLine();
            sb.AppendLine("## Artifacts");
            sb.AppendLine();
            sb.AppendLine("- `artifacts/acceptance/interaction-c3-any-unit-conditional/battle-report.md`");
            sb.AppendLine("- `artifacts/acceptance/interaction-c3-any-unit-conditional/trace.jsonl`");
            sb.AppendLine("- `artifacts/acceptance/interaction-c3-any-unit-conditional/path.mmd`");
            return sb.ToString();
        }

        private static string BuildTraceJsonl(IReadOnlyList<C3Snapshot> timeline)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < timeline.Count; i++)
            {
                C3Snapshot snapshot = timeline[i];

                sb.AppendLine(JsonSerializer.Serialize(new
                {
                    event_id = $"interaction-c3-headless-{i + 1:000}",
                    tick = snapshot.Tick,
                    step = snapshot.Step,
                    script_tick = snapshot.ScriptTick,
                    stage = snapshot.Stage,
                    cast_submitted = snapshot.CastSubmitted,
                    mana = Math.Round(snapshot.Mana, 2),
                    hostile_target_move_speed = Math.Round(snapshot.HostileMoveSpeed, 2),
                    friendly_target_move_speed = Math.Round(snapshot.FriendlyMoveSpeed, 2),
                    hostile_polymorph_active = snapshot.HostilePolymorphActive,
                    hostile_polymorph_count = snapshot.HostilePolymorphCount,
                    hostile_polymorph_applied = snapshot.HostilePolymorphApplied,
                    hostile_polymorph_applied_tick = snapshot.HostilePolymorphAppliedTick,
                    friendly_haste_active = snapshot.FriendlyHasteActive,
                    friendly_haste_count = snapshot.FriendlyHasteCount,
                    friendly_haste_applied = snapshot.FriendlyHasteApplied,
                    friendly_haste_applied_tick = snapshot.FriendlyHasteAppliedTick,
                    last_attempt_target_name = snapshot.LastAttemptTargetName,
                    status = "sample"
                }));
            }

            return sb.ToString();
        }

        private static string BuildPathMermaid()
        {
            return """
flowchart LR
    A[Load interaction_c3_any_unit_conditional] --> B[Autoplay warmup]
    B --> C[OrderQueue enqueues castAbility slot 0 on C3EnemyPrimary]
    C --> D[Ability emits hostile and friendly search wrappers at tick 0]
    D --> E[Hostile search fan-out passes and dispatches Effect.Interaction.C3HostilePolymorph]
    D --> F[Friendly search fan-out skips hostile target]
    E --> G[Hostile MoveSpeed 200 -> 80 and Status.Polymorphed]
    G --> H[OrderQueue enqueues same slot 0 on C3AllyPrimary]
    H --> I[Ability emits the same two search wrappers again]
    I --> J[Hostile search fan-out skips friendly target]
    I --> K[Friendly search fan-out passes and dispatches Effect.Interaction.C3FriendlyHaste]
    K --> L[Friendly MoveSpeed 180 -> 260 and Status.Hasted]
""";
        }

        private static string ReadString(GameEngine engine, string key, string fallback)
        {
            return engine.GlobalContext.TryGetValue(key, out var value) && value is string text
                ? text
                : fallback;
        }

        private static int ReadInt(GameEngine engine, string key, int fallback)
        {
            return engine.GlobalContext.TryGetValue(key, out var value) && value is int number
                ? number
                : fallback;
        }

        private static float ReadFloat(GameEngine engine, string key)
        {
            return engine.GlobalContext.TryGetValue(key, out var value) && value is float number
                ? number
                : 0f;
        }

        private static bool ReadBool(GameEngine engine, string key)
        {
            return engine.GlobalContext.TryGetValue(key, out var value) && value is bool flag && flag;
        }

        private static void InstallDummyInput(GameEngine engine)
        {
            var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
            var inputHandler = new PlayerInputHandler(new NullInputBackend(), inputConfig);
            engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
            engine.SetService(CoreServiceKeys.UiCaptured, false);
        }

        private static string FindRepoRoot()
        {
            string? dir = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrWhiteSpace(dir))
            {
                string candidate = Path.Combine(dir, "src", "Core", "Ludots.Core.csproj");
                if (File.Exists(candidate))
                {
                    return dir;
                }

                dir = Path.GetDirectoryName(dir);
            }

            throw new InvalidOperationException("Could not locate repo root.");
        }

        private readonly record struct C3Snapshot(
            int Tick,
            string Step,
            int ScriptTick,
            string Stage,
            bool CastSubmitted,
            float Mana,
            float HostileMoveSpeed,
            float FriendlyMoveSpeed,
            bool HostilePolymorphActive,
            int HostilePolymorphCount,
            bool HostilePolymorphApplied,
            int HostilePolymorphAppliedTick,
            bool FriendlyHasteActive,
            int FriendlyHasteCount,
            bool FriendlyHasteApplied,
            int FriendlyHasteAppliedTick,
            string LastAttemptTargetName,
            IReadOnlyList<string> OverlayLines);

        private sealed class NullInputBackend : IInputBackend
        {
            public float GetAxis(string devicePath) => 0f;
            public bool GetButton(string devicePath) => false;
            public Vector2 GetMousePosition() => Vector2.Zero;
            public float GetMouseWheel() => 0f;
            public void EnableIME(bool enable)
            {
            }

            public void SetIMECandidatePosition(int x, int y)
            {
            }

            public string GetCharBuffer() => string.Empty;

            public void Vibrate(int playerIndex, float lowFrequency, float highFrequency, float durationSeconds)
            {
            }

            public string[] GetConnectedGamepads() => Array.Empty<string>();
        }
    }
}
