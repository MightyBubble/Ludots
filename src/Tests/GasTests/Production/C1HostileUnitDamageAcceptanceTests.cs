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
    public sealed class C1HostileUnitDamageAcceptanceTests
    {
        private const float DeltaTime = 1f / 60f;
        private const int TotalTicks = 480;

        [Test]
        public void C1HostileUnitDamage_ProducesHeadlessAcceptanceArtifacts()
        {
            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");
            string artifactDir = Path.Combine(repoRoot, "artifacts", "acceptance", "interaction-c1-hostile-unit-damage");
            Directory.CreateDirectory(artifactDir);

            var timeline = new List<C1Snapshot>(TotalTicks + 1);
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
                engine.LoadMap(InteractionShowcaseIds.C1HostileUnitDamageMapId);

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

            C1Snapshot start = timeline[0];
            C1Snapshot submitted = FindFirst(
                timeline,
                snapshot => snapshot.CastSubmitted &&
                    string.Equals(snapshot.Stage, "order_submitted", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(snapshot.LastAttemptTargetName, InteractionShowcaseIds.C1PrimaryTargetName, StringComparison.OrdinalIgnoreCase),
                "Expected autoplay system to submit the slot 0 hostile cast at the primary target.");
            C1Snapshot damageApplied = FindFirst(
                timeline,
                snapshot => string.Equals(snapshot.Stage, "damage_applied", StringComparison.OrdinalIgnoreCase) &&
                    snapshot.DamageApplied &&
                    snapshot.PrimaryTargetHealth <= 300.001f &&
                    snapshot.DamageAmount >= 299.999f &&
                    snapshot.FinalDamage >= 199.999f,
                "Expected C1 hostile damage to reach damage_applied with DamageAmount=300 and FinalDamage=200.");
            C1Snapshot invalidTargetBlocked = FindFirst(
                timeline,
                snapshot => string.Equals(snapshot.Stage, "invalid_target_blocked", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(snapshot.LastCastFailReason, "InvalidTarget", StringComparison.Ordinal) &&
                    string.Equals(snapshot.LastAttemptTargetName, InteractionShowcaseIds.C1InvalidTargetName, StringComparison.OrdinalIgnoreCase),
                "Expected invalid target branch to fail with InvalidTarget.");
            C1Snapshot outOfRangeBlocked = FindFirst(
                timeline,
                snapshot => string.Equals(snapshot.Stage, "out_of_range_blocked", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(snapshot.LastCastFailReason, "OutOfRange", StringComparison.Ordinal) &&
                    string.Equals(snapshot.LastAttemptTargetName, InteractionShowcaseIds.C1FarTargetName, StringComparison.OrdinalIgnoreCase),
                "Expected out-of-range branch to fail with OutOfRange.");

            Assert.That(start.HeroBaseDamage, Is.EqualTo(200f).Within(0.001f), "Hero should start with BaseDamage=200.");
            Assert.That(start.Mana, Is.EqualTo(100f).Within(0.001f), "Hero should start with Mana=100.");
            Assert.That(start.PrimaryTargetHealth, Is.EqualTo(500f).Within(0.001f), "Primary target should start at HP=500.");
            Assert.That(start.PrimaryTargetArmor, Is.EqualTo(50f).Within(0.001f), "Primary target should start at Armor=50.");
            Assert.That(start.InvalidTargetHealth, Is.EqualTo(0f).Within(0.001f), "Invalid target should start dead.");
            Assert.That(start.FarTargetHealth, Is.EqualTo(500f).Within(0.001f), "Far target should start untouched.");
            Assert.That(submitted.Tick, Is.GreaterThanOrEqualTo(1), "Autoplay should submit after scenario warmup.");
            Assert.That(damageApplied.PrimaryTargetHealth, Is.EqualTo(300f).Within(0.001f), "Primary target should lose 200 HP.");
            Assert.That(damageApplied.DamageAmount, Is.EqualTo(300f).Within(0.001f), "DamageAmount should be 300 before mitigation.");
            Assert.That(damageApplied.FinalDamage, Is.EqualTo(200f).Within(0.001f), "FinalDamage should be 200 after armor mitigation.");
            Assert.That(invalidTargetBlocked.InvalidTargetHealth, Is.EqualTo(0f).Within(0.001f), "Invalid target retry must not revive or damage the dead target.");
            Assert.That(invalidTargetBlocked.PrimaryTargetHealth, Is.EqualTo(300f).Within(0.001f), "Invalid target retry must not affect the primary target.");
            Assert.That(outOfRangeBlocked.FarTargetHealth, Is.EqualTo(500f).Within(0.001f), "Out-of-range retry must not affect the far target.");
            Assert.That(outOfRangeBlocked.PrimaryTargetHealth, Is.EqualTo(300f).Within(0.001f), "Out-of-range retry must not alter the primary target.");

            File.WriteAllText(
                Path.Combine(artifactDir, "battle-report.md"),
                BuildBattleReport(start, submitted, damageApplied, invalidTargetBlocked, outOfRangeBlocked),
                Encoding.UTF8);
            File.WriteAllText(Path.Combine(artifactDir, "path.mmd"), BuildPathMermaid(), Encoding.UTF8);
        }

        private static C1Snapshot Sample(GameEngine engine, string step)
        {
            Entity hero = FindEntity(engine.World, InteractionShowcaseIds.HeroName);
            Entity primary = FindEntity(engine.World, InteractionShowcaseIds.C1PrimaryTargetName);
            Entity invalid = FindEntity(engine.World, InteractionShowcaseIds.C1InvalidTargetName);
            Entity far = FindEntity(engine.World, InteractionShowcaseIds.C1FarTargetName);

            int baseDamageId = AttributeRegistry.Register("BaseDamage");
            int healthId = AttributeRegistry.Register("Health");
            int armorId = AttributeRegistry.Register("Armor");
            int manaId = AttributeRegistry.Register("Mana");
            int damageAmountKeyId = ConfigKeyRegistry.Register("Interaction.C1.DamageAmount");
            int finalDamageKeyId = ConfigKeyRegistry.Register("Interaction.C1.FinalDamage");

            return new C1Snapshot(
                Tick: engine.GameSession.CurrentTick,
                Step: step,
                ScriptTick: ReadInt(engine, InteractionShowcaseRuntimeKeys.ScriptTick, 0),
                Stage: ReadString(engine, InteractionShowcaseRuntimeKeys.Stage, "not_started"),
                CastSubmitted: ReadBool(engine, InteractionShowcaseRuntimeKeys.CastSubmitted),
                HeroBaseDamage: ReadAttribute(engine.World, hero, baseDamageId),
                Mana: ReadAttribute(engine.World, hero, manaId),
                PrimaryTargetHealth: ReadAttribute(engine.World, primary, healthId),
                PrimaryTargetArmor: ReadAttribute(engine.World, primary, armorId),
                InvalidTargetHealth: ReadAttribute(engine.World, invalid, healthId),
                FarTargetHealth: ReadAttribute(engine.World, far, healthId),
                DamageAmount: ReadBlackboardFloat(engine.World, primary, damageAmountKeyId),
                FinalDamage: ReadBlackboardFloat(engine.World, primary, finalDamageKeyId),
                DamageApplied: ReadBool(engine, InteractionShowcaseRuntimeKeys.DamageApplied),
                DamageAppliedTick: ReadInt(engine, InteractionShowcaseRuntimeKeys.DamageAppliedTick, -1),
                LastAttemptTargetName: ReadString(engine, InteractionShowcaseRuntimeKeys.LastAttemptTargetName, string.Empty),
                LastCastFailReason: ReadString(engine, InteractionShowcaseRuntimeKeys.LastCastFailReason, string.Empty),
                OverlayLines: TakeOverlayFrame(engine.GetService(CoreServiceKeys.ScreenOverlayBuffer) as ScreenOverlayBuffer));
        }

        private static C1Snapshot FindFirst(
            IReadOnlyList<C1Snapshot> timeline,
            Func<C1Snapshot, bool> predicate,
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

        private static float ReadBlackboardFloat(World world, Entity entity, int keyId)
        {
            if (entity == Entity.Null || !world.IsAlive(entity) || !world.Has<BlackboardFloatBuffer>(entity))
            {
                return 0f;
            }

            ref var buffer = ref world.Get<BlackboardFloatBuffer>(entity);
            return buffer.TryGet(keyId, out float value) ? value : 0f;
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
            C1Snapshot start,
            C1Snapshot submitted,
            C1Snapshot damageApplied,
            C1Snapshot invalidTargetBlocked,
            C1Snapshot outOfRangeBlocked)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Interaction Showcase Acceptance");
            sb.AppendLine();
            sb.AppendLine("- scenario: `unit_target/c1_hostile_unit_damage`");
            sb.AppendLine("- doc source: `docs/architecture/interaction/features/unit_target/c1_hostile_unit_damage.md`");
            sb.AppendLine("- runtime wiring: `Ability.Interaction.C1HostileUnitDamage` applies `Effect.Interaction.C1HostileUnitDamage`, with graph phases writing `Interaction.C1.DamageAmount` / `Interaction.C1.FinalDamage` onto the target blackboard");
            sb.AppendLine($"- acceptance window: `{TotalTicks}` engine frames");
            sb.AppendLine();
            sb.AppendLine("## Verdict");
            sb.AppendLine();
            sb.AppendLine("- pass: hero starts at `BaseDamage=200`, primary target starts at `HP=500 Armor=50`, hit resolves to `DamageAmount=300` and `FinalDamage=200`, then invalid-target and out-of-range retries fail without any extra Health loss.");
            sb.AppendLine();
            sb.AppendLine("## Timeline");
            sb.AppendLine();
            sb.AppendLine($"- start: tick `{start.Tick}` stage=`{start.Stage}` baseDamage=`{start.HeroBaseDamage:F1}` mana=`{start.Mana:F1}` primaryHP=`{start.PrimaryTargetHealth:F1}` armor=`{start.PrimaryTargetArmor:F1}` invalidHP=`{start.InvalidTargetHealth:F1}` farHP=`{start.FarTargetHealth:F1}`");
            sb.AppendLine($"- order submitted: tick `{submitted.Tick}` scriptTick=`{submitted.ScriptTick}` target=`{submitted.LastAttemptTargetName}`");
            sb.AppendLine($"- damage applied: tick `{damageApplied.Tick}` primaryHP=`{damageApplied.PrimaryTargetHealth:F1}` damageAmount=`{damageApplied.DamageAmount:F1}` finalDamage=`{damageApplied.FinalDamage:F1}` damageTick=`{damageApplied.DamageAppliedTick}`");
            sb.AppendLine($"- invalid target blocked: tick `{invalidTargetBlocked.Tick}` fail=`{invalidTargetBlocked.LastCastFailReason}` target=`{invalidTargetBlocked.LastAttemptTargetName}` invalidHP=`{invalidTargetBlocked.InvalidTargetHealth:F1}`");
            sb.AppendLine($"- out of range blocked: tick `{outOfRangeBlocked.Tick}` fail=`{outOfRangeBlocked.LastCastFailReason}` target=`{outOfRangeBlocked.LastAttemptTargetName}` farHP=`{outOfRangeBlocked.FarTargetHealth:F1}`");
            sb.AppendLine();
            sb.AppendLine("## Reuse");
            sb.AppendLine();
            sb.AppendLine("- `AbilityDefinitionRegistry` and `EffectTemplateRegistry`: reuse the standard mod asset merge path for `assets/GAS/abilities.json` and `assets/GAS/effects.json`.");
            sb.AppendLine("- `GameEngine` startup and map loading: reuse standard mod pipeline with `LudotsCoreMod`, `ArpgDemoMod`, `InteractionShowcaseMod`.");
            sb.AppendLine("- `OrderQueue` -> `OrderBufferSystem` -> `AbilityExecSystem` -> `EffectProcessingLoopSystem`: no parallel runtime was introduced.");
            sb.AppendLine("- `Graph.Interaction.C1.CalculateDamage` and `Graph.Interaction.C1.ApplyMitigatedDamage`: reuse the core graph runtime and target blackboard buffers instead of bespoke effect listeners.");
            sb.AppendLine("- validation-scope note: `003_invalid_target_blocked` and `004_out_of_range_blocked` are blocked by showcase-local validation before enqueue; they do not certify a native GAS `CastFailed` path.");
            sb.AppendLine("- validation-scope note: current C1 effect/graphs do not add a native dead-target or range fence; bypassing the showcase-local guard would leave those negative paths outside this evidence envelope.");
            sb.AppendLine();
            sb.AppendLine("## Overlay Sample");
            sb.AppendLine();
            if (outOfRangeBlocked.OverlayLines.Count == 0)
            {
                sb.AppendLine("- no overlay text captured");
            }
            else
            {
                for (int i = 0; i < outOfRangeBlocked.OverlayLines.Count; i++)
                {
                    sb.AppendLine($"- {outOfRangeBlocked.OverlayLines[i]}");
                }
            }

            sb.AppendLine();
            sb.AppendLine("## Artifacts");
            sb.AppendLine();
            sb.AppendLine("- `artifacts/acceptance/interaction-c1-hostile-unit-damage/battle-report.md`");
            sb.AppendLine("- `artifacts/acceptance/interaction-c1-hostile-unit-damage/trace.jsonl`");
            sb.AppendLine("- `artifacts/acceptance/interaction-c1-hostile-unit-damage/path.mmd`");
            return sb.ToString();
        }

        private static string BuildTraceJsonl(IReadOnlyList<C1Snapshot> timeline)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < timeline.Count; i++)
            {
                sb.AppendLine(JsonSerializer.Serialize(timeline[i]));
            }
            return sb.ToString();
        }

        private static string BuildPathMermaid()
        {
            return """
flowchart LR
    A[Load interaction_c1_hostile_unit_damage] --> B[Autoplay warmup]
    B --> C[OrderQueue enqueues castAbility slot 0 on C1EnemyPrimary]
    C --> D[Graph.Interaction.C1.CalculateDamage writes target blackboard DamageAmount=300]
    D --> E[Graph.Interaction.C1.ApplyMitigatedDamage writes FinalDamage=200 and applies Health delta]
    E --> F[Primary target HP 500 -> 300]
    F --> G[Retry on C1EnemyInvalid]
    G --> H[Cast fails with InvalidTarget]
    H --> I[Retry on C1EnemyFar]
    I --> J[Cast fails with OutOfRange]
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

        private readonly record struct C1Snapshot(
            int Tick,
            string Step,
            int ScriptTick,
            string Stage,
            bool CastSubmitted,
            float HeroBaseDamage,
            float Mana,
            float PrimaryTargetHealth,
            float PrimaryTargetArmor,
            float InvalidTargetHealth,
            float FarTargetHealth,
            float DamageAmount,
            float FinalDamage,
            bool DamageApplied,
            int DamageAppliedTick,
            string LastAttemptTargetName,
            string LastCastFailReason,
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
