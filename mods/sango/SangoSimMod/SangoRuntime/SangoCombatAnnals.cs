// M2.c 战报采集器:订阅内核战斗 GameEvent 群,产出 MUD 风格人类可读战报行。
// 事件真源与触发点(全部在 SkillInstance.Action / Troop.ChangeTroops / City.OnFall 解算内):
//   OnSkillDamageTroop(SkillInstance.Action:628)   打击行:攻方/守方/技能/伤害/守军余量(伤害在
//                                                  ChangeTroops 前触发,余量=当前兵力-伤害);
//   OnTroopChangeTroops(Troop.ChangeTroops:1396)    反击行:atkBack>0 即反击通道(普通打击传 0,
//                                                  SkillInstance.Action:658/669 反击传 hitBack);
//   OnTroopDestroyed(Troop.OnDestroy:1532)          溃灭行:带攻击者(atk 为 SkillInstance/建筑);
//   OnSkillDamageBuildingTroops(Action:703)         攻城-守军杀伤行;
//   OnSkillDamageBuildingDurability(Action:732)     攻城-城防破坏行;
//   OnCityFall(City.OnFall:1860)                    城陷行(含原属势力);
//   OnForceFall(City.OnFall:1802)                   势力灭亡行。
// 纪律:处理器只读(含 OverrideData.Value 只读不 Recycle,池化对象不留引用),不消耗随机,
// 不改内核状态——订阅与否不影响确定性 digest。行文本即测试断言真源;结构化
// per-battle 战报聚合留给 M2.d,本层只做逐事件行。

using System;
using System.Collections.Generic;
using Sango.Core;
using Sango.Core.Tools;

namespace Sango.Runtime
{
    public sealed class SangoCombatAnnals : IDisposable
    {
        public const int MaxLines = 200;

        private readonly object _sync = new();
        private readonly Queue<string> _lines = new();
        private bool _attached;

        /// <summary>新战报行发布(feed 转投消息流;内核线程即发布线程)。</summary>
        public event Action<string>? LinePublished;

        /// <summary>订阅内核战斗事件群(幂等;内核二次启动无需重挂,事件是静态委托)。</summary>
        public void Attach()
        {
            if (_attached)
            {
                return;
            }

            GameEvent.OnSkillDamageTroop += OnSkillDamageTroop;
            GameEvent.OnTroopChangeTroops += OnTroopChangeTroops;
            GameEvent.OnTroopDestroyed += OnTroopDestroyed;
            GameEvent.OnSkillDamageBuildingTroops += OnSkillDamageBuildingTroops;
            GameEvent.OnSkillDamageBuildingDurability += OnSkillDamageBuildingDurability;
            GameEvent.OnCityFall += OnCityFall;
            GameEvent.OnForceFall += OnForceFall;
            _attached = true;
        }

        public void Dispose()
        {
            if (!_attached)
            {
                return;
            }

            GameEvent.OnSkillDamageTroop -= OnSkillDamageTroop;
            GameEvent.OnTroopChangeTroops -= OnTroopChangeTroops;
            GameEvent.OnTroopDestroyed -= OnTroopDestroyed;
            GameEvent.OnSkillDamageBuildingTroops -= OnSkillDamageBuildingTroops;
            GameEvent.OnSkillDamageBuildingDurability -= OnSkillDamageBuildingDurability;
            GameEvent.OnCityFall -= OnCityFall;
            GameEvent.OnForceFall -= OnForceFall;
            _attached = false;
        }

        public string[] SnapshotLines()
        {
            lock (_sync)
            {
                return _lines.ToArray();
            }
        }

        public void Clear()
        {
            lock (_sync)
            {
                _lines.Clear();
            }
        }

        void Publish(string line)
        {
            lock (_sync)
            {
                _lines.Enqueue(line);
                while (_lines.Count > MaxLines)
                {
                    _lines.Dequeue();
                }
            }

            LinePublished?.Invoke(line);
        }

        void OnSkillDamageTroop(SkillInstance skill, Troop defender, OverrideData<int> damage)
        {
            if (skill == null || defender == null)
            {
                return;
            }

            Troop attacker = skill.master;
            int dealt = Math.Max(0, damage.Value);
            Publish(
                $"[战斗] {TroopLabel(attacker)} 以「{skill.Name}」打击 {TroopLabel(defender)},杀伤 {dealt},守军余 {Math.Max(0, defender.troops - dealt)}");
        }

        // atkBack>0 = SkillInstance.Action 的反击通道(普通打击/伤兵损耗传 0 不成行);
        // num<0 才是伤害方向,正数(补员)即便带 atkBack 也不报。
        void OnTroopChangeTroops(Troop troop, SangoObject atk, int atkBack, OverrideData<int> num)
        {
            if (atkBack <= 0 || num == null || num.Value >= 0)
            {
                return;
            }

            string source = SourceLabel(atk);
            if (source.Length == 0)
            {
                return;
            }

            int dealt = Math.Min(-num.Value, troop.troops);
            Publish($"[反击] {TroopLabel(troop)} 遭 {source} 反击,伤亡 {dealt},余 {troop.troops - dealt}");
        }

        void OnTroopDestroyed(Troop troop, SangoObject atk, int atkBack, Scenario scenario)
        {
            if (troop == null)
            {
                return;
            }

            string source = SourceLabel(atk);
            Publish(source.Length > 0
                ? $"[溃灭] {TroopLabel(troop)} 全军覆没(败于 {source})"
                : $"[溃灭] {TroopLabel(troop)} 全军覆没");
        }

        void OnSkillDamageBuildingTroops(SkillInstance skill, BuildingBase building, OverrideData<int> damage)
        {
            if (skill?.master == null || building == null)
            {
                return;
            }

            if (building is City city)
            {
                int dealt = Math.Max(0, damage.Value);
                Publish(
                    $"[攻城] {TroopLabel(skill.master)} 攻 {BuildingLabel(city)},守军伤亡 {dealt},城内驻军余 {Math.Max(0, city.troops - dealt)}");
            }
        }

        void OnSkillDamageBuildingDurability(SkillInstance skill, BuildingBase building, OverrideData<int> damage)
        {
            if (skill?.master == null || building == null)
            {
                return;
            }

            int dealt = Math.Max(0, damage.Value);
            Publish($"[攻城] {TroopLabel(skill.master)} 以「{skill.Name}」轰击 {BuildingLabel(building)},城防破坏 {dealt},耐久余 {Math.Max(0, building.durability - dealt)}");
        }

        void OnCityFall(City city, Force lastForce, Troop atkTroop)
        {
            if (city == null)
            {
                return;
            }

            string taker = atkTroop != null ? TroopLabel(atkTroop) : "未知之师";
            string last = lastForce != null ? ForceLabel(lastForce) : "白城";
            Publish($"[城陷] {taker} 攻陷 {BuildingLabel(city)}(原属 {last})");
        }

        void OnForceFall(Force force, City city, Troop atkTroop)
        {
            if (force == null)
            {
                return;
            }

            string taker = atkTroop?.mBelongForce != null ? ForceLabel(atkTroop.mBelongForce) : "未知势力";
            string lastCity = city != null ? BuildingLabel(city) : "末城";
            Publish($"[灭亡] {ForceLabel(force)} 末城 {lastCity} 陷落,势力灭亡(亡于 {taker})");
        }

        static string TroopLabel(Troop troop)
        {
            if (troop == null)
            {
                return "未知部队";
            }

            return $"{ForceLabel(troop.mBelongForce)}·{troop.Name}({troop.troops})";
        }

        static string ForceLabel(Force force)
        {
            return force?.Name ?? $"势力#{force?.Id ?? 0}";
        }

        static string BuildingLabel(BuildingBase building)
        {
            if (building == null)
            {
                return "未知据点";
            }

            return $"{ForceLabel(building.mBelongForce)}·{building.Name}";
        }

        // 攻击者真名:SkillInstance 归属出招部队(master),建筑/部队直接具名。
        static string SourceLabel(SangoObject atk)
        {
            if (atk == null)
            {
                return string.Empty;
            }

            if (atk.ObjectType == SangoObjectType.SkillInstance && atk is SkillInstance skill)
            {
                return skill.master != null ? TroopLabel(skill.master) : string.Empty;
            }

            if (atk is Troop troop)
            {
                return TroopLabel(troop);
            }

            if (atk is BuildingBase building)
            {
                return BuildingLabel(building);
            }

            return string.Empty;
        }
    }
}
