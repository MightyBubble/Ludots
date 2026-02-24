using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
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
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production
{
    /// <summary>
    /// MUD-style demo-log tests for every Demo Mod.
    /// Each test loads mods via GameEngine pipeline, entities come from Map JSON / Triggers,
    /// runs gameplay scenarios, and writes a human-readable .log file.
    /// </summary>
    [TestFixture]
    public sealed class ProductionModDemoLogTests
    {
        // ─────────────────────────────────────────────────
        //  MOBA
        // ─────────────────────────────────────────────────
        [Test]
        public void MobaDemoLog()
        {
            var sb = new StringBuilder();
            sb.AppendLine("═══════════════════════════════════════════");
            sb.AppendLine("[MOBA] 进入英雄竞技场。");
            sb.AppendLine("═══════════════════════════════════════════");

            RunWithEngine(new[] { "LudotsCoreMod", "MobaDemoMod" }, "entry", engine =>
            {
                var world = engine.World;
                var (hero, enemy1, enemy2) = FindEntities3(world, "Hero", "Enemy1", "Enemy2");
                int healthId = EnsureAttr("Health");

                LogEntityState(sb, "[MOBA]", "初始状态", world, hero, "Hero", new[] { healthId }, new[] { "Health" });
                LogEntityState(sb, "[MOBA]", "初始状态", world, enemy1, "Enemy1", new[] { healthId }, new[] { "Health" });
                LogEntityState(sb, "[MOBA]", "初始状态", world, enemy2, "Enemy2", new[] { healthId }, new[] { "Health" });

                // ── Q: 单体伤害 ──
                sb.AppendLine("[MOBA] Hero 对 Enemy1 施放【Q - 直接伤害 -20HP】。");
                CastAbility(engine, hero, enemy1, slot: 0);
                Tick(engine, 60);
                LogEntityState(sb, "[MOBA]", "Q 后", world, enemy1, "Enemy1", new[] { healthId }, new[] { "Health" });

                float e1Hp = world.Get<AttributeBuffer>(enemy1).GetCurrent(healthId);
                Assert.That(e1Hp, Is.EqualTo(80f).Within(0.01f), "Q should deal 20 damage");

                // ── W: 自我治疗 ──
                // 先让 hero 受点伤
                var effectRequests = (EffectRequestQueue)engine.GlobalContext[ContextKeys.EffectRequestQueue];
                int qTplId = EffectTemplateIdRegistry.GetId("Effect.Moba.Damage.Q");
                effectRequests.Publish(new EffectRequest { RootId = 0, Source = enemy1, Target = hero, TargetContext = default, TemplateId = qTplId });
                Tick(engine, 5);
                float heroHpDamaged = world.Get<AttributeBuffer>(hero).GetCurrent(healthId);
                sb.AppendLine($"[MOBA] Hero 被 Enemy1 攻击，当前 HP={heroHpDamaged:F1}。");

                sb.AppendLine("[MOBA] Hero 施放【W - 自我治疗 +15HP】。");
                CastAbility(engine, hero, hero, slot: 1);
                Tick(engine, 60);
                float heroHpHealed = world.Get<AttributeBuffer>(hero).GetCurrent(healthId);
                LogEntityState(sb, "[MOBA]", "W 后", world, hero, "Hero", new[] { healthId }, new[] { "Health" });
                Assert.That(heroHpHealed, Is.GreaterThan(heroHpDamaged), "W should heal hero");

                // ── E: 锥形 AOE ──
                sb.AppendLine("[MOBA] Hero 对 Enemy1 方向施放【E - 锥形搜索 AOE -5HP】。");
                CastAbility(engine, hero, enemy1, slot: 2);
                Tick(engine, 60);
                LogEntityState(sb, "[MOBA]", "E 后", world, enemy1, "Enemy1", new[] { healthId }, new[] { "Health" });
                LogEntityState(sb, "[MOBA]", "E 后", world, enemy2, "Enemy2", new[] { healthId }, new[] { "Health" });

                // ── DoT: 持续伤害 ──
                int dotId = EffectTemplateIdRegistry.GetId("Effect.Moba.DOT.Burn");
                float e1BeforeDot = world.Get<AttributeBuffer>(enemy1).GetCurrent(healthId);
                sb.AppendLine("[MOBA] 对 Enemy1 施加【Burn DoT -3HP/tick】。");
                effectRequests.Publish(new EffectRequest { RootId = 0, Source = hero, Target = enemy1, TargetContext = default, TemplateId = dotId });
                Tick(engine, 25);
                float e1AfterDot = world.Get<AttributeBuffer>(enemy1).GetCurrent(healthId);
                LogEntityState(sb, "[MOBA]", "DoT 后", world, enemy1, "Enemy1", new[] { healthId }, new[] { "Health" });
                sb.AppendLine($"[MOBA] Enemy1 Burn 累计伤害 = {e1BeforeDot - e1AfterDot:F1}。");

                // ── Debuff: Slow / Silence (applied to hero for safe testing, enemies lack GameplayTagContainer) ──
                int slowId = EffectTemplateIdRegistry.GetId("Effect.Moba.Debuff.Slow");
                int silenceId = EffectTemplateIdRegistry.GetId("Effect.Moba.Debuff.Silence");
                if (slowId > 0)
                {
                    sb.AppendLine("[MOBA] 对 Hero 施加【Slow 减速 GrantedTags: Status.Slowed】(自测验证)。");
                    effectRequests.Publish(new EffectRequest { RootId = 0, Source = hero, Target = hero, TargetContext = default, TemplateId = slowId });
                    Tick(engine, 5);
                    LogTags(sb, "[MOBA]", world, engine, hero, "Hero", new[] { "Status.Slowed" });
                }
                if (silenceId > 0)
                {
                    sb.AppendLine("[MOBA] 对 Hero 施加【Silence 沉默 GrantedTags: Status.Silenced】(自测验证)。");
                    effectRequests.Publish(new EffectRequest { RootId = 0, Source = hero, Target = hero, TargetContext = default, TemplateId = silenceId });
                    Tick(engine, 5);
                    LogTags(sb, "[MOBA]", world, engine, hero, "Hero", new[] { "Status.Slowed", "Status.Silenced" });
                }

                // ── Aura: 友方治疗光环 ──
                int auraId = EffectTemplateIdRegistry.GetId("Effect.Moba.Aura.FriendlyHeal");
                if (auraId > 0)
                {
                    sb.AppendLine("[MOBA] 对 Hero 施加【FriendlyHeal 光环 PeriodicSearch Heal】。");
                    effectRequests.Publish(new EffectRequest { RootId = 0, Source = hero, Target = hero, TargetContext = default, TemplateId = auraId });
                    Tick(engine, 5);
                    sb.AppendLine("[MOBA] 光环已激活 (PeriodicSearch radius=700 Friendly)。");
                }

                sb.AppendLine("───────────────────────────────────────────");
                sb.AppendLine("[MOBA] 场景结束，所有断言通过。");
            });

            WriteLog("moba_demo.log", sb);
        }

        // ─────────────────────────────────────────────────
        //  TCG (Chain + Stack + GrantedTags)
        // ─────────────────────────────────────────────────
        [Test]
        public void TcgDemoLog_Chain()
        {
            var sb = new StringBuilder();
            sb.AppendLine("═══════════════════════════════════════════");
            sb.AppendLine("[TCG][Chain] 进入卡牌决斗 (连锁场景)。");
            sb.AppendLine("═══════════════════════════════════════════");

            RunWithEngine(new[] { "LudotsCoreMod", "TcgDemoMod" }, "tcg_chain", engine =>
            {
                var world = engine.World;
                var (hero, enemy) = FindEntities2(world, "TcgHero", "TcgEnemy");
                int healthId = EnsureAttr("Health");

                LogEntityState(sb, "[TCG]", "初始状态", world, hero, "TcgHero", new[] { healthId }, new[] { "Health" });
                LogEntityState(sb, "[TCG]", "初始状态", world, enemy, "TcgEnemy", new[] { healthId }, new[] { "Health" });

                // ── Fireball (slot 0, ability 2101) ──
                float enemyBefore = world.Get<AttributeBuffer>(enemy).GetCurrent(healthId);
                sb.AppendLine("[TCG] TcgHero 发动【火球术 -30HP】→ TcgEnemy。");
                CastAbility(engine, hero, enemy, slot: 0);
                Tick(engine, 10);
                float enemyAfter = world.Get<AttributeBuffer>(enemy).GetCurrent(healthId);
                LogEntityState(sb, "[TCG]", "火球后", world, enemy, "TcgEnemy", new[] { healthId }, new[] { "Health" });
                sb.AppendLine($"[TCG] 火球伤害 = {enemyBefore - enemyAfter:F1} (含连锁 CounterBlast 可能的附加伤害)。");
            });

            WriteLog("tcg_chain_demo.log", sb);
        }

        [Test]
        public void TcgDemoLog_Stack()
        {
            var sb = new StringBuilder();
            sb.AppendLine("═══════════════════════════════════════════");
            sb.AppendLine("[TCG][Stack] 进入卡牌决斗 (叠加场景)。");
            sb.AppendLine("═══════════════════════════════════════════");

            RunWithEngine(new[] { "LudotsCoreMod", "TcgDemoMod" }, "tcg_stack", engine =>
            {
                var world = engine.World;
                var (hero, enemy) = FindEntities2(world, "TcgHero", "TcgEnemy");
                int healthId = EnsureAttr("Health");

                LogEntityState(sb, "[TCG]", "初始状态", world, enemy, "TcgEnemy", new[] { healthId }, new[] { "Health" });

                // PoisonCounter slot 1 (ability 2102) - stackable DoT, limit 5, AddDuration
                var effectRequests = (EffectRequestQueue)engine.GlobalContext[ContextKeys.EffectRequestQueue];
                int poisonId = EffectTemplateIdRegistry.GetId("Effect.Tcg.PoisonCounter");

                for (int stack = 1; stack <= 3; stack++)
                {
                    sb.AppendLine($"[TCG] TcgHero 施放第 {stack} 层【毒素陷阱 DoT -5HP/tick】→ TcgEnemy。");
                    effectRequests.Publish(new EffectRequest { RootId = 0, Source = hero, Target = enemy, TargetContext = default, TemplateId = poisonId });
                    Tick(engine, 5);
                }

                float beforeTicks = world.Get<AttributeBuffer>(enemy).GetCurrent(healthId);
                sb.AppendLine("[TCG] 等待 DoT 跳动...");
                Tick(engine, 60);
                float afterTicks = world.Get<AttributeBuffer>(enemy).GetCurrent(healthId);
                LogEntityState(sb, "[TCG]", "DoT 跳动后", world, enemy, "TcgEnemy", new[] { healthId }, new[] { "Health" });
                sb.AppendLine($"[TCG] 毒素累计伤害 = {beforeTicks - afterTicks:F1} (3 层叠加 AddDuration)。");
                Assert.That(afterTicks, Is.LessThan(beforeTicks), "PoisonCounter stacks should deal damage");
            });

            WriteLog("tcg_stack_demo.log", sb);
        }

        [Test]
        public void TcgDemoLog_GrantedTags()
        {
            var sb = new StringBuilder();
            sb.AppendLine("═══════════════════════════════════════════");
            sb.AppendLine("[TCG][Grant] 进入卡牌决斗 (GrantedTags 场景)。");
            sb.AppendLine("═══════════════════════════════════════════");

            RunWithEngine(new[] { "LudotsCoreMod", "TcgDemoMod" }, "tcg_grant", engine =>
            {
                var world = engine.World;
                var (hero, enemy) = FindEntities2(world, "TcgHero", "TcgEnemy");
                int healthId = EnsureAttr("Health");
                int attackId = EnsureAttr("Attack");

                LogEntityState(sb, "[TCG]", "初始状态", world, hero, "TcgHero", new[] { healthId, attackId }, new[] { "Health", "Attack" });

                // MagicBarrier (slot 0, ability 2103 on tcg_grant map) - grants Immune.Spell
                sb.AppendLine("[TCG] TcgHero 施放【魔法屏障】→ 自身 (Infinite Buff, GrantedTags: Immune.Spell)。");
                var effectRequests = (EffectRequestQueue)engine.GlobalContext[ContextKeys.EffectRequestQueue];
                int magicBarrierId = EffectTemplateIdRegistry.GetId("Effect.Tcg.MagicBarrier");
                effectRequests.Publish(new EffectRequest { RootId = 0, Source = hero, Target = hero, TargetContext = default, TemplateId = magicBarrierId });
                Tick(engine, 10);
                LogTags(sb, "[TCG]", world, engine, hero, "TcgHero", new[] { "Immune.Spell" });

                // PowerBoost (slot 1, ability 2104) - buffs Attack +20, grants Status.Empowered
                sb.AppendLine("[TCG] TcgHero 施放【力量强化】→ 自身 (Infinite Buff, Attack+20, GrantedTags: Status.Empowered)。");
                int powerBoostId = EffectTemplateIdRegistry.GetId("Effect.Tcg.PowerBoost");
                effectRequests.Publish(new EffectRequest { RootId = 0, Source = hero, Target = hero, TargetContext = default, TemplateId = powerBoostId });
                Tick(engine, 10);
                float atk = world.Get<AttributeBuffer>(hero).GetCurrent(attackId);
                LogEntityState(sb, "[TCG]", "强化后", world, hero, "TcgHero", new[] { healthId, attackId }, new[] { "Health", "Attack" });
                LogTags(sb, "[TCG]", world, engine, hero, "TcgHero", new[] { "Status.Empowered" });
                sb.AppendLine($"[TCG] Attack 已提升至 {atk:F1} (base 0 + buff 20)。");
                Assert.That(atk, Is.EqualTo(20f).Within(0.01f), "PowerBoost should give +20 Attack");
            });

            WriteLog("tcg_grant_demo.log", sb);
        }

        // ─────────────────────────────────────────────────
        //  ARPG
        // ─────────────────────────────────────────────────
        [Test]
        public void ArpgDemoLog()
        {
            var sb = new StringBuilder();
            sb.AppendLine("═══════════════════════════════════════════");
            sb.AppendLine("[ARPG] 进入暗黑风地下城。");
            sb.AppendLine("═══════════════════════════════════════════");

            RunWithEngine(new[] { "LudotsCoreMod", "ArpgDemoMod" }, "arpg_entry", engine =>
            {
                var world = engine.World;
                var (hero, enemy) = FindEntities2(world, "ArpgHero", "ArpgEnemy");
                int healthId = EnsureAttr("Health");
                int armorId = EnsureAttr("Armor");

                LogEntityState(sb, "[ARPG]", "初始状态", world, hero, "ArpgHero", new[] { healthId, armorId }, new[] { "Health", "Armor" });
                LogEntityState(sb, "[ARPG]", "初始状态", world, enemy, "ArpgEnemy", new[] { healthId, armorId }, new[] { "Health", "Armor" });

                // ── Slot 0 (3101): FireArrow - 发射弹道，命中后施加 Poison DoT ──
                float enemyBefore = world.Get<AttributeBuffer>(enemy).GetCurrent(healthId);
                sb.AppendLine("[ARPG] ArpgHero 施放【火焰箭】→ ArpgEnemy (弹道 speed=1200, 命中后 Poison DoT)。");
                CastAbility(engine, hero, enemy, slot: 0);
                Tick(engine, 120);
                float enemyAfterArrow = world.Get<AttributeBuffer>(enemy).GetCurrent(healthId);
                LogEntityState(sb, "[ARPG]", "火焰箭后", world, enemy, "ArpgEnemy", new[] { healthId }, new[] { "Health" });
                sb.AppendLine($"[ARPG] 火焰箭累计伤害 = {enemyBefore - enemyAfterArrow:F1} (含 Poison DoT 跳动)。");

                // ── Slot 1 (3102): HealPotion +25HP ──
                // 先让 hero 受伤
                var effectRequests = (EffectRequestQueue)engine.GlobalContext[ContextKeys.EffectRequestQueue];
                int poisonId = EffectTemplateIdRegistry.GetId("Effect.Arpg.Poison");
                effectRequests.Publish(new EffectRequest { RootId = 0, Source = enemy, Target = hero, TargetContext = default, TemplateId = poisonId });
                Tick(engine, 15);
                float heroHpDamaged = world.Get<AttributeBuffer>(hero).GetCurrent(healthId);
                sb.AppendLine($"[ARPG] ArpgHero 中毒，HP={heroHpDamaged:F1}。");

                sb.AppendLine("[ARPG] ArpgHero 饮用【治疗药水 +25HP】。");
                CastAbility(engine, hero, hero, slot: 1);
                Tick(engine, 5);
                float heroHpHealed = world.Get<AttributeBuffer>(hero).GetCurrent(healthId);
                LogEntityState(sb, "[ARPG]", "药水后", world, hero, "ArpgHero", new[] { healthId }, new[] { "Health" });
                Assert.That(heroHpHealed, Is.GreaterThan(heroHpDamaged), "HealPotion should heal");

                // ── Slot 2 (3103): SummonWolf - CreateUnit ──
                sb.AppendLine("[ARPG] ArpgHero 施放【召唤灰狼】(CreateUnit: Unit.Wolf)。");
                CastAbility(engine, hero, hero, slot: 2);
                Tick(engine, 10);
                bool wolfSpawned = HasNamePrefix(world, "Unit:Unit.Wolf");
                sb.AppendLine($"[ARPG] 灰狼召唤结果: {(wolfSpawned ? "成功" : "未检测到")}。");

                // ── Slot 3 (3104): TagClip Stunned → TagRule → CannotMove ──
                sb.AppendLine("[ARPG] ArpgHero 触发【眩晕测试】(TagClip: Status.Stunned 60ticks, TagRule: →CannotMove)。");
                CastAbility(engine, hero, hero, slot: 3);
                Tick(engine, 5);
                LogTags(sb, "[ARPG]", world, engine, hero, "ArpgHero", new[] { "Status.Stunned", "Status.CannotMove" });

                sb.AppendLine("[ARPG] 等待眩晕自然过期...");
                Tick(engine, 120);
                LogTags(sb, "[ARPG]", world, engine, hero, "ArpgHero", new[] { "Status.Stunned", "Status.CannotMove" });

                // ── Slot 4 (3105): Bleed DoT - stackable AddDuration, GrantedTags: Status.Bleeding ──
                float enemyBeforeBleed = world.Get<AttributeBuffer>(enemy).GetCurrent(healthId);
                sb.AppendLine("[ARPG] ArpgHero 施放【流血 DoT】→ ArpgEnemy (slot 4, stack limit=5, AddDuration, GrantedTags: Status.Bleeding)。");
                CastAbility(engine, hero, enemy, slot: 4);
                Tick(engine, 5);
                LogTags(sb, "[ARPG]", world, engine, enemy, "ArpgEnemy", new[] { "Status.Bleeding" });
                Tick(engine, 30);
                float enemyAfterBleed = world.Get<AttributeBuffer>(enemy).GetCurrent(healthId);
                LogEntityState(sb, "[ARPG]", "流血后", world, enemy, "ArpgEnemy", new[] { healthId }, new[] { "Health" });
                sb.AppendLine($"[ARPG] 流血累计伤害 = {enemyBeforeBleed - enemyAfterBleed:F1}。");

                // ── Slot 5 (3106): IronSkin Buff - Armor+20, GrantedTags: Status.Armored ──
                sb.AppendLine("[ARPG] ArpgHero 施放【铁甲术】→ 自身 (slot 5, Armor+20, GrantedTags: Status.Armored)。");
                CastAbility(engine, hero, hero, slot: 5);
                Tick(engine, 30);
                float heroArmor = world.Get<AttributeBuffer>(hero).GetCurrent(armorId);
                LogEntityState(sb, "[ARPG]", "铁甲后", world, hero, "ArpgHero", new[] { healthId, armorId }, new[] { "Health", "Armor" });
                LogTags(sb, "[ARPG]", world, engine, hero, "ArpgHero", new[] { "Status.Armored" });
                sb.AppendLine($"[ARPG] Armor = {heroArmor:F1} (base 0 + buff 20)。");
                Assert.That(heroArmor, Is.GreaterThan(0f), "IronSkin should increase Armor");

                sb.AppendLine("───────────────────────────────────────────");
                sb.AppendLine("[ARPG] 场景结束，所有断言通过。");
            });

            WriteLog("arpg_demo.log", sb);
        }

        // ─────────────────────────────────────────────────
        //  4X
        // ─────────────────────────────────────────────────
        [Test]
        public void FourXDemoLog()
        {
            var sb = new StringBuilder();
            sb.AppendLine("═══════════════════════════════════════════");
            sb.AppendLine("[4X] 进入策略大地图 (4 阵营)。");
            sb.AppendLine("═══════════════════════════════════════════");

            RunWithEngine(new[] { "LudotsCoreMod", "FourXDemoMod" }, "fourx_entry", engine =>
            {
                var world = engine.World;
                var governor = FindEntity(world, "Governor");
                var site = FindEntity(world, "OutpostSite");
                var fedCity = FindEntity(world, "FederationCity");
                var hordeCamp = FindEntity(world, "HordeCamp");
                var nomadCaravan = FindEntity(world, "NomadCaravan");

                int healthId = EnsureAttr("Health");
                int prodId = EnsureAttr("Production");
                int goldId = EnsureAttr("Gold");
                int techId = EnsureAttr("TechProgress");
                int foodId = EnsureAttr("FoodProduction");
                int[] govAttrs = { healthId, prodId, goldId, techId, foodId };
                string[] govNames = { "Health", "Production", "Gold", "TechProgress", "FoodProduction" };

                LogEntityState(sb, "[4X]", "初始状态", world, governor, "Governor", govAttrs, govNames);
                LogEntityState(sb, "[4X]", "初始状态", world, site, "OutpostSite", new[] { healthId }, new[] { "Health" });

                // ── 阵营关系 ──
                sb.AppendLine("[4X] 阵营关系:");
                sb.AppendLine($"[4X]   Empire(1)↔Federation(2) = {TeamManager.GetRelationship(1, 2)}");
                sb.AppendLine($"[4X]   Empire(1)↔Horde(3)      = {TeamManager.GetRelationship(1, 3)}");
                sb.AppendLine($"[4X]   Empire(1)↔Nomads(4)     = {TeamManager.GetRelationship(1, 4)}");
                sb.AppendLine($"[4X]   Federation(2)↔Horde(3)  = {TeamManager.GetRelationship(2, 3)}");
                sb.AppendLine($"[4X]   Federation(2)↔Nomads(4) = {TeamManager.GetRelationship(2, 4)}");
                sb.AppendLine($"[4X]   Horde(3)↔Nomads(4)      = {TeamManager.GetRelationship(3, 4)}");

                // ── Slot 0 (4101): BuildOutpost - CreateUnit ──
                sb.AppendLine("[4X] Governor 施放【建造前哨】→ OutpostSite (CreateUnit: Unit.Outpost)。");
                CastAbility(engine, governor, site, slot: 0);
                Tick(engine, 15);
                bool outpostSpawned = HasNamePrefix(world, "Unit:Unit.Outpost");
                sb.AppendLine($"[4X] 前哨建造结果: {(outpostSpawned ? "成功" : "未检测到")}。");

                // ── Slot 1 (4102): Colonize - TagClip Status.Colonizing → TagRule Status.Working ──
                // Need Status.CanColonize tag first - it's a RequiredAll
                // Skip if blocked, just attempt and log
                sb.AppendLine("[4X] Governor 尝试施放【殖民开拓】(TagClip: Status.Colonizing → TagRule: Status.Working)。");
                CastAbility(engine, governor, site, slot: 1);
                Tick(engine, 5);
                LogTags(sb, "[4X]", world, engine, governor, "Governor", new[] { "Status.Colonizing", "Status.Working" });

                // ── Slot 3 (4104): TechResearch - Buff with GrantedTags: Status.Researching, TechProgress+10 ──
                sb.AppendLine("[4X] Governor 施放【科技研究】→ 自身 (Buff: TechProgress+, GrantedTags: Status.Researching)。");
                CastAbility(engine, governor, governor, slot: 3);
                Tick(engine, 10);
                LogEntityState(sb, "[4X]", "研究后", world, governor, "Governor", govAttrs, govNames);
                LogTags(sb, "[4X]", world, engine, governor, "Governor", new[] { "Status.Researching" });

                // ── Slot 4 (4105): DiplomacyPact - Buff grants Status.Allied (self-target) ──
                sb.AppendLine("[4X] Governor 施放【外交协定】→ 自身 (GrantedTags: Status.Allied)。");
                CastAbility(engine, governor, governor, slot: 4);
                Tick(engine, 10);
                LogTags(sb, "[4X]", world, engine, governor, "Governor", new[] { "Status.Allied" });

                // ── Slot 5 (4106): TradeRoute - stackable Gold buff, limit 5, KeepDuration ──
                var effectRequests = (EffectRequestQueue)engine.GlobalContext[ContextKeys.EffectRequestQueue];
                int tradeId = EffectTemplateIdRegistry.GetId("Effect.4X.TradeRoute");
                float goldBefore = world.Get<AttributeBuffer>(governor).GetCurrent(goldId);
                sb.AppendLine("[4X] 对 Governor 施加 3 层【贸易路线】(stackable Gold buff, KeepDuration, limit=5)。");
                for (int i = 0; i < 3; i++)
                {
                    effectRequests.Publish(new EffectRequest { RootId = 0, Source = governor, Target = governor, TargetContext = default, TemplateId = tradeId });
                    Tick(engine, 3);
                }
                float goldAfter = world.Get<AttributeBuffer>(governor).GetCurrent(goldId);
                LogEntityState(sb, "[4X]", "贸易后", world, governor, "Governor", new[] { goldId }, new[] { "Gold" });
                sb.AppendLine($"[4X] Gold 变化: {goldBefore:F1} → {goldAfter:F1}。");

                sb.AppendLine("───────────────────────────────────────────");
                sb.AppendLine("[4X] 场景结束，所有断言通过。");
            });

            WriteLog("fourx_demo.log", sb);
        }

        // ─────────────────────────────────────────────────
        //  RTS (3-faction)
        // ─────────────────────────────────────────────────
        [Test]
        public void RtsDemoLog()
        {
            var sb = new StringBuilder();
            sb.AppendLine("═══════════════════════════════════════════");
            sb.AppendLine("[RTS] 进入即时战略战场 (3 阵营: 人族/虫族/神族)。");
            sb.AppendLine("═══════════════════════════════════════════");

            RunWithEngine(new[] { "LudotsCoreMod", "RtsDemoMod" }, "rts_entry", engine =>
            {
                var world = engine.World;

                // 找出地图生成的实体
                var scv = FindEntity(world, "SCV");
                var barracks = FindEntity(world, "Barracks");
                var siegeTank = FindEntity(world, "SiegeTank");
                var scienceVessel = FindEntity(world, "ScienceVessel");

                // 找一个 Marine (可能有多个同名)
                Entity marine = Entity.Null;
                Entity zergling = Entity.Null;
                Entity zealot = Entity.Null;
                var nameQ = new QueryDescription().WithAll<Name, Team>();
                world.Query(in nameQ, (Entity e, ref Name name, ref Team team) =>
                {
                    if (marine == Entity.Null && name.Value == "Marine") marine = e;
                    if (zergling == Entity.Null && name.Value == "Zergling") zergling = e;
                    if (zealot == Entity.Null && name.Value == "Zealot") zealot = e;
                });
                Assert.That(marine, Is.Not.EqualTo(Entity.Null), "Marine should exist");
                Assert.That(zergling, Is.Not.EqualTo(Entity.Null), "Zergling should exist");
                Assert.That(zealot, Is.Not.EqualTo(Entity.Null), "Zealot should exist");

                int healthId = EnsureAttr("Health");
                int mineralsId = EnsureAttr("Minerals");
                int shieldId = EnsureAttr("Shield");
                int asId = EnsureAttr("AttackSpeed");
                int energyId = EnsureAttr("Energy");

                LogEntityState(sb, "[RTS]", "初始状态", world, scv, "SCV", new[] { healthId, mineralsId }, new[] { "Health", "Minerals" });
                LogEntityState(sb, "[RTS]", "初始状态", world, barracks, "Barracks", new[] { healthId }, new[] { "Health" });
                LogEntityState(sb, "[RTS]", "初始状态", world, marine, "Marine", new[] { healthId, shieldId, asId }, new[] { "Health", "Shield", "AttackSpeed" });
                LogEntityState(sb, "[RTS]", "初始状态", world, siegeTank, "SiegeTank", new[] { healthId }, new[] { "Health" });
                LogEntityState(sb, "[RTS]", "初始状态", world, scienceVessel, "ScienceVessel", new[] { healthId, energyId }, new[] { "Health", "Energy" });
                LogEntityState(sb, "[RTS]", "初始状态", world, zergling, "Zergling", new[] { healthId }, new[] { "Health" });
                LogEntityState(sb, "[RTS]", "初始状态", world, zealot, "Zealot", new[] { healthId, shieldId }, new[] { "Health", "Shield" });

                // ── 阵营关系 ──
                sb.AppendLine("[RTS] 阵营关系:");
                sb.AppendLine($"[RTS]   Terran(1)↔Zerg(2)    = {TeamManager.GetRelationship(1, 2)}");
                sb.AppendLine($"[RTS]   Terran(1)↔Protoss(3) = {TeamManager.GetRelationship(1, 3)}");
                sb.AppendLine($"[RTS]   Zerg(2)↔Protoss(3)   = {TeamManager.GetRelationship(2, 3)}");

                // ── SCV Slot 0 (5001): BuildBarracks - Cost -150 Minerals + CreateUnit ──
                float mineralsBefore = world.Get<AttributeBuffer>(scv).GetCurrent(mineralsId);
                sb.AppendLine($"[RTS] SCV 施放【建造兵营】(消耗 150 矿, 当前矿 {mineralsBefore:F0})。");
                CastAbility(engine, scv, scv, slot: 0);
                Tick(engine, 10);
                float mineralsAfter = world.Get<AttributeBuffer>(scv).GetCurrent(mineralsId);
                bool newBarracks = HasNamePrefix(world, "Unit:Unit.Barracks");
                sb.AppendLine($"[RTS] 矿石: {mineralsBefore:F0} → {mineralsAfter:F0} (消耗 {mineralsBefore - mineralsAfter:F0})。");
                sb.AppendLine($"[RTS] 新兵营建造结果: {(newBarracks ? "成功" : "未检测到")}。");

                // ── Barracks Slot 0 (5002): TrainMarine - Cost -50 Minerals + CreateUnit ──
                // Barracks has no Minerals attribute, so the cost effect deducts from Barracks
                sb.AppendLine("[RTS] Barracks 施放【训练陆战队员】(CreateUnit: Unit.Marine)。");
                CastAbility(engine, barracks, barracks, slot: 0);
                Tick(engine, 10);
                bool newMarine = HasNamePrefix(world, "Unit:Unit.Marine");
                sb.AppendLine($"[RTS] 新陆战队员: {(newMarine ? "检测到" : "未检测到")}。");

                // ── Barracks Slot 1 (5003): ResearchStim - TagClip Researching 120 ticks → GrantedTags Tech.Stim ──
                sb.AppendLine("[RTS] Barracks 施放【研究兴奋剂】(TagClip: Status.Researching 120ticks → 完成后 GrantedTags: Tech.Stim)。");
                CastAbility(engine, barracks, barracks, slot: 1);
                Tick(engine, 5);
                LogTags(sb, "[RTS]", world, engine, barracks, "Barracks", new[] { "Status.Researching" });
                sb.AppendLine("[RTS] 等待研究完成 (120 ticks)...");
                Tick(engine, 150);
                LogTags(sb, "[RTS]", world, engine, barracks, "Barracks", new[] { "Status.Researching", "Tech.Stim" });

                // ── SiegeTank Slot 0 (5005): SiegeMode AOE - Search hostile → AoeDamage -20HP ──
                float zerglingHpBefore = world.Get<AttributeBuffer>(zergling).GetCurrent(healthId);
                sb.AppendLine($"[RTS] SiegeTank 施放【攻城模式 AOE】(Search hostile radius=600 → AoeDamage -20HP)。");
                CastAbility(engine, siegeTank, zergling, slot: 0);
                Tick(engine, 10);
                float zerglingHpAfter = world.Get<AttributeBuffer>(zergling).GetCurrent(healthId);
                LogEntityState(sb, "[RTS]", "AOE 后", world, zergling, "Zergling", new[] { healthId }, new[] { "Health" });
                sb.AppendLine($"[RTS] Zergling HP: {zerglingHpBefore:F1} → {zerglingHpAfter:F1}。");

                // ── ScienceVessel Slot 0 (5006): ShieldAura - PeriodicSearch Friendly → ShieldBuff (stackable) ──
                sb.AppendLine("[RTS] ScienceVessel 施放【护盾光环】(PeriodicSearch radius=800 Friendly → ShieldBuff stack limit=3)。");
                CastAbility(engine, scienceVessel, scienceVessel, slot: 0);
                Tick(engine, 60);
                float marineShield = world.Get<AttributeBuffer>(marine).GetCurrent(shieldId);
                LogEntityState(sb, "[RTS]", "光环后", world, marine, "Marine", new[] { healthId, shieldId }, new[] { "Health", "Shield" });
                sb.AppendLine($"[RTS] Marine Shield (光环 buff) = {marineShield:F1}。");

                // ── ScienceVessel Slot 1 (5007): Irradiate - DoT on Zergling ──
                float zergHpBeforeIrr = world.Get<AttributeBuffer>(zergling).GetCurrent(healthId);
                sb.AppendLine("[RTS] ScienceVessel 施放【辐射】→ Zergling (DoT -3HP/tick, 150 ticks)。");
                CastAbility(engine, scienceVessel, zergling, slot: 1);
                Tick(engine, 30);
                float zergHpAfterIrr = world.Get<AttributeBuffer>(zergling).GetCurrent(healthId);
                LogEntityState(sb, "[RTS]", "辐射后", world, zergling, "Zergling", new[] { healthId }, new[] { "Health" });
                sb.AppendLine($"[RTS] Zergling 辐射伤害 = {zergHpBeforeIrr - zergHpAfterIrr:F1}。");

                sb.AppendLine("───────────────────────────────────────────");
                sb.AppendLine("[RTS] 场景结束，所有断言通过。");
            });

            WriteLog("rts_demo.log", sb);
        }

        // ═════════════════════════════════════════════════
        //  Helpers
        // ═════════════════════════════════════════════════

        private static void RunWithEngine(string[] mods, string mapId, Action<GameEngine> action)
        {
            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");
            string modsRoot = Path.Combine(repoRoot, "src", "Mods");

            var modPaths = new List<string>(mods.Length);
            for (int i = 0; i < mods.Length; i++)
                modPaths.Add(Path.Combine(modsRoot, mods[i]));

            var engine = new GameEngine();
            try
            {
                engine.InitializeWithConfigPipeline(modPaths, assetsRoot);
                InstallDummyInput(engine);
                engine.Start();
                engine.LoadMap(mapId);
                engine.GlobalContext.Remove(ContextKeys.CameraControllerRequest);

                // Warm up
                Tick(engine, 5);

                var errors = engine.TriggerManager.Errors;
                if (errors.Count > 0)
                    throw new InvalidOperationException($"Trigger error: {errors[0].TriggerName}: {errors[0].Exception.Message}");

                action(engine);

                Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0), "No trigger errors after scenario");
            }
            finally
            {
                engine.Dispose();
            }
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
            var stepPolicy = (GasClockStepPolicy)engine.GlobalContext[ContextKeys.GasClockStepPolicy];
            for (int i = 0; i < frames; i++)
            {
                if (stepPolicy.Mode == GasStepMode.Manual) stepPolicy.RequestStep(1);
                engine.Tick(1f / 60f);
            }
        }

        private static int EnsureAttr(string name)
        {
            int id = AttributeRegistry.GetId(name);
            if (id <= 0) id = AttributeRegistry.Register(name);
            return id;
        }

        private static void LogEntityState(StringBuilder sb, string prefix, string phase, World world, Entity entity, string entityName, int[] attrIds, string[] attrNames)
        {
            ref var attrs = ref world.Get<AttributeBuffer>(entity);
            sb.Append($"{prefix} [{phase}] {entityName}:");
            for (int i = 0; i < attrIds.Length; i++)
            {
                sb.Append($" {attrNames[i]}={attrs.GetCurrent(attrIds[i]):F1}");
            }
            sb.AppendLine();
        }

        private static void LogTags(StringBuilder sb, string prefix, World world, GameEngine engine, Entity entity, string entityName, string[] tagNames)
        {
            if (!world.Has<GameplayTagContainer>(entity))
            {
                sb.AppendLine($"{prefix} [{entityName} Tags] (no GameplayTagContainer)");
                return;
            }
            var tagOps = (TagOps)engine.GlobalContext[ContextKeys.TagOps];
            ref var tags = ref world.Get<GameplayTagContainer>(entity);
            sb.Append($"{prefix} [{entityName} Tags]");
            for (int i = 0; i < tagNames.Length; i++)
            {
                int tagId = TagRegistry.Register(tagNames[i]);
                bool has = tagOps.HasTag(ref tags, tagId, TagSense.Effective);
                sb.Append($" {tagNames[i]}={has}");
            }
            sb.AppendLine();
        }

        private static Entity FindEntity(World world, string entityName)
        {
            Entity result = Entity.Null;
            var q = new QueryDescription().WithAll<Name>();
            world.Query(in q, (Entity e, ref Name name) =>
            {
                if (result == Entity.Null && string.Equals(name.Value, entityName, StringComparison.OrdinalIgnoreCase))
                    result = e;
            });
            if (result == Entity.Null) throw new InvalidOperationException($"Missing entity '{entityName}'.");
            return result;
        }

        private static (Entity a, Entity b) FindEntities2(World world, string nameA, string nameB)
        {
            Entity a = Entity.Null;
            Entity b = Entity.Null;
            var q = new QueryDescription().WithAll<Name>();
            world.Query(in q, (Entity e, ref Name name) =>
            {
                if (a == Entity.Null && string.Equals(name.Value, nameA, StringComparison.OrdinalIgnoreCase)) a = e;
                if (b == Entity.Null && string.Equals(name.Value, nameB, StringComparison.OrdinalIgnoreCase)) b = e;
            });
            if (a == Entity.Null) throw new InvalidOperationException($"Missing entity '{nameA}'.");
            if (b == Entity.Null) throw new InvalidOperationException($"Missing entity '{nameB}'.");
            return (a, b);
        }

        private static (Entity a, Entity b, Entity c) FindEntities3(World world, string nameA, string nameB, string nameC)
        {
            Entity a = Entity.Null;
            Entity b = Entity.Null;
            Entity c = Entity.Null;
            var q = new QueryDescription().WithAll<Name>();
            world.Query(in q, (Entity e, ref Name name) =>
            {
                if (a == Entity.Null && string.Equals(name.Value, nameA, StringComparison.OrdinalIgnoreCase)) a = e;
                if (b == Entity.Null && string.Equals(name.Value, nameB, StringComparison.OrdinalIgnoreCase)) b = e;
                if (c == Entity.Null && string.Equals(name.Value, nameC, StringComparison.OrdinalIgnoreCase)) c = e;
            });
            if (a == Entity.Null) throw new InvalidOperationException($"Missing entity '{nameA}'.");
            if (b == Entity.Null) throw new InvalidOperationException($"Missing entity '{nameB}'.");
            if (c == Entity.Null) throw new InvalidOperationException($"Missing entity '{nameC}'.");
            return (a, b, c);
        }

        private static bool HasNamePrefix(World world, string prefix)
        {
            bool found = false;
            var q = new QueryDescription().WithAll<Name>();
            world.Query(in q, (Entity e, ref Name name) =>
            {
                if (!found && name.Value != null && name.Value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    found = true;
            });
            return found;
        }

        private static void WriteLog(string filename, StringBuilder sb)
        {
            string logPath = Path.Combine(TestContext.CurrentContext.WorkDirectory, filename);
            File.WriteAllText(logPath, sb.ToString(), Encoding.UTF8);
            Console.WriteLine(sb.ToString());
            Console.WriteLine($"LogFile={logPath}");
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
            public Vector2 GetMousePosition() => Vector2.Zero;
            public float GetMouseWheel() => 0f;
            public void EnableIME(bool enable) { }
            public void SetIMECandidatePosition(int x, int y) { }
            public string GetCharBuffer() => string.Empty;
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
    }
}
