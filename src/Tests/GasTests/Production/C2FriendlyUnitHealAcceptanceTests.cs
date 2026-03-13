using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Arch.Core;
using InteractionShowcaseMod;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production
{
    [TestFixture]
    [NonParallelizable]
    public sealed class C2FriendlyUnitHealAcceptanceTests
    {
        private const float DeltaTime = 1f / 60f;
        private const int TotalTicks = 480;
        private static readonly int HealthAttributeId = AttributeRegistry.Register("Health");
        private static readonly int ManaAttributeId = AttributeRegistry.Register("Mana");

        [Test]
        public void C2FriendlyUnitHeal_ProducesHeadlessAcceptanceArtifacts()
        {
            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");
            string artifactDir = Path.Combine(repoRoot, "artifacts", "acceptance", "interaction-c2-friendly-unit-heal");
            Directory.CreateDirectory(artifactDir);

            var timeline = new List<C2Snapshot>(TotalTicks + 1);
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
                engine.LoadMap(InteractionShowcaseIds.C2FriendlyUnitHealMapId);

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

            C2Snapshot start = FindFirst(
                timeline,
                snapshot => string.Equals(snapshot.Stage, "warmup", StringComparison.OrdinalIgnoreCase) &&
                    Math.Abs(snapshot.AllyTargetHealth - 200f) <= 0.001f &&
                    Math.Abs(snapshot.HostileTargetHealth - 400f) <= 0.001f &&
                    Math.Abs(snapshot.DeadAllyTargetHealth) <= 0.001f,
                "Expected autoplay warmup to initialize ally/hostile/dead-ally Health values.");
            C2Snapshot submitted = FindFirst(
                timeline,
                snapshot => snapshot.CastSubmitted &&
                    string.Equals(snapshot.Stage, "order_submitted", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(snapshot.LastAttemptTargetName, InteractionShowcaseIds.C2AllyTargetName, StringComparison.OrdinalIgnoreCase),
                "Expected autoplay system to submit the slot 0 friendly heal at the wounded ally.");
            C2Snapshot healApplied = FindFirst(
                timeline,
                snapshot => string.Equals(snapshot.Stage, "heal_applied", StringComparison.OrdinalIgnoreCase) &&
                    snapshot.HealApplied &&
                    snapshot.AllyTargetHealth >= 349.999f,
                "Expected C2 friendly heal to reach heal_applied with ally HP restored to 350.");
            C2Snapshot hostileTargetBlocked = FindFirst(
                timeline,
                snapshot => string.Equals(snapshot.Stage, "hostile_target_blocked", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(snapshot.LastCastFailReason, "InvalidTarget", StringComparison.Ordinal) &&
                    string.Equals(snapshot.LastAttemptTargetName, InteractionShowcaseIds.C2HostileTargetName, StringComparison.OrdinalIgnoreCase),
                "Expected hostile target branch to fail with InvalidTarget.");
            C2Snapshot deadAllyBlocked = FindFirst(
                timeline,
                snapshot => string.Equals(snapshot.Stage, "dead_ally_blocked", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(snapshot.LastCastFailReason, "InvalidTarget", StringComparison.Ordinal) &&
                    string.Equals(snapshot.LastAttemptTargetName, InteractionShowcaseIds.C2DeadAllyTargetName, StringComparison.OrdinalIgnoreCase),
                "Expected dead ally branch to fail with InvalidTarget.");

            Assert.That(start.Mana, Is.EqualTo(100f).Within(0.001f), "Hero should start with Mana=100.");
            Assert.That(start.AllyTargetHealth, Is.EqualTo(200f).Within(0.001f), "Friendly target should start wounded at HP=200.");
            Assert.That(start.HostileTargetHealth, Is.EqualTo(400f).Within(0.001f), "Hostile target should start untouched at HP=400.");
            Assert.That(start.DeadAllyTargetHealth, Is.EqualTo(0f).Within(0.001f), "Dead ally should start at HP=0.");
            Assert.That(submitted.Tick, Is.GreaterThanOrEqualTo(1), "Autoplay should submit after scenario warmup.");
            Assert.That(healApplied.AllyTargetHealth, Is.EqualTo(350f).Within(0.001f), "Friendly heal should restore 150 HP.");
            Assert.That(healApplied.HealAmount, Is.EqualTo(150f).Within(0.001f), "Recorded heal amount should be 150.");
            Assert.That(hostileTargetBlocked.HostileTargetHealth, Is.EqualTo(400f).Within(0.001f), "Hostile target retry must not change the enemy.");
            Assert.That(hostileTargetBlocked.AllyTargetHealth, Is.EqualTo(350f).Within(0.001f), "Hostile target retry must not alter the healed ally.");
            Assert.That(deadAllyBlocked.DeadAllyTargetHealth, Is.EqualTo(0f).Within(0.001f), "Dead ally retry must not revive or modify the dead ally.");
            Assert.That(deadAllyBlocked.AllyTargetHealth, Is.EqualTo(350f).Within(0.001f), "Dead ally retry must not alter the healed ally.");

            File.WriteAllText(
                Path.Combine(artifactDir, "battle-report.md"),
                BuildBattleReport(start, submitted, healApplied, hostileTargetBlocked, deadAllyBlocked),
                Encoding.UTF8);
            File.WriteAllText(Path.Combine(artifactDir, "path.mmd"), BuildPathMermaid(), Encoding.UTF8);
        }

        private static C2Snapshot Sample(GameEngine engine, string step)
        {
            Entity hero = FindEntity(engine.World, InteractionShowcaseIds.HeroName);
            Entity ally = FindEntity(engine.World, InteractionShowcaseIds.C2AllyTargetName);
            Entity hostile = FindEntity(engine.World, InteractionShowcaseIds.C2HostileTargetName);
            Entity deadAlly = FindEntity(engine.World, InteractionShowcaseIds.C2DeadAllyTargetName);

            return new C2Snapshot(
                Tick: engine.GameSession.CurrentTick,
                Step: step,
                ScriptTick: ReadInt(engine, InteractionShowcaseRuntimeKeys.ScriptTick, 0),
                Stage: ReadString(engine, InteractionShowcaseRuntimeKeys.Stage, "not_started"),
                CastSubmitted: ReadBool(engine, InteractionShowcaseRuntimeKeys.CastSubmitted),
                Mana: ReadAttribute(engine.World, hero, ManaAttributeId),
                AllyTargetHealth: ReadAttribute(engine.World, ally, HealthAttributeId),
                HostileTargetHealth: ReadAttribute(engine.World, hostile, HealthAttributeId),
                DeadAllyTargetHealth: ReadAttribute(engine.World, deadAlly, HealthAttributeId),
                HealAmount: ReadFloat(engine, InteractionShowcaseRuntimeKeys.C2HealAmount),
                HealApplied: ReadBool(engine, InteractionShowcaseRuntimeKeys.C2HealApplied),
                HealAppliedTick: ReadInt(engine, InteractionShowcaseRuntimeKeys.C2HealAppliedTick, -1),
                LastAttemptTargetName: ReadString(engine, InteractionShowcaseRuntimeKeys.LastAttemptTargetName, string.Empty),
                LastCastFailReason: ReadString(engine, InteractionShowcaseRuntimeKeys.LastCastFailReason, string.Empty),
                LastCastFailTick: ReadInt(engine, InteractionShowcaseRuntimeKeys.LastCastFailTick, -1),
                OverlayLines: TakeOverlayFrame(engine.GetService(CoreServiceKeys.ScreenOverlayBuffer) as ScreenOverlayBuffer));
        }

        private static C2Snapshot FindFirst(
            IReadOnlyList<C2Snapshot> timeline,
            Func<C2Snapshot, bool> predicate,
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

        private static Entity FindEntity(World world, string entityName)
        {
            Entity result = Entity.Null;
            var query = new QueryDescription().WithAll<Name>();
            world.Query(in query, (Entity entity, ref Name name) =>
            {
                if (result != Entity.Null)
                {
                    return;
                }

                if (world.IsAlive(entity) && string.Equals(name.Value, entityName, StringComparison.OrdinalIgnoreCase))
                {
                    result = entity;
                }
            });
            return result;
        }

        private static float ReadAttribute(World world, Entity entity, int attributeId)
        {
            if (entity == Entity.Null || !world.IsAlive(entity) || !world.Has<AttributeBuffer>(entity))
            {
                return 0f;
            }

            return world.Get<AttributeBuffer>(entity).GetCurrent(attributeId);
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
            C2Snapshot start,
            C2Snapshot submitted,
            C2Snapshot healApplied,
            C2Snapshot hostileTargetBlocked,
            C2Snapshot deadAllyBlocked)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Interaction Showcase Acceptance");
            sb.AppendLine();
            sb.AppendLine("- scenario: `unit_target/c2_friendly_unit_heal`");
            sb.AppendLine("- doc source: `docs/architecture/interaction/features/unit_target/c2_friendly_unit_heal.md`");
            sb.AppendLine("- runtime wiring: `Ability.Interaction.C2FriendlyUnitHeal` applies `Effect.Interaction.C2FriendlyUnitHeal`, using the built-in `Heal` preset to add `+150 Health` on the explicit ally target");
            sb.AppendLine("- resource note: current showcase does not configure mana cost, so hero Mana stays at `100` across the full scenario.");
            sb.AppendLine($"- acceptance window: `{TotalTicks}` engine frames");
            sb.AppendLine();
            sb.AppendLine("## Verdict");
            sb.AppendLine();
            sb.AppendLine("- pass: wounded ally starts at `HP=200`, the direct heal restores it to `350`, then hostile-target and dead-ally retries fail without changing any target Health.");
            sb.AppendLine();
            sb.AppendLine("## Timeline");
            sb.AppendLine();
            sb.AppendLine($"- start: tick `{start.Tick}` stage=`{start.Stage}` mana=`{start.Mana:F1}` allyHP=`{start.AllyTargetHealth:F1}` hostileHP=`{start.HostileTargetHealth:F1}` deadAllyHP=`{start.DeadAllyTargetHealth:F1}`");
            sb.AppendLine($"- order submitted: tick `{submitted.Tick}` scriptTick=`{submitted.ScriptTick}` target=`{submitted.LastAttemptTargetName}`");
            sb.AppendLine($"- heal first observed: tick `{healApplied.Tick}` allyHP=`{healApplied.AllyTargetHealth:F1}` healAmount=`{healApplied.HealAmount:F1}` healTick=`{healApplied.HealAppliedTick}`");
            sb.AppendLine($"- hostile target blocked: tick `{hostileTargetBlocked.Tick}` fail=`{hostileTargetBlocked.LastCastFailReason}` target=`{hostileTargetBlocked.LastAttemptTargetName}` hostileHP=`{hostileTargetBlocked.HostileTargetHealth:F1}`");
            sb.AppendLine($"- dead ally blocked: tick `{deadAllyBlocked.Tick}` fail=`{deadAllyBlocked.LastCastFailReason}` target=`{deadAllyBlocked.LastAttemptTargetName}` deadAllyHP=`{deadAllyBlocked.DeadAllyTargetHealth:F1}`");
            sb.AppendLine();
            sb.AppendLine("## Reuse");
            sb.AppendLine();
            sb.AppendLine("- `EffectPresetType.Heal`: reuse the core preset mapping in `assets/Configs/GAS/preset_types.json`.");
            sb.AppendLine("- `AbilityDefinitionRegistry` and `EffectTemplateRegistry`: reuse the standard mod asset merge path for `assets/GAS/abilities.json` and `assets/GAS/effects.json`.");
            sb.AppendLine("- `GameEngine` startup and map loading: reuse standard mod pipeline with `LudotsCoreMod`, `ArpgDemoMod`, `InteractionShowcaseMod`.");
            sb.AppendLine("- `OrderQueue` -> `OrderBufferSystem` -> `AbilityExecSystem` -> `EffectProcessingLoopSystem`: no parallel runtime was introduced.");
            sb.AppendLine("- validation-scope note: hostile-target and dead-ally retries are both blocked by showcase-local validation before enqueue; the positive branch alone is fully evidenced as native GAS execution.");
            sb.AppendLine("- validation-scope note: hostile-target rejection remains a showcase-local pre-enqueue guard; although the effect template still carries `targetFilter.relationFilter = Friendly`, the direct explicit-target relation-filter gap tracked in `artifacts/techdebt/2026-03-13-c3-direct-explicit-target-relation-filter-gap.md` means this path is not treated as a proven native hostile fence.");
            sb.AppendLine("- validation-scope note: C2 therefore records the hostile retry as showcase-local containment, not as evidence that native GAS rejected the hostile cast.");
            sb.AppendLine("- validation-scope note: dead-ally rejection currently has no native alive-status fence in evidence; bypassing the showcase-local alive guard would allow `Friendly + Heal` to raise `Health 0 -> 150`.");
            sb.AppendLine("- tech-debt: `TD-2026-03-13-C2-DeadAllyAliveFenceGap` -> `artifacts/techdebt/2026-03-13-c2-dead-ally-alive-fence-gap.md`");
            sb.AppendLine("- trace note: root `trace.jsonl` is the per-tick headless sample stream; `visual/trace.jsonl` is the five-checkpoint launcher capture with the same core fields plus recorder metadata.");
            sb.AppendLine($"- timing note: `heal_applied` / `healTick={healApplied.HealAppliedTick}` record the first autoplay sample that publishes the completed heal state. Headless trace can already show `allyHP=350` late in `order_submitted`, so `heal_applied_tick` should not be treated as a precise modifier-apply timestamp.");
            sb.AppendLine("- trace note: headless sampling starts before warmup, so the first two tick-0 rows still show template base Health; the visual recorder starts at the first post-warmup checkpoint.");
            sb.AppendLine();
            sb.AppendLine("## Overlay Sample");
            sb.AppendLine();
            if (deadAllyBlocked.OverlayLines.Count == 0)
            {
                sb.AppendLine("- no overlay text captured");
            }
            else
            {
                for (int i = 0; i < deadAllyBlocked.OverlayLines.Count; i++)
                {
                    sb.AppendLine($"- {deadAllyBlocked.OverlayLines[i]}");
                }
            }

            sb.AppendLine();
            sb.AppendLine("## Artifacts");
            sb.AppendLine();
            sb.AppendLine("- `artifacts/acceptance/interaction-c2-friendly-unit-heal/battle-report.md`");
            sb.AppendLine("- `artifacts/acceptance/interaction-c2-friendly-unit-heal/trace.jsonl`");
            sb.AppendLine("- `artifacts/acceptance/interaction-c2-friendly-unit-heal/path.mmd`");
            return sb.ToString();
        }

        private static string BuildTraceJsonl(IReadOnlyList<C2Snapshot> timeline)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < timeline.Count; i++)
            {
                C2Snapshot snapshot = timeline[i];
                bool currentAttemptEnqueued =
                    string.Equals(snapshot.Stage, "order_submitted", StringComparison.OrdinalIgnoreCase) &&
                    snapshot.CastSubmitted &&
                    string.Equals(snapshot.LastAttemptTargetName, InteractionShowcaseIds.C2AllyTargetName, StringComparison.OrdinalIgnoreCase) &&
                    !snapshot.HealApplied &&
                    snapshot.AllyTargetHealth <= 200.001f;
                bool blockedLocallyBeforeEnqueue =
                    (string.Equals(snapshot.Stage, "hostile_target_blocked", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(snapshot.Stage, "dead_ally_blocked", StringComparison.OrdinalIgnoreCase));

                sb.AppendLine(JsonSerializer.Serialize(new
                {
                    event_id = $"interaction-c2-headless-{i + 1:000}",
                    tick = snapshot.Tick,
                    step = snapshot.Step,
                    script_tick = snapshot.ScriptTick,
                    stage = snapshot.Stage,
                    run_has_submitted_cast = snapshot.CastSubmitted,
                    current_attempt_enqueued = currentAttemptEnqueued,
                    blocked_locally_before_enqueue = blockedLocallyBeforeEnqueue,
                    mana = Math.Round(snapshot.Mana, 2),
                    ally_target_health = Math.Round(snapshot.AllyTargetHealth, 2),
                    hostile_target_health = Math.Round(snapshot.HostileTargetHealth, 2),
                    dead_ally_target_health = Math.Round(snapshot.DeadAllyTargetHealth, 2),
                    heal_amount = Math.Round(snapshot.HealAmount, 2),
                    heal_applied = snapshot.HealApplied,
                    heal_applied_tick = snapshot.HealAppliedTick,
                    last_attempt_target_name = snapshot.LastAttemptTargetName,
                    last_cast_fail_reason = snapshot.LastCastFailReason,
                    status = "sample"
                }));
            }
            return sb.ToString();
        }

        private static string BuildPathMermaid()
        {
            return """
flowchart LR
    A[Load interaction_c2_friendly_unit_heal] --> B[Autoplay warmup]
    B --> C[OrderQueue enqueues castAbility slot 0 on C2AllyPrimary]
    C --> D[Effect.Interaction.C2FriendlyUnitHeal applies built-in Heal modifiers]
    D --> E[Ally HP 200 -> 350]
    E --> F[Retry on C2EnemyInvalid]
    F --> G[Autoplay blocks enqueue: InvalidTarget local guard]
    G --> H[Retry on C2AllyDead]
    H --> I[Autoplay blocks enqueue: InvalidTarget local guard]
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

        private readonly record struct C2Snapshot(
            int Tick,
            string Step,
            int ScriptTick,
            string Stage,
            bool CastSubmitted,
            float Mana,
            float AllyTargetHealth,
            float HostileTargetHealth,
            float DeadAllyTargetHealth,
            float HealAmount,
            bool HealApplied,
            int HealAppliedTick,
            string LastAttemptTargetName,
            string LastCastFailReason,
            int LastCastFailTick,
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
        }
    }
}
