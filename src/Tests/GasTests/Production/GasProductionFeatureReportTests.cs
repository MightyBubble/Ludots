using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Arch.Core;
using Arch.Core.Extensions;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Spatial;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production
{
    [TestFixture]
    public sealed class GasProductionFeatureReportTests
    {
        private sealed record StepResult(string Name, bool Passed, string Detail);
        private sealed record ScenarioResult(string Name, List<StepResult> Steps);

        [Test]
        public void GenerateGasProductionReport()
        {
            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");
            string modsRoot = Path.Combine(repoRoot, "src", "Mods");
            string reportPath = Path.Combine(repoRoot, "artifacts", "GasProductionReport.md");

            var scenarios = new List<ScenarioResult>();
            try
            {
                scenarios.Add(RunScenario("MOBA", assetsRoot, modsRoot, new[] { "LudotsCoreMod", "MobaDemoMod" }, "entry", RunMobaScenario));
                scenarios.Add(RunScenario("TCG/Modify", assetsRoot, modsRoot, new[] { "LudotsCoreMod", "TcgDemoMod" }, "tcg_modify", RunTcgModifyScenario));
                scenarios.Add(RunScenario("TCG/Hook", assetsRoot, modsRoot, new[] { "LudotsCoreMod", "TcgDemoMod" }, "tcg_hook", RunTcgHookScenario));
                scenarios.Add(RunScenario("ARPG", assetsRoot, modsRoot, new[] { "LudotsCoreMod", "ArpgDemoMod" }, "arpg_entry", RunArpgScenario));
                scenarios.Add(RunScenario("4X", assetsRoot, modsRoot, new[] { "LudotsCoreMod", "FourXDemoMod" }, "fourx_entry", RunFourXScenario));
            }
            finally
            {
                Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
                File.WriteAllText(reportPath, RenderMarkdown(scenarios), Encoding.UTF8);
                TestContext.Progress.WriteLine($"[GAS] Report written: {reportPath}");
            }

            for (int i = 0; i < scenarios.Count; i++)
            {
                var s = scenarios[i];
                for (int j = 0; j < s.Steps.Count; j++)
                {
                    if (!s.Steps[j].Passed)
                    {
                        Assert.Fail($"Scenario '{s.Name}' failed: {s.Steps[j].Name} - {s.Steps[j].Detail}");
                    }
                }
            }
        }

        private static ScenarioResult RunScenario(
            string name,
            string assetsRoot,
            string modsRoot,
            string[] mods,
            string mapId,
            Action<GameEngine, List<StepResult>> scenario)
        {
            var steps = new List<StepResult>();
            var engine = new GameEngine();
            try
            {
                var modPaths = new List<string>(mods.Length);
                for (int i = 0; i < mods.Length; i++)
                {
                    modPaths.Add(Path.Combine(modsRoot, mods[i]));
                }

                engine.InitializeWithConfigPipeline(modPaths, assetsRoot);
                InstallDummyInput(engine);
                engine.Start();
                engine.LoadMap(mapId);

                for (int i = 0; i < 5; i++) engine.Tick(1f / 60f);

                if (engine.TriggerManager.Errors.Count > 0)
                {
                    steps.Add(new StepResult("Startup triggers", false, engine.TriggerManager.Errors[0].Exception.Message));
                    return new ScenarioResult(name, steps);
                }

                scenario(engine, steps);

                steps.Add(new StepResult("Trigger errors after scenario", engine.TriggerManager.Errors.Count == 0, $"Errors={engine.TriggerManager.Errors.Count}"));
                return new ScenarioResult(name, steps);
            }
            finally
            {
                engine.Dispose();
            }
        }

        private static void RunMobaScenario(GameEngine engine, List<StepResult> steps)
        {
            var world = engine.World;
            var (hero, enemy1, enemy2) = FindByNames(world, "Hero", "Enemy1", "Enemy2");
            int healthId = AttributeRegistry.GetId("Health");
            float e1Before = world.Get<AttributeBuffer>(enemy1).GetCurrent(healthId);

            CastAbility(engine, hero, enemy1, slot: 0);
            Tick(engine, 60);
            float e1AfterQ = world.Get<AttributeBuffer>(enemy1).GetCurrent(healthId);
            steps.Add(CheckNear("Cast Q damages Enemy1", e1Before - 20f, e1AfterQ));

            var tagOps = (TagOps)engine.GlobalContext[ContextKeys.TagOps];
            int gcd = TagRegistry.Register("Cooldown.GCD");
            ref var heroTags = ref world.Get<GameplayTagContainer>(hero);
            steps.Add(new StepResult("Global cooldown expires", !tagOps.HasTag(ref heroTags, gcd, TagSense.Effective), "Expected Cooldown.GCD not present"));

            float e1BeforeE = e1AfterQ;
            float e2BeforeE = world.Get<AttributeBuffer>(enemy2).GetCurrent(healthId);

            var (hx, hy) = world.Get<WorldPositionCm>(hero).Value.ToInt();
            var (tx, ty) = world.Get<WorldPositionCm>(enemy1).Value.ToInt();
            int dx = tx - hx;
            int dy = ty - hy;
            int dir = 0;
            if (dx != 0 || dy != 0)
            {
                var rad = Fix64Math.Atan2Fast(Fix64.FromInt(dy), Fix64.FromInt(dx));
                dir = (rad * Fix64.FromInt(180) / Fix64.Pi).RoundToInt();
                if (dir < 0) dir += 360;
            }
            Span<Entity> buf = stackalloc Entity[32];
            var coneRes = engine.SpatialQueries.QueryCone(new WorldCmInt2(hx, hy), dir, halfAngleDeg: 45, rangeCm: 800, buf);
            steps.Add(new StepResult("SpatialQuery cone finds targets", coneRes.Count >= 2, $"Count={coneRes.Count} Dropped={coneRes.Dropped}"));
            var arrBuf = new Entity[32];
            var coneResArr = engine.SpatialQueries.QueryCone(new WorldCmInt2(hx, hy), dir, halfAngleDeg: 45, rangeCm: 800, arrBuf);
            steps.Add(new StepResult("SpatialQuery cone works with array buffer", coneResArr.Count >= 2, $"Count={coneResArr.Count} Dropped={coneResArr.Dropped}"));

            var templates = (EffectTemplateRegistry)engine.GlobalContext[ContextKeys.EffectTemplateRegistry];
            int eTplId = EffectTemplateIdRegistry.GetId("Effect.Moba.Damage.E");
            if (!templates.TryGetRef(eTplId, out int eTplIdx))
            {
                steps.Add(new StepResult("Effect template E exists", false, $"TemplateId={eTplId}"));
            }
            else
            {
                ref readonly var eTpl = ref templates.GetRef(eTplIdx);
                steps.Add(new StepResult("Entities alive for resolver", world.IsAlive(hero) && world.IsAlive(enemy1), $"HeroAlive={world.IsAlive(hero)} Enemy1Alive={world.IsAlive(enemy1)}"));
                steps.Add(new StepResult("Effect template E has TargetQuery", eTpl.TargetQuery.Kind != TargetResolverKind.None, $"Kind={eTpl.TargetQuery.Kind}"));
                steps.Add(new StepResult("Effect template E TargetQuery params", eTpl.TargetQuery.Spatial.RadiusCm > 0, $"Radius={eTpl.TargetQuery.Spatial.RadiusCm} HalfAngle={eTpl.TargetQuery.Spatial.HalfAngleDeg}"));
                steps.Add(new StepResult("Effect template E has PayloadEffect", eTpl.TargetDispatch.PayloadEffectTemplateId > 0, $"PayloadTplId={eTpl.TargetDispatch.PayloadEffectTemplateId}"));

                var cmds = new List<FanOutCommand>(8);
                var budget = new RootBudgetTable(64);
                var ctx = new EffectContext { RootId = 123, Source = hero, Target = enemy1, TargetContext = default };
                var tmpBuf = new Entity[64];
                int dropped = 0;
                int cand = TargetResolverFanOutHelper.ResolveTargets(world, in ctx, in eTpl.TargetQuery, engine.SpatialQueries, tmpBuf);
                steps.Add(new StepResult("TargetResolver ResolveTargets returns candidates", cand >= 2, $"Count={cand}"));
                TargetResolverFanOutHelper.CollectFanOutTargets(
                    world, in ctx, in eTpl.TargetQuery, in eTpl.TargetFilter, in eTpl.TargetDispatch,
                    engine.SpatialQueries, budget, cmds, tmpBuf, ref dropped);
                steps.Add(new StepResult("TargetResolver creates fan-out commands", cmds.Count >= 2, $"Count={cmds.Count} Dropped={dropped}"));
            }

            CastAbility(engine, hero, enemy1, slot: 2);
            Tick(engine, 10);
            float e1AfterE = world.Get<AttributeBuffer>(enemy1).GetCurrent(healthId);
            float e2AfterE = world.Get<AttributeBuffer>(enemy2).GetCurrent(healthId);
            steps.Add(CheckNear("Cast E hits Enemy1", e1BeforeE - 5f, e1AfterE));
            steps.Add(CheckNear("Cast E hits Enemy2", e2BeforeE - 5f, e2AfterE));

            var effectRequests = (EffectRequestQueue)engine.GlobalContext[ContextKeys.EffectRequestQueue];
            int dotId = EffectTemplateIdRegistry.GetId("Effect.Moba.DOT.Burn");
            effectRequests.Publish(new EffectRequest { RootId = 0, Source = hero, Target = enemy1, TargetContext = default, TemplateId = dotId });
            Tick(engine, 25);
            float e1AfterDot = world.Get<AttributeBuffer>(enemy1).GetCurrent(healthId);
            steps.Add(CheckNear("DoT Burn ticks once", e1AfterE - 3f, e1AfterDot));

            int hotId = EffectTemplateIdRegistry.GetId("Effect.Moba.HOT.Regen");
            int qId = EffectTemplateIdRegistry.GetId("Effect.Moba.Damage.Q");
            effectRequests.Publish(new EffectRequest { RootId = 0, Source = hero, Target = hero, TargetContext = default, TemplateId = qId });
            Tick(engine, 5);
            float heroDamaged = world.Get<AttributeBuffer>(hero).GetCurrent(healthId);
            effectRequests.Publish(new EffectRequest { RootId = 0, Source = hero, Target = hero, TargetContext = default, TemplateId = hotId });
            Tick(engine, 35);
            float heroAfterHot = world.Get<AttributeBuffer>(hero).GetCurrent(healthId);
            steps.Add(new StepResult("HoT Regen restores Health (clamped to base)", heroAfterHot > heroDamaged && heroAfterHot <= 100f, $"HeroHealth {heroDamaged} -> {heroAfterHot}"));
        }

        private static void RunTcgModifyScenario(GameEngine engine, List<StepResult> steps)
        {
            var world = engine.World;
            var (hero, enemy) = FindByNames2(world, "TcgHero", "TcgEnemy");
            int healthId = AttributeRegistry.GetId("Health");
            float before = world.Get<AttributeBuffer>(enemy).GetCurrent(healthId);
            CastAbility(engine, hero, enemy, slot: 0);
            Tick(engine, 10);
            float after = world.Get<AttributeBuffer>(enemy).GetCurrent(healthId);
            steps.Add(CheckNear("Response Modify reduces Fireball damage", before - 20f, after));
        }

        private static void RunTcgHookScenario(GameEngine engine, List<StepResult> steps)
        {
            var world = engine.World;
            var (hero, enemy) = FindByNames2(world, "TcgHero", "TcgEnemy");
            int healthId = AttributeRegistry.GetId("Health");
            float before = world.Get<AttributeBuffer>(enemy).GetCurrent(healthId);
            CastAbility(engine, hero, enemy, slot: 0);
            Tick(engine, 10);
            float after = world.Get<AttributeBuffer>(enemy).GetCurrent(healthId);
            steps.Add(CheckNear("Response Hook cancels Fireball", before, after));
        }

        private static void RunArpgScenario(GameEngine engine, List<StepResult> steps)
        {
            var world = engine.World;
            var (hero, enemy) = FindByNames2(world, "ArpgHero", "ArpgEnemy");
            int healthId = AttributeRegistry.GetId("Health");

            var templates = (EffectTemplateRegistry)engine.GlobalContext[ContextKeys.EffectTemplateRegistry];
            int arrowTplId = EffectTemplateIdRegistry.GetId("Effect.Arpg.FireArrow");
            if (templates.TryGetRef(arrowTplId, out int arrowIdx))
            {
                ref readonly var arrowTpl = ref templates.GetRef(arrowIdx);
                steps.Add(new StepResult("ARPG FireArrow projectile config", arrowTpl.Projectile.Speed > 0 && arrowTpl.Projectile.ImpactEffectTemplateId > 0, $"Speed={arrowTpl.Projectile.Speed} ImpactTplId={arrowTpl.Projectile.ImpactEffectTemplateId}"));
            }

            int wolfTplId = EffectTemplateIdRegistry.GetId("Effect.Arpg.SummonWolf");
            if (templates.TryGetRef(wolfTplId, out int wolfIdx))
            {
                ref readonly var wolfTpl = ref templates.GetRef(wolfIdx);
                steps.Add(new StepResult("ARPG SummonWolf unit config", wolfTpl.UnitCreation.UnitTypeId > 0 && wolfTpl.UnitCreation.Count > 0, $"UnitTypeId={wolfTpl.UnitCreation.UnitTypeId} Count={wolfTpl.UnitCreation.Count}"));
            }

            float enemyBefore = world.Get<AttributeBuffer>(enemy).GetCurrent(healthId);
            CastAbility(engine, hero, enemy, slot: 0);
            Tick(engine, 120);
            float enemyAfter = world.Get<AttributeBuffer>(enemy).GetCurrent(healthId);
            steps.Add(new StepResult("Projectile applies poison DoT", enemyAfter < enemyBefore, $"EnemyHealth {enemyBefore} -> {enemyAfter}"));

            CastAbility(engine, hero, hero, slot: 2);
            Tick(engine, 10);
            bool wolfSpawned = HasAnyNamePrefix(world, "Unit:Unit.Wolf");
            steps.Add(new StepResult("CreateUnit spawns Wolf", wolfSpawned, "Expect Unit:Unit.Wolf entity"));

            var effectRequests = (EffectRequestQueue)engine.GlobalContext[ContextKeys.EffectRequestQueue];
            int poisonId = EffectTemplateIdRegistry.GetId("Effect.Arpg.Poison");
            effectRequests.Publish(new EffectRequest { RootId = 0, Source = enemy, Target = hero, TargetContext = default, TemplateId = poisonId });
            Tick(engine, 15);
            float heroBeforePotion = world.Get<AttributeBuffer>(hero).GetCurrent(healthId);
            CastAbility(engine, hero, hero, slot: 1);
            Tick(engine, 5);
            float heroAfterPotion = world.Get<AttributeBuffer>(hero).GetCurrent(healthId);
            steps.Add(new StepResult("HealPotion restores Health (clamped to base)", heroAfterPotion > heroBeforePotion && heroAfterPotion <= 100f, $"HeroHealth {heroBeforePotion} -> {heroAfterPotion}"));

            CastAbility(engine, hero, hero, slot: 3);
            Tick(engine, 5);
            var tagOps = (TagOps)engine.GlobalContext[ContextKeys.TagOps];
            int stunned = TagRegistry.Register("Status.Stunned");
            int cannotMove = TagRegistry.Register("Status.CannotMove");
            ref var tags = ref world.Get<GameplayTagContainer>(hero);
            steps.Add(new StepResult("TagRule attached tag appears (Stunned->CannotMove)", tagOps.HasTag(ref tags, cannotMove, TagSense.Effective), "Expected Status.CannotMove"));

            Tick(engine, 120);
            steps.Add(new StepResult("TimedTag expires", !tagOps.HasTag(ref tags, stunned, TagSense.Effective), "Expected Status.Stunned expired"));
        }

        private static void RunFourXScenario(GameEngine engine, List<StepResult> steps)
        {
            var world = engine.World;
            var (hero, site) = FindByNames2(world, "Governor", "OutpostSite");

            var templates = (EffectTemplateRegistry)engine.GlobalContext[ContextKeys.EffectTemplateRegistry];
            int buildTplId = EffectTemplateIdRegistry.GetId("Effect.4X.BuildOutpost");
            if (templates.TryGetRef(buildTplId, out int buildIdx))
            {
                ref readonly var buildTpl = ref templates.GetRef(buildIdx);
                steps.Add(new StepResult("4X BuildOutpost unit config", buildTpl.UnitCreation.UnitTypeId > 0 && buildTpl.UnitCreation.Count > 0, $"UnitTypeId={buildTpl.UnitCreation.UnitTypeId} Count={buildTpl.UnitCreation.Count}"));
            }

            CastAbility(engine, hero, site, slot: 1);
            Tick(engine, 5);
            var tagOps = (TagOps)engine.GlobalContext[ContextKeys.TagOps];
            int colonizing = TagRegistry.Register("Status.Colonizing");
            int working = TagRegistry.Register("Status.Working");
            ref var tags = ref world.Get<GameplayTagContainer>(hero);
            steps.Add(new StepResult("TagRule attaches Working when Colonizing", tagOps.HasTag(ref tags, working, TagSense.Effective), "Expected Status.Working"));

            CastAbility(engine, hero, hero, slot: 2);
            Tick(engine, 5);
            steps.Add(new StepResult("TagRule removes Colonizing when Blocked", !tagOps.HasTag(ref tags, colonizing, TagSense.Effective), "Expected Status.Colonizing removed"));

            CastAbility(engine, hero, site, slot: 0);
            Tick(engine, 15);
            bool outpostSpawned = HasAnyNamePrefix(world, "Unit:Unit.Outpost");
            steps.Add(new StepResult("CreateUnit spawns Outpost", outpostSpawned, "Expect Unit:Unit.Outpost entity"));
        }

        private static void InstallDummyInput(GameEngine engine)
        {
            var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
            var inputHandler = new PlayerInputHandler(new NullInputBackend(), inputConfig);
            engine.GlobalContext[ContextKeys.InputHandler] = inputHandler;
            engine.GlobalContext[ContextKeys.UiCaptured] = false;
        }

        private sealed class NullInputBackend : IInputBackend
        {
            public float GetAxis(string devicePath) => 0f;
            public bool GetButton(string devicePath) => false;
            public System.Numerics.Vector2 GetMousePosition() => System.Numerics.Vector2.Zero;
            public float GetMouseWheel() => 0f;
            public void EnableIME(bool enable) { }
            public void SetIMECandidatePosition(int x, int y) { }
            public string GetCharBuffer() => string.Empty;
        }

        private static void CastAbility(GameEngine engine, Entity actor, Entity target, int slot)
        {
            var orderQueue = (OrderQueue)engine.GlobalContext[ContextKeys.OrderQueue];
            int castAbilityTagId = engine.MergedConfig.Constants.OrderTags["castAbility"];
            orderQueue.TryEnqueue(new Order
            {
                OrderTagId = castAbilityTagId,
                Actor = actor,
                Target = target,
                Args = new OrderArgs { I0 = slot }
            });
        }

        private static void Tick(GameEngine engine, int frames)
        {
            for (int i = 0; i < frames; i++) engine.Tick(1f / 60f);
        }

        private static (Entity a, Entity b) FindByNames2(World world, string aName, string bName)
        {
            Entity a = Entity.Null;
            Entity b = Entity.Null;
            var q = new QueryDescription().WithAll<Name>();
            world.Query(in q, (Entity e, ref Name name) =>
            {
                if (a == Entity.Null && string.Equals(name.Value, aName, StringComparison.OrdinalIgnoreCase)) a = e;
                if (b == Entity.Null && string.Equals(name.Value, bName, StringComparison.OrdinalIgnoreCase)) b = e;
            });
            if (a == Entity.Null) throw new InvalidOperationException($"Missing entity '{aName}'.");
            if (b == Entity.Null) throw new InvalidOperationException($"Missing entity '{bName}'.");
            return (a, b);
        }

        private static (Entity hero, Entity enemy1, Entity enemy2) FindByNames(World world, string heroName, string e1Name, string e2Name)
        {
            Entity hero = Entity.Null;
            Entity e1 = Entity.Null;
            Entity e2 = Entity.Null;
            var q = new QueryDescription().WithAll<Name>();
            world.Query(in q, (Entity e, ref Name name) =>
            {
                if (hero == Entity.Null && string.Equals(name.Value, heroName, StringComparison.OrdinalIgnoreCase)) hero = e;
                if (e1 == Entity.Null && string.Equals(name.Value, e1Name, StringComparison.OrdinalIgnoreCase)) e1 = e;
                if (e2 == Entity.Null && string.Equals(name.Value, e2Name, StringComparison.OrdinalIgnoreCase)) e2 = e;
            });
            if (hero == Entity.Null || e1 == Entity.Null || e2 == Entity.Null) throw new InvalidOperationException("Missing required entities for MOBA scenario.");
            return (hero, e1, e2);
        }

        private static bool HasAnyNamePrefix(World world, string prefix)
        {
            bool found = false;
            var q = new QueryDescription().WithAll<Name>();
            world.Query(in q, (Entity e, ref Name name) =>
            {
                if (!found && name.Value != null && name.Value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) found = true;
            });
            return found;
        }

        private static StepResult CheckNear(string name, float expected, float actual, float eps = 0.001f)
        {
            bool ok = MathF.Abs(expected - actual) <= eps;
            return new StepResult(name, ok, $"Expected={expected} Actual={actual}");
        }

        private static string RenderMarkdown(List<ScenarioResult> scenarios)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# GAS 生产验证报告");
            sb.AppendLine();
            sb.AppendLine($"生成时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();
            for (int i = 0; i < scenarios.Count; i++)
            {
                var s = scenarios[i];
                sb.AppendLine($"## {s.Name}");
                sb.AppendLine();
                for (int j = 0; j < s.Steps.Count; j++)
                {
                    var step = s.Steps[j];
                    sb.Append("- ");
                    sb.Append(step.Passed ? "[PASS] " : "[FAIL] ");
                    sb.Append(step.Name);
                    if (!string.IsNullOrWhiteSpace(step.Detail))
                    {
                        sb.Append(" — ");
                        sb.Append(step.Detail);
                    }
                    sb.AppendLine();
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }

        private static string FindRepoRoot()
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrWhiteSpace(dir))
            {
                var candidate = Path.Combine(dir, "src", "Core", "Ludots.Core.csproj");
                if (File.Exists(candidate)) return dir;
                dir = Path.GetDirectoryName(dir);
            }
            throw new InvalidOperationException("Could not locate repo root.");
        }

        private static bool HasAny<T>(World world)
        {
            bool found = false;
            var q = new QueryDescription().WithAll<T>();
            world.Query(in q, (Entity e) =>
            {
                found = true;
            });
            return found;
        }
    }
}
