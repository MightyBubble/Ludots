// D-4' 战斗解算 GAS 化:解算语义步骤(mod builtin 阶段 handler,Layer 2 Mod 行为)。
// 语义 SSOT = 内核预言机(SkillInstance.Action / Troop.CalculateSkillDamage×5 /
// Troop.ChangeTroops / Troop.ChangeMorale / Troop.OnDestroy / BuildingBase 攻城面),
// 语句序、事件位、随机流次序逐位保持(对拍铁律);技能数值位来自 effect 模板
// configParams(assets/GAS/effects.json,Skills.json SSOT 派生,测试断言相等),
// 内核对象(攻击/目标部队、城、技能实例)经施放环境(SangoCombatCastScope)进入。
//
// 保留面调用(CALL,非转写;与内核路径完全同一调用 = 同一副作用与掷点位):
//   SkillInstance.GetAttackCells(纯格子枚举)、Troop.GainEP/GainTargetResource(武将/势力域)、
//   Troop.ChangeFood/ChangeGold(纯写原语)、Troop.Clear(解散共用路径)、Troop.UpdateCell(位移)、
//   City.ChangeTroops/BuildingBase.ChangeDurability(城域原语,无掷点;零耐久时 ChangeDurability
//   内部落 OnFall)、City.OnFall(城陷链,城域保留面)、SkillEffect.Action(技能效果,内核掷点)。
// 转写面(TRANSCRIBE,语义搬到 GAS 正式面):
//   五条伤害公式、反击系数、ChangeTroops 兵力段(事件/伤兵/掷粮/生死)、ChangeMorale、
//   OnDestroy 俘获掷点、DoOffset 击退(含碰撞伤害与随机位移)、DoEffect 编排、能量扣减、
//   攻城两段编排(守军杀伤→城陷 / 耐久破坏→城池反击)。
// 数值写入:部队兵力/士气/携粮走 GAS 属性正式通道(AttributeMutationOps.SetBase,
// sango.troop.*)+ 内核 PONO write-through;城耐久/城兵力走城域既有通道(sango.city.durability
// 属性 + PONO)。表现位(Render.ShowInfo/UpdateRender)按内核同条件保留调用(headless 无副作用)。

using System;
using System.Collections.Generic;
using System.Reflection;
using Sango.Core;
using Sango.Core.Tools;
using Arch.Core;
using Sango.Render;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;

namespace Sango.Runtime
{
    /// <summary>当前施放环境(线程静态):战斗运行时在激活+排空前压栈,handler 内取用。</summary>
    public sealed class SangoCombatCastScope : IDisposable
    {
        public required SangoCombatNativeRuntime Runtime;
        public required Troop Caster;
        public SkillInstance? Skill;
        public required Cell SpellCell;
        public required int CriticalFactor;

        [ThreadStatic]
        internal static SangoCombatCastScope? Current;

        public SangoCombatCastScope()
        {
            _previous = Current;
            Current = this;
        }

        readonly SangoCombatCastScope? _previous;

        public void Dispose() => Current = _previous;
    }

    /// <summary>
    /// 战斗解算步骤:mod builtin 阶段 handler(注册键 SangoSimMod.SangoSkillAction /
    /// SangoSimMod.SangoMoraleChange)。组合形态见 artifacts/gas-composition-gate.md:
    /// 28+2 技能/演出模板(configParams 数据位)引用同一 preset → 本 handler;变体 = 模板数据行。
    /// </summary>
    public static class SangoCombatSteps
    {
        public const string SkillActionHandlerKey = "SangoSimMod.SangoSkillAction";
        public const string MoraleChangeHandlerKey = "SangoSimMod.SangoMoraleChange";

        public static void RegisterHandlers(BuiltinHandlerRegistry registry)
        {
            registry.Register(SkillActionHandlerKey, SkillActionHandler,
                EffectOperationMetadata.GasTransactional(SkillActionHandlerKey));
            registry.Register(MoraleChangeHandlerKey, MoraleChangeHandler,
                EffectOperationMetadata.GasTransactional(MoraleChangeHandlerKey));
        }

        // configParams 键位(_ep.* 命名,ConfigKeyRegistry 自动分配)。
        const string KeySkillId = "_ep.sangoSkillId";
        const string KeyAtk = "_ep.atk";
        const string KeyAtkDurability = "_ep.atkDurability";
        const string KeyCostEnergy = "_ep.costEnergy";
        const string KeyCanDamageTroop = "_ep.canDamageTroop";
        const string KeyCanDamageBuilding = "_ep.canDamageBuilding";
        const string KeyCanDamageTeam = "_ep.canDamageTeam";
        const string KeyCanSpellToCell = "_ep.canSpellToCell";
        const string KeyBlockFactor = "_ep.blockFactor";
        const string KeyAtkOffsetCount = "_ep.atkOffsetCount";
        const string KeyOffsetCount = "_ep.offsetCount";
        const string KeyMoraleDelta = "_ep.moraleDelta";

        static int IntParam(in EffectConfigParams parameters, string key)
        {
            int keyId = Ludots.Core.Gameplay.GAS.Registry.ConfigKeyRegistry.Register(key);
            return parameters.TryGetInt(keyId, out int value) ? value : 0;
        }

        static int[] ArrayParam(in EffectConfigParams parameters, string countKey, string elementKeyFormat)
        {
            int count = IntParam(parameters, countKey);
            if (count <= 0)
            {
                return Array.Empty<int>();
            }

            var values = new int[count];
            for (int i = 0; i < count; i++)
            {
                values[i] = IntParam(parameters, string.Format(null, elementKeyFormat, i));
            }

            return values;
        }

        /// <summary>技能解算主 handler(内核 SkillInstance.Action 的 GAS 面)。</summary>
        public static void SkillActionHandler(
            World world,
            Entity effectEntity,
            ref EffectContext context,
            in EffectConfigParams mergedParams,
            in EffectTemplateData templateData)
        {
            SangoCombatCastScope? scope = SangoCombatCastScope.Current
                ?? throw new InvalidOperationException(
                    "SangoSimMod.SangoSkillAction fired outside a sango combat cast scope; the cast runtime must bracket activation.");
            _ = world;
            _ = effectEntity;
            _ = context;
            _ = templateData;

            ResolveSkillAction(
                scope.Runtime,
                scope.Caster,
                scope.Skill ?? throw new InvalidOperationException(
                    "SangoSimMod.SangoSkillAction fired without a skill in the cast scope."),
                scope.SpellCell,
                scope.CriticalFactor,
                IntParam(mergedParams, KeyAtk),
                IntParam(mergedParams, KeyAtkDurability),
                IntParam(mergedParams, KeyCostEnergy),
                IntParam(mergedParams, KeyCanDamageTroop) != 0,
                IntParam(mergedParams, KeyCanDamageBuilding) != 0,
                IntParam(mergedParams, KeyCanDamageTeam) != 0,
                IntParam(mergedParams, KeyCanSpellToCell) != 0,
                IntParam(mergedParams, KeyBlockFactor),
                ArrayParam(mergedParams, KeyOffsetCount, "_ep.offset.{0}"));
        }

        /// <summary>士气变化 handler(演出回写面:舌战 ±10 等)。</summary>
        public static void MoraleChangeHandler(
            World world,
            Entity effectEntity,
            ref EffectContext context,
            in EffectConfigParams mergedParams,
            in EffectTemplateData templateData)
        {
            SangoCombatCastScope? scope = SangoCombatCastScope.Current
                ?? throw new InvalidOperationException(
                    "SangoSimMod.SangoMoraleChange fired outside a sango combat cast scope; the cast runtime must bracket activation.");
            _ = world;
            _ = effectEntity;
            _ = context;
            _ = templateData;

            ApplyMoraleChange(scope.Runtime, scope.Caster, IntParam(mergedParams, KeyMoraleDelta), showInfo: true);
        }

        // ---- 解算主体(SkillInstance.Action 语句序转写) ----

        static void ResolveSkillAction(
            SangoCombatNativeRuntime runtime,
            Troop troop,
            SkillInstance skill,
            Cell spellCell,
            int criticalFactor,
            int atk,
            int atkDurability,
            int costEnergy,
            bool canDamageTroop,
            bool canDamageBuilding,
            bool canDamageTeam,
            bool canSpellToCell,
            int blockFactor,
            int[] offsetAction)
        {
            Scenario scenario = Scenario.Cur!;
            Troop? targetTroop = spellCell.troop;
            BuildingBase? targetBuilding = spellCell.building;

            var activedTargetList = new List<SangoObject>();
            var tempCellList = new List<Cell>();
            skill.GetAttackCells(troop, spellCell, tempCellList);
            int targetDamage = 0;
            for (int i = 0; i < tempCellList.Count; i++)
            {
                Cell atkCell = tempCellList[i];
                Troop? beAtkTroop = atkCell.troop;

                if (atk > 0 && beAtkTroop != null && canDamageTroop && (troop.IsEnemy(beAtkTroop) || canDamageTeam))
                {
                    int damage = CalculateDamageTroopVsTroop(troop, beAtkTroop, atk) * criticalFactor / 100;
                    if (damage < 0)
                        damage = 0;

                    // 内核此处取 .Value 不回收,且 OnSkillDamageTroopAfter 复用同一 OverrideData(原样保持)。
                    OverrideData<int> damageOverride = OverrideData<int>.Create(damage);
                    GameEvent.OnSkillDamageTroop?.Invoke(skill, beAtkTroop, damageOverride);
                    damage = damageOverride.Value;

                    ApplyTroopChange(runtime, beAtkTroop, -damage, skill, 0);
                    int ep = Math.Max(1, damage / 10);
                    if (!beAtkTroop.IsAlive)
                    {
                        ep += 200;
                        troop.GainTargetResource(beAtkTroop);
                    }

                    troop.GainEP(ep);

                    // 反击
                    if (beAtkTroop.IsAlive && targetTroop == beAtkTroop && !beAtkTroop.HasControlBuff())
                    {
                        targetDamage = damage;
                        int hitBack = beAtkTroop.GetAttackBackFactor(skill, scenario.Map.Distance(troop.cell, spellCell));
                        OverrideData<int> hitBackOverride = OverrideData<int>.Create(hitBack);
                        GameEvent.OnTroopCalculateAttackBack?.Invoke(troop, beAtkTroop, skill, scenario, hitBackOverride);
                        hitBack = hitBackOverride.ValueAndRecycle;

                        if (hitBack > 0)
                        {
                            if (skill.IsRange())
                            {
                                // 内核远程反击分支:击毙攻方不发 GainTargetResource(原样保持)。
                                SkillInstance counterSkill = beAtkTroop.NormalRangeSkill;
                                int hitBackDmg = hitBack * CalculateDamageTroopVsTroop(beAtkTroop, troop, counterSkill.atk) / 100;
                                ApplyTroopChange(runtime, troop, -hitBackDmg, counterSkill, hitBack);
                                ep = Math.Max(1, damage / 10);
                                if (!troop.IsAlive) ep += 200;
                                beAtkTroop.GainEP(ep);
                            }
                            else
                            {
                                SkillInstance counterSkill = beAtkTroop.NormalSkill;
                                int hitBackDmg = hitBack * CalculateDamageTroopVsTroop(beAtkTroop, troop, counterSkill.atk) / 100;
                                ApplyTroopChange(runtime, troop, -hitBackDmg, counterSkill, hitBack);
                                ep = Math.Max(1, damage / 10);
                                if (!troop.IsAlive)
                                {
                                    ep += 200;
                                    beAtkTroop.GainTargetResource(troop);
                                }
                                beAtkTroop.GainEP(ep);
                            }
                        }
                    }
                    if (troop.IsAlive && beAtkTroop.IsAlive)
                        GameEvent.OnSkillDamageTroopAfter?.Invoke(skill, beAtkTroop, damageOverride);
                }

                BuildingBase beAtkBuildingBase = atkCell.building!;
                if (atkDurability > 0 && beAtkBuildingBase != null && canDamageBuilding && (troop.IsEnemy(beAtkBuildingBase) || canDamageTeam))
                {
                    // 一个目标只会收到一次伤害
                    if (activedTargetList.Contains(beAtkBuildingBase))
                        continue;
                    activedTargetList.Add(beAtkBuildingBase);
                    if (beAtkBuildingBase is City city)
                    {
                        int damageTroops = CalculateDamageTroopOnCity(troop, city, atk) * criticalFactor / 100;
                        OverrideData<int> damageTroopsOverride = OverrideData<int>.Create(damageTroops);
                        GameEvent.OnSkillDamageBuildingTroops?.Invoke(skill, beAtkBuildingBase, damageTroopsOverride);
                        damageTroops = damageTroopsOverride.ValueAndRecycle;
                        int ep = Math.Max(1, damageTroops / 10);
                        if (!city.IsSameForce(troop) && !city.ChangeTroops(-damageTroops, troop, city.mBelongForce != null))
                        {
                            if (city.IsCity())
                                ep += 1200;
                            else if (city.IsGate())
                                ep += 600;
                            else if (city.IsPort())
                                ep += 300;
                            troop.GainEP(ep);
                            runtime.SyncCityAfterCombat(city);
                            city.OnFall(skill);
                            return;
                        }
                        else
                        {
                            troop.GainEP(ep);
                        }
                    }

                    int damage = CalculateDamageTroopVsBuilding(troop, beAtkBuildingBase, atkDurability) * criticalFactor / 100;
                    OverrideData<int> durabilityOverride = OverrideData<int>.Create(damage);
                    GameEvent.OnSkillDamageBuildingDurability?.Invoke(skill, beAtkBuildingBase, durabilityOverride);
                    damage = durabilityOverride.ValueAndRecycle;
                    if (beAtkBuildingBase.ChangeDurability(-damage, skill))
                    {
                        int ep = damage + 200;
                        troop.GainEP(ep);
                    }
                    else
                    {
                        int ep = damage;
                        troop.GainEP(ep);

                        // 城池反击
                        if (targetBuilding == beAtkBuildingBase)
                        {
                            float hitBack = beAtkBuildingBase.GetAttackBackFactor(skill, scenario.Map.Distance(troop.cell, atkCell));
                            if (hitBack > 0)
                            {
                                int atkBack = beAtkBuildingBase.GetAttackBack();
                                OverrideData<int> atkBackOverride = OverrideData<int>.Create(atkBack);
                                GameEvent.OnBuildCalculateAttackBack?.Invoke(troop, spellCell, beAtkBuildingBase, skill, atkBackOverride);
                                atkBack = atkBackOverride.ValueAndRecycle;
                                if (atkBack > 0)
                                {
                                    int hitBackDmg = (int)Math.Ceiling(hitBack * CalculateDamageBuildingVsTroop(beAtkBuildingBase, troop, atkBack));
                                    ApplyTroopChange(runtime, troop, -hitBackDmg, beAtkBuildingBase, atkBack);
                                }
                            }
                        }
                    }
                    runtime.SyncCityAfterCombat(beAtkBuildingBase as City);
                }
            }

            DoOffset(runtime, troop, targetTroop, targetDamage, blockFactor, offsetAction, skill);

            DoEffect(troop, spellCell, tempCellList, canSpellToCell, canDamageTroop, canDamageBuilding, canDamageTeam, skill);

            if (troop.IsAlive)
            {
                ApplyMoraleChange(runtime, troop, -costEnergy, showInfo: false);
            }

            GameEvent.OnSkillActionOver?.Invoke(skill);
        }

        // ---- 兵力段(Troop.ChangeTroops 转写:事件/伤兵/掷粮/生死/俘获) ----

        static void ApplyTroopChange(SangoCombatNativeRuntime runtime, Troop target, int num, SangoObject atk, int atkBack)
        {
            OverrideData<int> overrideData = OverrideData<int>.Create(num);
            GameEvent.OnTroopChangeTroops?.Invoke(target, atk, atkBack, overrideData);
            num = overrideData.ValueAndRecycle;

            if (num == 0)
            {
                return;
            }

            // 表现位与内核同条件保留(headless Render 为内核同款对象,同调用零差异)。
            if (target.Render != null)
            {
                if (atk.ObjectType == SangoObjectType.SkillInstance)
                {
                    SkillInstance skill = (SkillInstance)atk;
                    if (skill != null && target.Render != null)
                    {
                        bool isCrit = skill.IsCritical();
                        target.Render.ShowInfo(num, (int)InfoType.Troop, isCrit);
                    }
                }
            }

            target.troops = target.troops + num;
            if (num < 0)
            {
                if (target.Render != null && target.Render.IsVisible())
                {
                    GameMedia.Instance.PlayPersonSay(target.Leader, GameRandom.Chance(50) ? 3216 : 3230);
                    GameParticales.Instance.PlayEfect("Assets/Effect/Prefab/ef_troop_destroy.prefab", target.Render.MapObject.position, 3);
                }

                int absNum = Math.Abs(num);
                target.woundedTroops += (int)Math.Ceiling(absNum * 0.14f);
                int foodCost = (int)Math.Ceiling(Scenario.Cur!.Variables.baseFoodCostInTroop * absNum * target.TroopType.foodCostFactor) / 2;
                int divFood = 0;
                // 有概率保留部分
                if (GameRandom.Chance(80))
                    divFood += foodCost;
                if (GameRandom.Chance(50))
                    divFood += foodCost;
                target.ChangeFood(-divFood, false);

                target.IsAlive = target.troops > 0;
            }
            else
            {
                if (target.troops > target.MaxTroops)
                    target.troops = target.MaxTroops;
            }

            if (!target.IsAlive)
            {
                ApplyTroopDestroyed(target, atk, atkBack);
                target.Clear();
            }

            if (target.Render != null)
            {
                target.Render.UpdateRender();
            }

            runtime.SyncTroopAfterCombat(target);
        }

        // 溃灭段(Troop.OnDestroy 转写:俘获掷点 + 献俘演出事件 + 内核事件位)。
        static void ApplyTroopDestroyed(Troop target, SangoObject atk, int atkBack)
        {
            if (atk.ObjectType == SangoObjectType.SkillInstance)
            {
                SkillInstance skill = (SkillInstance)atk;
                Troop atkTroop = skill.master;

                var captives = new List<Person>();
                if (target.Leader.state != (int)PersonStateType.Governor)
                {
                    int p = Math.Max(0, atkTroop.GetCaptureChangce() - target.Leader.escapeFactorWhenTroopDestroy);
                    if (GameRandom.Chance(p))
                        captives.Add(target.Leader);
                }
                if (target.Member1 != null && target.Member1.state != (int)PersonStateType.Governor)
                {
                    int p = Math.Max(0, atkTroop.GetCaptureChangce() - target.Member1.escapeFactorWhenTroopDestroy);
                    if (GameRandom.Chance(p))
                        captives.Add(target.Member1);
                }

                if (target.Member2 != null && target.Member2.state != (int)PersonStateType.Governor)
                {
                    int p = Math.Max(0, atkTroop.GetCaptureChangce() - target.Member2.escapeFactorWhenTroopDestroy);
                    if (GameRandom.Chance(p))
                        captives.Add(target.Member2);
                }

                if (captives.Count > 0)
                {
                    CityRecruitPersonWhenTroopFallEvent te = RenderEvent.Instance.Create<CityRecruitPersonWhenTroopFallEvent>();
                    te.Init(captives, atkTroop);
                    RenderEvent.Instance.Add(te);
                }
            }

            GameEvent.OnTroopDestroyed?.Invoke(target, atk, atkBack, Scenario.Cur);
        }

        // ---- 士气段(Troop.ChangeMorale 转写) ----

        static void ApplyMoraleChange(SangoCombatNativeRuntime runtime, Troop target, int num, bool showInfo)
        {
            OverrideData<int> overrideData = OverrideData<int>.Create(num);
            GameEvent.OnTroopChangeMorale?.Invoke(target, target.morale, overrideData);
            num = overrideData.ValueAndRecycle;

            if (num == 0)
                return;

            target.morale += num;

            if (target.morale < 0)
                target.morale = 0;
            else if (target.morale > target.MaxMorale)
                target.morale = target.MaxMorale;

            if (showInfo)
                target.Render?.ShowInfo(num, (int)InfoType.Morale);
            target.Render?.UpdateRender();
            runtime.SyncTroopAfterCombat(target);
        }

        // ---- 击退段(SkillInstance.DoOffset 转写;位移走内核 UpdateCell,碰撞伤害走兵力段) ----

        static void DoOffset(
            SangoCombatNativeRuntime runtime,
            Troop troop,
            Troop? targetTroop,
            int targetDamage,
            int blockFactor,
            int[] offsetAction,
            SkillInstance skill)
        {
            if (targetTroop == null || offsetAction.Length <= 0) return;
            var checkList = new List<Cell>();
            int dir = troop.cell.DirectionTo(targetTroop.cell);
            for (int k = 0; k < offsetAction.Length; k += 2)
            {
                int offsetType = offsetAction[k];
                int offsetLength = offsetAction[k + 1];
                int targetDir = dir;
                int absOffsetLength = Math.Abs(offsetLength);
                if (offsetLength < 0)
                    targetDir -= 3;
                checkList.Clear();
                switch (offsetType)
                {
                    case (int)SkillCellOffsetType.Master:
                        {
                            if (troop.IsAlive)
                            {
                                troop.cell.GetDirectionLine(targetDir, absOffsetLength, checkList);
                                for (int i = 0; i < checkList.Count; i++)
                                {
                                    Cell c = checkList[i];
                                    if (c.CanStay(troop))
                                        troop.UpdateCell(c, troop.cell, true);
                                    else
                                        break;
                                }
                            }
                        }
                        break;
                    case (int)SkillCellOffsetType.Target:
                        {
                            if (targetTroop.IsAlive)
                            {
                                targetTroop.cell.GetDirectionLine(targetDir, absOffsetLength, checkList);
                                for (int i = 0; i < checkList.Count; i++)
                                {
                                    Cell c = checkList[i];
                                    if (c.CanStay(targetTroop))
                                        targetTroop.UpdateCell(c, targetTroop.cell, true);
                                    else
                                        break;
                                }
                            }
                        }
                        break;
                    case (int)SkillCellOffsetType.MasterBlock:
                        {
                            if (troop.IsAlive)
                            {
                                troop.cell.GetDirectionLine(targetDir, absOffsetLength, checkList);
                                for (int i = 0; i < checkList.Count; i++)
                                {
                                    Cell c = checkList[i];
                                    if (c.CanStay(troop))
                                        troop.UpdateCell(c, troop.cell, true);
                                    else
                                    {
                                        Troop? blockTroop = c.troop;
                                        if (blockFactor > 0 && blockTroop != null && blockTroop.IsEnemy(troop))
                                        {
                                            int blockDmg = targetDamage * blockFactor / 100;
                                            ApplyTroopChange(runtime, blockTroop, -blockDmg, skill, -blockFactor);
                                            int ep = blockDmg / 10;
                                            if (!blockTroop.IsAlive)
                                            {
                                                ep += 200;
                                                troop.GainTargetResource(blockTroop);
                                            }
                                            troop.GainEP(ep);
                                        }
                                        break;
                                    }
                                }
                            }
                        }
                        break;
                    case (int)SkillCellOffsetType.TargetBlock:
                        {
                            if (targetTroop.IsAlive)
                            {
                                // 内核此处 CanStay 检查的是 troop、位移的是 targetTroop(原样保持)。
                                targetTroop.cell.GetDirectionLine(targetDir, absOffsetLength, checkList);
                                for (int i = 0; i < checkList.Count; i++)
                                {
                                    Cell c = checkList[i];
                                    if (c.CanStay(troop))
                                        targetTroop.UpdateCell(c, targetTroop.cell, true);
                                    else
                                    {
                                        Troop? blockTroop = c.troop;
                                        if (blockFactor > 0 && blockTroop != null && blockTroop.IsEnemy(troop))
                                        {
                                            int blockDmg = targetDamage * blockFactor / 100;
                                            ApplyTroopChange(runtime, blockTroop, -blockDmg, skill, -blockFactor);
                                            int ep = blockDmg / 10;
                                            if (!blockTroop.IsAlive)
                                            {
                                                ep += 200;
                                                troop.GainTargetResource(blockTroop);
                                            }
                                            troop.GainEP(ep);
                                        }
                                        break;
                                    }
                                }
                            }
                        }
                        break;
                    case (int)SkillCellOffsetType.MasterJustCheckEnd:
                        {
                            if (troop.IsAlive)
                            {
                                troop.cell.GetDirectionLine(targetDir, absOffsetLength, checkList);
                                for (int i = checkList.Count - 1; i >= 0; i--)
                                {
                                    Cell c = checkList[i];
                                    if (c.CanStay(troop))
                                    {
                                        troop.UpdateCell(c, troop.cell, true);
                                        break;
                                    }
                                }
                            }
                        }
                        break;
                    case (int)SkillCellOffsetType.MasterRandom:
                        {
                            if (troop.IsAlive)
                            {
                                var availableCells = new List<Cell>();
                                troop.cell.GetNeighbors((cell) =>
                                {
                                    if (cell.CanStay(troop))
                                        availableCells.Add(cell);
                                });
                                if (availableCells.Count > 0)
                                {
                                    int randomIndex = GameRandom.Range(0, availableCells.Count);
                                    Cell targetCell = availableCells[randomIndex];
                                    troop.UpdateCell(targetCell, troop.cell, true);
                                }
                            }
                        }
                        break;
                    case (int)SkillCellOffsetType.TargetRandom:
                        {
                            if (targetTroop.IsAlive)
                            {
                                var availableCells = new List<Cell>();
                                targetTroop.cell.GetNeighbors((cell) =>
                                {
                                    if (cell.CanStay(targetTroop))
                                        availableCells.Add(cell);
                                });
                                if (availableCells.Count > 0)
                                {
                                    int randomIndex = GameRandom.Range(0, availableCells.Count);
                                    Cell targetCell = availableCells[randomIndex];
                                    targetTroop.UpdateCell(targetCell, targetTroop.cell, true);
                                }
                            }
                        }
                        break;
                    case (int)SkillCellOffsetType.Master指定位置:
                        {
                            if (troop.IsAlive && offsetAction.Length > k + 2)
                            {
                                int targetX = offsetAction[k + 2];
                                int targetY = offsetAction[k + 3];
                                Cell? targetCell = Scenario.Cur!.Map.GetCell(targetX, targetY);
                                if (targetCell != null && targetCell.CanStay(troop))
                                {
                                    troop.UpdateCell(targetCell, troop.cell, true);
                                }
                            }
                        }
                        break;
                    case (int)SkillCellOffsetType.Target指定位置:
                        {
                            if (targetTroop.IsAlive && offsetAction.Length > k + 2)
                            {
                                int targetX = offsetAction[k + 2];
                                int targetY = offsetAction[k + 3];
                                Cell? targetCell = Scenario.Cur!.Map.GetCell(targetX, targetY);
                                if (targetCell != null && targetCell.CanStay(targetTroop))
                                {
                                    targetTroop.UpdateCell(targetCell, targetTroop.cell, true);
                                }
                            }
                        }
                        break;
                }
            }
        }

        // ---- 技能效果段(SkillInstance.DoEffect 编排转写;效果体是内核 SkillEffect) ----

        static readonly FieldInfo SkillEffectsField = typeof(SkillInstance).GetField("effects", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException(
                "Kernel seam moved: SkillInstance.effects no longer exists; re-audit the D-4' DoEffect transcription.");

        static void DoEffect(
            Troop troop,
            Cell spellCell,
            List<Cell> atkCellList,
            bool canSpellToCell,
            bool canDamageTroop,
            bool canDamageBuilding,
            bool canDamageTeam,
            SkillInstance skill)
        {
            List<SkillEffect>? effects = (List<SkillEffect>?)SkillEffectsField.GetValue(skill);
            if (effects == null || effects.Count == 0) return;

            foreach (Cell target in atkCellList)
            {
                Cell atkCell = target;
                if (canSpellToCell)
                {
                    effects.ForEach(s => s.Action(target));
                }
                else
                {
                    Troop? beAtkTroop = atkCell.troop;
                    if (beAtkTroop != null && canDamageTroop && (troop.IsEnemy(beAtkTroop) || canDamageTeam))
                    {
                        effects.ForEach(s => s.Action(target));
                    }
                    else
                    {
                        BuildingBase? beAtkBuildingBase = atkCell.building;
                        if (beAtkBuildingBase != null && canDamageBuilding && (troop.IsEnemy(beAtkBuildingBase) || canDamageTeam))
                        {
                            effects.ForEach(s => s.Action(target));
                        }
                    }
                }
            }
        }

        // ---- 伤害公式转写(Troop.CalculateSkillDamage×5 / CalculateRestrainBoost;算式形状与取整点逐位一致) ----

        public static int CalculateDamageTroopVsTroop(Troop attacker, Troop target, int atkBounds)
        {
            ScenarioVariables variables = Scenario.Cur!.Variables;

            float difficultyDamageFactor = 1;
            if (attacker.mBelongForce != null && attacker.mBelongForce.IsPlayer)
                difficultyDamageFactor = variables.DifficultyDamageFactor;

            int damage = (int)(
                (

                (Math.Pow(atkBounds * variables.fight_base_damage, 0.5) + Math.Max(0, (int)((Math.Pow(attacker.Attack, 2) - Math.Pow(Math.Max(40, target.Defence), 2)) / 300)) +
                Math.Max(0, (attacker.troops - target.troops) / variables.fight_base_troops_need) + 50)

                * 10 * ((int)(

                (((int)(attacker.troops * 0.01) + 300) * Math.Pow((attacker.Attack + 50), 2)) /
                (((int)(attacker.troops * 0.01) + 300) * Math.Pow((attacker.Attack + 50), 2) * 0.01 +
                ((int)(target.troops * 0.01) + 300) * Math.Pow((target.Defence + 50), 2) * 0.01)

                - 50)

                + 50)
                * Math.Min(Math.Pow(Math.Max(1, attacker.troops / 4), 0.5), 40)

                * variables.fight_damage_magic_number

                + attacker.troops / variables.fight_base_troop_count

                )
                * CalculateRestrainBoost(attacker, target)
                * Math.Max(0, (1 + attacker.DamageTroopExtraFactor))
                * difficultyDamageFactor
                );

            return damage;
        }

        public static int CalculateDamageTroopVsBuilding(Troop attacker, BuildingBase target, int atkDurability)
        {
            TroopType attackTroopType = attacker.TroopType;
            BuildingType buildingType = target.BuildingType;
            ScenarioVariables variables = Scenario.Cur!.Variables;

            float difficultyDamageFactor = 1;
            if (attacker.mBelongForce != null && attacker.mBelongForce.IsPlayer)
                difficultyDamageFactor = variables.DifficultyDamageFactor;

            if (attacker.IsHelepolis)
            {
                int damage = (int)(attacker.troops / 25 + Math.Pow(attacker.troops, 0.5f) + Math.Min(Math.Pow(attacker.troops, 0.5f), 40) * attacker.Attack * Math.Pow((1f / 1500f), 0.5f) * (1 + (float)atkDurability / 25f) * buildingType.damageBounds
               * Math.Max(0, (1 + attacker.DamageBuildingExtraFactor))
               * attackTroopType.durabilityDmg / 100
               * difficultyDamageFactor
               );
                return damage;
            }
            else
            {
                int damage = (int)(Math.Pow(attacker.troops, 0.5f) * attacker.Attack * Math.Pow((1f / 1500f), 0.5f) * (1 + (float)atkDurability / 25f) * buildingType.damageBounds
                * Math.Max(0, (1 + attacker.DamageBuildingExtraFactor))
                * attackTroopType.durabilityDmg / 100
                * difficultyDamageFactor
                );

                return damage;
            }
        }

        public static int CalculateDamageTroopOnCity(Troop attacker, City target, int atkBounds)
        {
            ScenarioVariables variables = Scenario.Cur!.Variables;

            float difficultyDamageFactor = 1;
            if (attacker.mBelongForce != null && attacker.mBelongForce.IsPlayer)
                difficultyDamageFactor = variables.DifficultyDamageFactor;

            int damage = (int)(
                (

                (Math.Pow(atkBounds * variables.fight_base_damage, 0.5) + Math.Max(0, (int)((Math.Pow(attacker.Attack, 2) - Math.Pow(Math.Max(40, target.GetDefence()), 2)) / 300)) +
                Math.Max(0, (attacker.troops - target.troops) / variables.fight_base_troops_need) + 50)

                * 10 * ((int)(

                (((int)(attacker.troops * 0.01) + 300) * Math.Pow((attacker.Attack + 50), 2)) /
                (((int)(attacker.troops * 0.01) + 300) * Math.Pow((attacker.Attack + 50), 2) * 0.01 +
                ((int)(target.troops * 0.01) + 300) * Math.Pow((target.GetDefence() + 50), 2) * 0.01)

                - 50)

                + 50)
                * Math.Min(Math.Pow(Math.Max(1, attacker.troops / 4), 0.5), 40)

                * variables.fight_damage_magic_number

                * target.BuildingType.damageBounds

                + attacker.troops / variables.fight_base_troop_count

                )

                * Math.Max(0, (1 + attacker.DamageTroopExtraFactor))

                * difficultyDamageFactor
                );

            return damage;
        }

        public static int CalculateDamageBuildingVsTroop(BuildingBase attacker, Troop target, int atk)
        {
            ScenarioVariables variables = Scenario.Cur!.Variables;

            float baseAtk = atk;
            int baseTroops = attacker.GetSkillMethodAvaliabledTroops();

            float difficultyDamageFactor = 1;
            if (attacker.mBelongForce != null && attacker.mBelongForce.IsPlayer)
                difficultyDamageFactor = variables.DifficultyDamageFactor;

            int damage = (int)(
                 (

                 (Math.Max(0, (int)((Math.Pow(baseAtk, 2) - Math.Pow(Math.Max(40, target.Defence), 2)) / 300)) +
                 Math.Max(0, (baseTroops - target.troops) / variables.fight_base_troops_need) + 50)

                 * 10 * ((int)(

                 (((int)(baseTroops * 0.01) + 300) * Math.Pow((baseAtk + 50), 2)) /
                 (((int)(baseTroops * 0.01) + 300) * Math.Pow((baseAtk + 50), 2) * 0.01 +
                 ((int)(target.troops * 0.01) + 300) * Math.Pow((target.Defence + 50), 2) * 0.01)

                 - 50)

                 + 50)

                 * Math.Min(Math.Pow(Math.Max(1, baseTroops / 4), 0.5), 40)

                 * variables.fight_damage_magic_number

                 + baseTroops / variables.fight_base_troop_count

                 )

                * difficultyDamageFactor
                );

            return damage;
        }

        public static float CalculateRestrainBoost(Troop attacker, Troop target)
        {
            ScenarioVariables variables = Scenario.Cur!.Variables;
            float[] typeMap = variables.troops_type_restraint[attacker.TroopType.kind];
            return typeMap[target.TroopType.kind];
        }
    }
}
