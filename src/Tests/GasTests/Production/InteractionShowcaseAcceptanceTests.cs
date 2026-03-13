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
using Ludots.Core.Gameplay.GAS;
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
    public sealed class InteractionShowcaseAcceptanceTests
    {
        private const float DeltaTime = 1f / 60f;
        private const int TotalTicks = 960;

        [Test]
        public void B1SelfBuff_ProducesHeadlessAcceptanceArtifacts()
        {
            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");
            string artifactDir = Path.Combine(repoRoot, "artifacts", "acceptance", "interaction-b1-self-buff");
            Directory.CreateDirectory(artifactDir);

            var timeline = new List<InteractionSnapshot>(TotalTicks + 1);
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
                engine.LoadMap(InteractionShowcaseIds.B1SelfBuffMapId);

                Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0), "Map load should not emit trigger errors.");

                timeline.Add(SampleSnapshot(engine, "start"));
                for (int tick = 1; tick <= TotalTicks; tick++)
                {
                    engine.Tick(DeltaTime);
                    timeline.Add(SampleSnapshot(engine, $"t{tick:000}"));
                }

                Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0), "Scenario should not emit trigger errors.");
            }
            finally
            {
                engine.Dispose();
            }

            File.WriteAllText(Path.Combine(artifactDir, "trace.jsonl"), BuildTraceJsonl(timeline), Encoding.UTF8);

            InteractionSnapshot start = timeline[0];
            InteractionSnapshot submitted = FindFirst(
                timeline,
                snapshot => snapshot.CastSubmitted && string.Equals(snapshot.Stage, "order_submitted", StringComparison.OrdinalIgnoreCase),
                "Expected autoplay system to submit the slot 0 self-cast order.");
            InteractionSnapshot active = FindFirst(
                timeline,
                snapshot => string.Equals(snapshot.Stage, "buff_active", StringComparison.OrdinalIgnoreCase) &&
                    snapshot.AttackDamage >= 149.999f &&
                    snapshot.EmpoweredCount > 0 &&
                    snapshot.HasEmpoweredTag,
                "Expected B1 self buff to reach buff_active with AttackDamage=150 and effective Status.Empowered.");
            InteractionSnapshot expired = FindFirst(
                timeline,
                snapshot => snapshot.Tick > active.Tick &&
                    snapshot.BuffExpired &&
                    snapshot.AttackDamage <= 100.001f &&
                    snapshot.EmpoweredCount == 0 &&
                    !snapshot.HasEmpoweredTag,
                "Expected B1 self buff to expire and fully clear Status.Empowered.");
            InteractionSnapshot silencedBlocked = FindFirst(
                timeline,
                snapshot => string.Equals(snapshot.Stage, "silenced_blocked", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(snapshot.LastCastFailReason, "BlockedByTag", StringComparison.Ordinal),
                "Expected silenced branch to fail with BlockedByTag.");
            InteractionSnapshot insufficientManaBlocked = FindFirst(
                timeline,
                snapshot => string.Equals(snapshot.Stage, "insufficient_mana_blocked", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(snapshot.LastCastFailReason, "InsufficientResource", StringComparison.Ordinal),
                "Expected insufficient mana branch to fail with InsufficientResource.");

            Assert.That(start.AttackDamage, Is.EqualTo(100f).Within(0.001f), "Hero should start with AttackDamage=100.");
            Assert.That(start.Mana, Is.EqualTo(100f).Within(0.001f), "Hero should start with Mana=100.");
            Assert.That(start.HasEmpoweredTag, Is.False, "Hero should start without Status.Empowered.");
            Assert.That(start.EmpoweredCount, Is.EqualTo(0), "Hero should start without granted empowered tag counts.");
            Assert.That(submitted.Tick, Is.GreaterThanOrEqualTo(1), "Autoplay should submit after scenario warmup.");
            Assert.That(active.AttackDamage, Is.EqualTo(150f).Within(0.001f), "B1 buff should multiply AttackDamage to 150.");
            Assert.That(active.Mana, Is.EqualTo(100f).Within(0.001f), "B1 showcase validates the mana gate only; successful cast should keep mana unchanged.");
            Assert.That(active.HasEmpoweredTag, Is.True, "Effective Status.Empowered should be true while buff is active.");
            Assert.That(active.EmpoweredCount, Is.EqualTo(1), "Granted empowered tag count should be exactly 1 while buff is active.");
            Assert.That(expired.Tick, Is.GreaterThan(active.Tick), "Expiry must happen after buff activation.");
            Assert.That(expired.AttackDamage, Is.EqualTo(100f).Within(0.001f), "AttackDamage should return to baseline after expiry.");
            Assert.That(expired.HasEmpoweredTag, Is.False, "Effective Status.Empowered should clear on expiry.");
            Assert.That(expired.EmpoweredCount, Is.EqualTo(0), "Granted empowered tag count should clear on expiry.");
            Assert.That(silencedBlocked.AttackDamage, Is.EqualTo(100f).Within(0.001f), "Silenced branch must not apply the buff.");
            Assert.That(silencedBlocked.HasEmpoweredTag, Is.False, "Silenced branch must not grant Status.Empowered.");
            Assert.That(insufficientManaBlocked.Mana, Is.EqualTo(0f).Within(0.001f), "Insufficient mana branch should run at Mana=0.");
            Assert.That(insufficientManaBlocked.LastCastFailAttribute, Is.EqualTo("Mana"), "Insufficient mana branch should report Mana as the failing attribute.");
            Assert.That(insufficientManaBlocked.LastCastFailDelta, Is.EqualTo(50f).Within(0.001f), "Insufficient mana branch should report the missing 50 mana.");

            File.WriteAllText(
                Path.Combine(artifactDir, "battle-report.md"),
                BuildBattleReport(timeline, start, submitted, active, expired, silencedBlocked, insufficientManaBlocked),
                Encoding.UTF8);
            File.WriteAllText(Path.Combine(artifactDir, "path.mmd"), BuildPathMermaid(), Encoding.UTF8);
        }

        private static InteractionSnapshot SampleSnapshot(GameEngine engine, string step)
        {
            Entity hero = FindEntity(engine.World, InteractionShowcaseIds.HeroName);
            float attackDamage = 0f;
            float mana = 0f;
            bool empoweredTag = false;
            int empoweredCount = 0;
            bool heroPresent = hero != Entity.Null;

            if (heroPresent)
            {
                int attackDamageId = AttributeRegistry.Register("AttackDamage");
                int manaId = AttributeRegistry.Register("Mana");
                int empoweredTagId = TagRegistry.Register("Status.Empowered");

                if (engine.World.Has<AttributeBuffer>(hero))
                {
                    ref var attributes = ref engine.World.Get<AttributeBuffer>(hero);
                    attackDamage = attributes.GetCurrent(attackDamageId);
                    mana = attributes.GetCurrent(manaId);
                }

                if (engine.World.Has<GameplayTagContainer>(hero) && engine.GetService(CoreServiceKeys.TagOps) is TagOps tagOps)
                {
                    ref var tags = ref engine.World.Get<GameplayTagContainer>(hero);
                    empoweredTag = tagOps.HasTag(ref tags, empoweredTagId, TagSense.Effective);
                }

                if (engine.World.Has<TagCountContainer>(hero))
                {
                    empoweredCount = engine.World.Get<TagCountContainer>(hero).GetCount(empoweredTagId);
                }
            }

            string stage = ReadString(engine, InteractionShowcaseRuntimeKeys.Stage, "not_started");
            int scriptTick = ReadInt(engine, InteractionShowcaseRuntimeKeys.ScriptTick, 0);
            bool castSubmitted = ReadBool(engine, InteractionShowcaseRuntimeKeys.CastSubmitted);
            int castSubmittedTick = ReadInt(engine, InteractionShowcaseRuntimeKeys.CastSubmittedTick, -1);
            bool buffObserved = ReadBool(engine, InteractionShowcaseRuntimeKeys.BuffObserved);
            bool buffExpired = ReadBool(engine, InteractionShowcaseRuntimeKeys.BuffExpired);
            string lastCastFailReason = ReadString(engine, InteractionShowcaseRuntimeKeys.LastCastFailReason, string.Empty);
            int lastCastFailTick = ReadInt(engine, InteractionShowcaseRuntimeKeys.LastCastFailTick, -1);
            string lastCastFailAttribute = ReadString(engine, InteractionShowcaseRuntimeKeys.LastCastFailAttribute, string.Empty);
            float lastCastFailDelta = ReadFloat(engine, InteractionShowcaseRuntimeKeys.LastCastFailDelta, 0f);

            return new InteractionSnapshot(
                Tick: engine.GameSession.CurrentTick,
                Step: step,
                ScriptTick: scriptTick,
                Stage: stage,
                HeroPresent: heroPresent,
                AttackDamage: attackDamage,
                Mana: mana,
                HasEmpoweredTag: empoweredTag,
                EmpoweredCount: empoweredCount,
                CastSubmitted: castSubmitted,
                CastSubmittedTick: castSubmittedTick,
                BuffObserved: buffObserved,
                BuffExpired: buffExpired,
                LastCastFailReason: lastCastFailReason,
                LastCastFailTick: lastCastFailTick,
                LastCastFailAttribute: lastCastFailAttribute,
                LastCastFailDelta: lastCastFailDelta,
                OverlayLines: TakeOverlayFrame(engine.GetService(CoreServiceKeys.ScreenOverlayBuffer) as ScreenOverlayBuffer));
        }

        private static InteractionSnapshot FindFirst(
            IReadOnlyList<InteractionSnapshot> timeline,
            Func<InteractionSnapshot, bool> predicate,
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
            IReadOnlyList<InteractionSnapshot> timeline,
            InteractionSnapshot start,
            InteractionSnapshot submitted,
            InteractionSnapshot active,
            InteractionSnapshot expired,
            InteractionSnapshot silencedBlocked,
            InteractionSnapshot insufficientManaBlocked)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# Interaction Showcase Acceptance");
            sb.AppendLine();
            sb.AppendLine("- scenario: `instant_press/b1_self_buff`");
            sb.AppendLine("- doc source: `docs/architecture/interaction/features/instant_press/b1_self_buff.md`");
            sb.AppendLine("- runtime wiring: `Ability.Interaction.B1SelfBuff` on slot `0`, applying `Effect.Interaction.B1SelfBuffBuff` through the standard GAS pipeline");
            sb.AppendLine($"- acceptance window: `{TotalTicks}` engine frames");
            sb.AppendLine();
            sb.AppendLine("## Verdict");
            sb.AppendLine();
            sb.AppendLine("- pass: hero starts at `AttackDamage=100` / `Mana=100`, reaches `AttackDamage=150` with effective `Status.Empowered`, returns to baseline on expiry, then demonstrates both silenced and insufficient-mana failure branches.");
            sb.AppendLine();
            sb.AppendLine("## Timeline");
            sb.AppendLine();
            sb.AppendLine($"- start: tick `{start.Tick}` stage=`{start.Stage}` attackDamage=`{start.AttackDamage:F1}` mana=`{start.Mana:F1}` empowered=`{start.HasEmpoweredTag}` count=`{start.EmpoweredCount}`");
            sb.AppendLine($"- order submitted: tick `{submitted.Tick}` scriptTick=`{submitted.ScriptTick}` stage=`{submitted.Stage}`");
            sb.AppendLine($"- first headless sample with `stage=buff_active && effectiveTag=true`: tick `{active.Tick}` attackDamage=`{active.AttackDamage:F1}` empowered=`{active.HasEmpoweredTag}` count=`{active.EmpoweredCount}`");
            sb.AppendLine($"- buff expired: tick `{expired.Tick}` attackDamage=`{expired.AttackDamage:F1}` empowered=`{expired.HasEmpoweredTag}` count=`{expired.EmpoweredCount}`");
            sb.AppendLine($"- silenced blocked: tick `{silencedBlocked.Tick}` fail=`{silencedBlocked.LastCastFailReason}` stage=`{silencedBlocked.Stage}`");
            sb.AppendLine($"- insufficient mana: tick `{insufficientManaBlocked.Tick}` fail=`{insufficientManaBlocked.LastCastFailReason}` attr=`{insufficientManaBlocked.LastCastFailAttribute}` delta=`{insufficientManaBlocked.LastCastFailDelta:F1}` mana=`{insufficientManaBlocked.Mana:F1}`");
            sb.AppendLine("- timing note: headless acceptance keys on the first fixed-step sample where `stage=buff_active` and `EffectiveTag=true` are both visible; launcher visual capture can show the same buff-active checkpoint one presentation boundary earlier.");
            sb.AppendLine();
            sb.AppendLine("## Reuse");
            sb.AppendLine();
            sb.AppendLine("- `AbilityDefinitionRegistry` and `EffectTemplateRegistry`: reuse the standard mod asset merge path for `assets/GAS/abilities.json` and `assets/GAS/effects.json`.");
            sb.AppendLine("- `GameEngine` startup and map loading: reuse standard mod pipeline with `LudotsCoreMod`, `ArpgDemoMod`, `InteractionShowcaseMod`.");
            sb.AppendLine("- `OrderQueue` -> `OrderBufferSystem` -> `AbilityExecSystem` -> `EffectProcessingLoopSystem`: no parallel runtime was introduced.");
            sb.AppendLine("- `AbilityActivationPreconditionEvaluator` and `GasPresentationEventBuffer`: reuse core activation guards and presentation fail events for the no-mana / silenced branches.");
            sb.AppendLine();
            sb.AppendLine("## Overlay Sample");
            sb.AppendLine();
            if (insufficientManaBlocked.OverlayLines.Count == 0)
            {
                sb.AppendLine("- no overlay text captured");
            }
            else
            {
                for (int i = 0; i < insufficientManaBlocked.OverlayLines.Count; i++)
                {
                    sb.AppendLine($"- {insufficientManaBlocked.OverlayLines[i]}");
                }
            }

            sb.AppendLine();
            sb.AppendLine("## Artifacts");
            sb.AppendLine();
            sb.AppendLine("- `artifacts/acceptance/interaction-b1-self-buff/battle-report.md`");
            sb.AppendLine("- `artifacts/acceptance/interaction-b1-self-buff/trace.jsonl`");
            sb.AppendLine("- `artifacts/acceptance/interaction-b1-self-buff/path.mmd`");
            sb.AppendLine();
            sb.AppendLine($"- snapshots captured: `{timeline.Count}`");
            return sb.ToString();
        }

        private static string BuildTraceJsonl(IReadOnlyList<InteractionSnapshot> timeline)
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
    A[Load interaction_b1_self_buff] --> B[Autoplay warmup]
    B --> C[OrderQueue enqueues castAbility slot 0]
    C --> D[Ability.Interaction.B1SelfBuff emits Effect.Interaction.B1SelfBuffBuff]
    D --> E[Buff active: AttackDamage 100 -> 150 and Status.Empowered effective=true]
    E --> F[Duration reaches 300 ticks]
    F --> G[Buff removed: AttackDamage returns to 100 and Status.Empowered clears]
    G --> H[Apply Status.Silenced and retry]
    H --> I[Cast fails with BlockedByTag]
    I --> J[Set Mana to 0 and retry]
    J --> K[Cast fails with InsufficientResource on Mana delta 50]
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

        private static float ReadFloat(GameEngine engine, string key, float fallback)
        {
            return engine.GlobalContext.TryGetValue(key, out var value) && value is float number
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

        private readonly record struct InteractionSnapshot(
            int Tick,
            string Step,
            int ScriptTick,
            string Stage,
            bool HeroPresent,
            float AttackDamage,
            float Mana,
            bool HasEmpoweredTag,
            int EmpoweredCount,
            bool CastSubmitted,
            int CastSubmittedTick,
            bool BuffObserved,
            bool BuffExpired,
            string LastCastFailReason,
            int LastCastFailTick,
            string LastCastFailAttribute,
            float LastCastFailDelta,
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
