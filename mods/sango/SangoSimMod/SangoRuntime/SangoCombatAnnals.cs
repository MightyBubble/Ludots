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
//   OnForceFall(City.OnFall:1802)                   势力灭亡行(先于城陷事件触发)。
// 纪律:处理器只读(含 OverrideData.Value 只读不 Recycle,池化对象不留引用),不消耗随机,
// 不改内核状态——订阅与否不影响确定性 digest。
// M2.d 结构化 per-battle 战报:同一事件群在文本行之外同步聚合为 SangoBattleRecord
// (不二次解析文本;行与结构化是同一事件的两种投影)。聚合键 = (攻方部队, 交战对象):
//   部队对部队 = 无序对(互授歼灭任务时双方都出技能,首个事件定向攻/守);
//   部队对城/建筑 = 有序键(围攻同一据点的多支部队各成一卡,城陷时全体收官);
//   城/建筑反击 = 优先挂"该部队围攻该据点"的既有卡,否则以据点为攻方开新卡。
// 终局事件(溃灭/城陷/灭亡)把卡收口;卡序即事件序,环 200 张。

using System;
using System.Collections.Generic;
using Sango.Core;
using Sango.Core.Tools;

namespace Sango.Runtime
{
    /// <summary>战斗卡的参战方快照(部队/城/建筑,名字+势力)。</summary>
    public sealed record SangoBattleParticipant(int Id, string Name, int ForceId, string ForceName, string Kind);

    /// <summary>战斗卡内一条交战事件(伤害序列的一步;TargetTroopsAfter=受方结算后余量)。</summary>
    public sealed record SangoBattleEventRow(
        int Turn, string Kind, string Attacker, string Defender, string? Skill, int Damage, int TargetTroopsAfter);

    /// <summary>参战方兵力/守军变化(首观测 → 终观测;城用守军数,部队用兵力数)。</summary>
    public sealed record SangoTroopChangeRow(string Name, string Kind, int Start, int End);

    /// <summary>城池归属变化([城陷] 卡片专属)。</summary>
    public sealed record SangoCityChangeRow(int CityId, string Name, string OldForce, string NewForce);

    /// <summary>
    /// 一场交战的聚合卡:回合区间、攻/守参战方、用过的技能、总杀伤、伤害事件序列、
    /// 兵力变化、终局(ongoing/defender-destroyed/attacker-destroyed/city-fallen/force-fallen)
    /// 与城池易主。Web UI 战报面板按卡渲染、按回合过滤。
    /// </summary>
    public sealed record SangoBattleRecord(
        long Seq,
        int TurnStart,
        int TurnLast,
        SangoBattleParticipant Attacker,
        SangoBattleParticipant Defender,
        string[] Skills,
        int DamageDealt,
        SangoBattleEventRow[] Events,
        SangoTroopChangeRow[] TroopChanges,
        string Result,
        SangoCityChangeRow? City);

    public sealed class SangoCombatAnnals : IDisposable
    {
        public const int MaxLines = 200;
        public const int MaxBattles = 200;

        public const string ResultOngoing = "ongoing";
        public const string ResultDefenderDestroyed = "defender-destroyed";
        public const string ResultAttackerDestroyed = "attacker-destroyed";
        public const string ResultCityFallen = "city-fallen";
        public const string ResultForceFallen = "force-fallen";

        private readonly object _sync = new();
        private readonly Queue<string> _lines = new();
        private readonly Queue<BattleState> _battles = new();
        private readonly Dictionary<string, BattleState> _openByKey = new(StringComparer.Ordinal);
        private long _battleSeq;
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
            // M3.f 战斗演出:单挑走内核静态事件;舌战事件挂在实例上,经
            // SangoChallengeOps 的静态转发汇入同一消息流。
            GameEvent.OnDuelStart += OnDuelStart;
            GameEvent.OnDuelEnd += OnDuelEnd;
            SangoChallengeOps.DebateLinePublished += OnDebateLine;
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
            GameEvent.OnDuelStart -= OnDuelStart;
            GameEvent.OnDuelEnd -= OnDuelEnd;
            SangoChallengeOps.DebateLinePublished -= OnDebateLine;
            _attached = false;
        }

        public string[] SnapshotLines()
        {
            lock (_sync)
            {
                return _lines.ToArray();
            }
        }

        /// <summary>结构化 per-battle 战报快照(卡序即事件序;战报面板话题的真源)。</summary>
        public SangoBattleRecord[] SnapshotBattles()
        {
            lock (_sync)
            {
                var records = new SangoBattleRecord[_battles.Count];
                int index = 0;
                foreach (BattleState state in _battles)
                {
                    records[index++] = state.ToRecord();
                }

                return records;
            }
        }

        public void Clear()
        {
            lock (_sync)
            {
                _lines.Clear();
                _battles.Clear();
                _openByKey.Clear();
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

        int CurrentTurn => Scenario.Cur?.Info?.turnCount ?? 0;

        // 事件处理器:文本行与结构化卡同锁更新(_sync 可重入;结构化读方只有 Snapshot*)。

        void OnSkillDamageTroop(SkillInstance skill, Troop defender, OverrideData<int> damage)
        {
            if (skill == null || defender == null)
            {
                return;
            }

            Troop attacker = skill.master;
            int dealt = Math.Max(0, damage.Value);
            int after = Math.Max(0, defender.troops - dealt);
            Publish(
                $"[战斗] {TroopLabel(attacker)} 以「{skill.Name}」打击 {TroopLabel(defender)},杀伤 {dealt},守军余 {after}");
            lock (_sync)
            {
                BattleState battle = BattleFor(attacker, defender);
                battle.Observe(attacker, attacker.troops);
                battle.Observe(defender, after);
                battle.Append(CurrentTurn, "strike", TroopLabel(attacker), TroopLabel(defender), skill.Name, dealt, after);
                battle.AddSkill(skill.Name);
                battle.DamageDealt += dealt;
            }
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
            int after = troop.troops - dealt;
            Publish($"[反击] {TroopLabel(troop)} 遭 {source} 反击,伤亡 {dealt},余 {after}");
            lock (_sync)
            {
                BattleState battle;
                if (atk is Troop counterTroop)
                {
                    battle = BattleFor(counterTroop, troop);
                    battle.Observe(counterTroop, counterTroop.troops);
                }
                else if (atk is BuildingBase building)
                {
                    // 围城反制:优先挂"该部队围攻该据点"的既有卡,没有才以据点为攻方开新卡。
                    string siegeKey = SiegeKey(troop.Id, building.Id);
                    battle = _openByKey.GetValueOrDefault(siegeKey)
                        ?? CreateBattle(building, troop, SiegeKey(building.Id, troop.Id));
                }
                else
                {
                    return;
                }

                battle.Observe(troop, after);
                battle.Append(CurrentTurn, "counter", source, TroopLabel(troop), null, dealt, after);
            }
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
            lock (_sync)
            {
                foreach (BattleState battle in _battles)
                {
                    if (battle.Closed || !battle.Involves(troop.Id))
                    {
                        continue;
                    }

                    battle.Observe(troop, 0);
                    battle.Append(CurrentTurn, "troop-destroyed", source, TroopLabel(troop), null, 0, 0);
                    battle.Result = ReferenceEquals(battle.DefenderSource, troop)
                        ? ResultDefenderDestroyed
                        : ResultAttackerDestroyed;
                    Close(battle);
                }
            }
        }

        void OnSkillDamageBuildingTroops(SkillInstance skill, BuildingBase building, OverrideData<int> damage)
        {
            if (skill?.master == null || building == null)
            {
                return;
            }

            if (building is City city)
            {
                Troop attacker = skill.master;
                int dealt = Math.Max(0, damage.Value);
                int after = Math.Max(0, city.troops - dealt);
                Publish(
                    $"[攻城] {TroopLabel(attacker)} 攻 {BuildingLabel(city)},守军伤亡 {dealt},城内驻军余 {after}");
                lock (_sync)
                {
                    BattleState battle = BattleFor(attacker, city);
                    battle.Observe(attacker, attacker.troops);
                    battle.Observe(city, after);
                    battle.Append(CurrentTurn, "siege-garrison", TroopLabel(attacker), BuildingLabel(city), skill.Name, dealt, after);
                    battle.AddSkill(skill.Name);
                    battle.DamageDealt += dealt;
                }
            }
        }

        void OnSkillDamageBuildingDurability(SkillInstance skill, BuildingBase building, OverrideData<int> damage)
        {
            if (skill?.master == null || building == null)
            {
                return;
            }

            Troop attacker = skill.master;
            int dealt = Math.Max(0, damage.Value);
            int after = Math.Max(0, building.durability - dealt);
            Publish($"[攻城] {TroopLabel(attacker)} 以「{skill.Name}」轰击 {BuildingLabel(building)},城防破坏 {dealt},耐久余 {after}");
            lock (_sync)
            {
                BattleState battle = BattleFor(attacker, building);
                battle.Observe(attacker, attacker.troops);
                battle.Append(CurrentTurn, "siege-durability", TroopLabel(attacker), BuildingLabel(building), skill.Name, dealt, after);
                battle.AddSkill(skill.Name);
            }
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
            lock (_sync)
            {
                foreach (BattleState battle in OpenCityBattles(city))
                {
                    battle.Append(CurrentTurn, "city-fall", taker, BuildingLabel(city), null, 0, 0);
                    if (battle.Result == ResultOngoing)
                    {
                        battle.Result = ResultCityFallen;
                    }

                    battle.City = new SangoCityChangeRow(
                        city.Id, city.Name ?? string.Empty, last,
                        atkTroop?.mBelongForce?.Name ?? "未知势力");
                    Close(battle);
                }
            }
        }

        void OnForceFall(Force force, City city, Troop atkTroop)
        {            if (force == null)
            {
                return;
            }

            string taker = atkTroop?.mBelongForce != null ? ForceLabel(atkTroop.mBelongForce) : "未知势力";
            string lastCity = city != null ? BuildingLabel(city) : "末城";
            Publish($"[灭亡] {ForceLabel(force)} 末城 {lastCity} 陷落,势力灭亡(亡于 {taker})");
            if (city == null)
            {
                return;
            }

            lock (_sync)
            {
                // 灭亡先于城陷事件触发:置终局不收口,城陷稍后补 City 变更并收官。
                foreach (BattleState battle in OpenCityBattles(city))
                {
                    battle.Append(CurrentTurn, "force-fall", taker, BuildingLabel(city), null, 0, 0);
                    battle.Result = ResultForceFallen;
                }
            }
        }

        // ---- M3.f 战斗演出行(单挑/舌战;与上面事件群同一消息流) ----

        // DuelSystem.StartDuel 尾部触发;此刻结果尚未结算,只报对阵。
        void OnDuelStart(DuelSystem duel)
        {
            if (duel?.AttackerTroop == null || duel.DefenderTroop == null)
            {
                return;
            }

            Publish(
                $"[单挑] {DuelLeaderLabel(duel.AttackerTroop)}(士气 {duel.AttackerTroop.morale}) 向 " +
                $"{DuelLeaderLabel(duel.DefenderTroop)}(士气 {duel.DefenderTroop.morale}) 发起单挑");
            lock (_sync)
            {
                BattleFor(duel.AttackerTroop, duel.DefenderTroop)
                    .Append(CurrentTurn, "duel", TroopLabel(duel.AttackerTroop), TroopLabel(duel.DefenderTroop), null, 0, 0);
            }
        }

        // EndDuel 尾部触发:HandleDuelResult(士气直写 ±20、30% 俘将、20% 部队溃灭)
        // 已结算完毕,此处读的是落点后状态。
        void OnDuelEnd(DuelSystem duel, DuelResult result)
        {
            if (duel?.AttackerTroop == null || duel.DefenderTroop == null)
            {
                return;
            }

            Troop winner = result == DuelResult.AttackerWin ? duel.AttackerTroop
                : result == DuelResult.DefenderWin ? duel.DefenderTroop
                : null!;
            Troop loser = ReferenceEquals(winner, duel.AttackerTroop) ? duel.DefenderTroop
                : winner == null ? null!
                : duel.AttackerTroop;

            string verdict = result switch
            {
                DuelResult.AttackerWin => "攻将获胜",
                DuelResult.DefenderWin => "守将获胜",
                DuelResult.Draw => "不分胜负",
                _ => result.ToString(),
            };

            string aftermath = string.Empty;
            if (winner != null && loser != null)
            {
                bool captured = winner.captiveList.Contains(loser.Leader!);
                bool destroyed = !loser.IsAlive;
                if (captured)
                {
                    aftermath += $",{loser.Leader!.Name} 被生擒";
                }

                if (destroyed)
                {
                    aftermath += $",{loser.Name} 全军溃灭";
                }
            }

            Publish(
                $"[单挑·终] {DuelLeaderLabel(duel.AttackerTroop)} 对 {DuelLeaderLabel(duel.DefenderTroop)}:{verdict}," +
                $"攻军士气 {duel.AttackerTroop.morale},守军士气 {duel.DefenderTroop.morale}{aftermath}");
            lock (_sync)
            {
                BattleFor(duel.AttackerTroop, duel.DefenderTroop)
                    .Append(CurrentTurn, "duel-end", TroopLabel(duel.AttackerTroop), TroopLabel(duel.DefenderTroop), null, 0, 0);
            }
        }

        // SangoChallengeOps 的舌战行转发(内核舌战事件挂实例,不进 GameEvent 静态群)。
        void OnDebateLine(string line) => Publish(line);

        static string DuelLeaderLabel(Troop troop) =>
            $"{ForceLabel(troop.mBelongForce)}·{troop.Leader?.Name ?? "未知"}({troop.Name})";

        // ---- 聚合索引(全部在 _sync 内调用) ----

        // t: 键 = 部队对部队,一张卡登记双向键(任一方向检索同卡,攻/守由首事件定向);
        // b: 键 = 部队→据点围攻(围攻同一据点的多支部队各成一卡)。
        static string TroopPairKey(int leftId, int rightId) => $"t:{leftId}>{rightId}";
        static string SiegeKey(int attackerId, int targetId) => $"b:{attackerId}>{targetId}";

        BattleState BattleFor(Troop attacker, Troop defender)
        {
            string forward = TroopPairKey(attacker.Id, defender.Id);
            if (_openByKey.TryGetValue(forward, out BattleState? battle))
            {
                return battle;
            }

            string reverse = TroopPairKey(defender.Id, attacker.Id);
            if (_openByKey.TryGetValue(reverse, out battle))
            {
                return battle;
            }

            return CreateBattle(attacker, defender, forward, reverse);
        }

        BattleState BattleFor(Troop attacker, BuildingBase building)
        {
            string key = SiegeKey(attacker.Id, building.Id);
            return _openByKey.TryGetValue(key, out BattleState? battle)
                ? battle
                : CreateBattle(attacker, building, key);
        }

        List<BattleState> OpenCityBattles(City city)
        {
            var matches = new List<BattleState>();
            foreach (BattleState battle in _battles)
            {
                if (!battle.Closed && battle.Defender.Id == city.Id && battle.Defender.Kind == "city")
                {
                    matches.Add(battle);
                }
            }

            return matches;
        }

        BattleState CreateBattle(object attacker, object defender, params string[] keys)
        {
            var state = new BattleState(++_battleSeq, CurrentTurn, Participant(attacker), Participant(defender), keys)
            {
                AttackerSource = attacker,
                DefenderSource = defender,
            };
            _battles.Enqueue(state);
            foreach (string key in keys)
            {
                _openByKey[key] = state;
            }

            while (_battles.Count > MaxBattles)
            {
                DetachKeys(_battles.Dequeue());
            }

            return state;
        }

        void Close(BattleState battle)
        {
            battle.Closed = true;
            DetachKeys(battle);
        }

        void DetachKeys(BattleState battle)
        {
            foreach (string key in battle.Keys)
            {
                if (ReferenceEquals(_openByKey.GetValueOrDefault(key), battle))
                {
                    _openByKey.Remove(key);
                }
            }
        }

        static string ParticipantKind(object participant) => participant switch
        {
            Troop => "troop",
            City => "city",
            BuildingBase => "building",
            _ => "object",
        };

        static SangoBattleParticipant Participant(object participant)
        {
            switch (participant)
            {
                case Troop troop:
                    return new SangoBattleParticipant(
                        troop.Id, troop.Name ?? string.Empty,
                        troop.mBelongForce?.Id ?? 0, ForceLabel(troop.mBelongForce), "troop");
                case BuildingBase building:
                    return new SangoBattleParticipant(
                        building.Id, building.Name ?? string.Empty,
                        building.mBelongForce?.Id ?? 0, ForceLabel(building.mBelongForce), ParticipantKind(building));
                default:
                    return new SangoBattleParticipant(0, "未知", 0, "未知势力", "object");
            }
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

        /// <summary>聚合卡的内部可变态;ToRecord 是只读出锁视图。Source 保留对象身份供终局判位。</summary>
        private sealed class BattleState
        {
            readonly long _seq;
            readonly int _turnStart;
            readonly SangoBattleParticipant _attacker;
            readonly SangoBattleParticipant _defender;
            readonly List<SangoBattleEventRow> _events = new();
            readonly List<string> _skills = new();
            // 参战方 Id → 兵力/守军账本(城用守军数);首观测定 Start,后续刷新 End。
            readonly Dictionary<int, LedgerRow> _ledger = new();

            internal BattleState(long seq, int turn, SangoBattleParticipant attacker, SangoBattleParticipant defender, string[] keys)
            {
                _seq = seq;
                _turnStart = turn;
                TurnLast = turn;
                _attacker = attacker;
                _defender = defender;
                Keys = keys;
            }

            internal object AttackerSource { get; init; }
            internal object DefenderSource { get; init; }
            internal string[] Keys { get; }
            internal int TurnLast { get; set; }
            internal int DamageDealt { get; set; }
            internal string Result { get; set; } = ResultOngoing;
            internal SangoCityChangeRow? City { get; set; }
            internal bool Closed { get; set; }

            internal SangoBattleParticipant Defender => _defender;

            internal bool Involves(int troopId) =>
                (_attacker.Kind == "troop" && _attacker.Id == troopId) ||
                (_defender.Kind == "troop" && _defender.Id == troopId);

            internal void AddSkill(string? name)
            {
                if (!string.IsNullOrEmpty(name) && !_skills.Contains(name))
                {
                    _skills.Add(name);
                }
            }

            internal void Observe(object participant, int observed)
            {
                if (participant is not SangoObject sangoObject)
                {
                    return;
                }

                if (!_ledger.TryGetValue(sangoObject.Id, out LedgerRow? row))
                {
                    _ledger[sangoObject.Id] = new LedgerRow(
                        sangoObject switch
                        {
                            Troop troop => troop.Name ?? string.Empty,
                            BuildingBase building => building.Name ?? string.Empty,
                            _ => string.Empty,
                        },
                        ParticipantKind(participant),
                        observed);
                    return;
                }

                row.End = observed;
            }

            internal void Append(int turn, string kind, string attacker, string defender, string? skill,
                int damage, int targetTroopsAfter)
            {
                TurnLast = turn;
                _events.Add(new SangoBattleEventRow(turn, kind, attacker, defender, skill, damage, targetTroopsAfter));
            }

            internal SangoBattleRecord ToRecord()
            {
                var changes = new SangoTroopChangeRow[_ledger.Count];
                int index = 0;
                foreach (LedgerRow row in _ledger.Values)
                {
                    changes[index++] = new SangoTroopChangeRow(row.Name, row.Kind, row.Start, row.End);
                }

                return new SangoBattleRecord(
                    _seq, _turnStart, TurnLast, _attacker, _defender,
                    _skills.ToArray(), DamageDealt, _events.ToArray(), changes, Result, City);
            }

            internal sealed class LedgerRow
            {
                public LedgerRow(string name, string kind, int start)
                {
                    Name = name;
                    Kind = kind;
                    Start = start;
                    End = start;
                }

                internal string Name { get; }
                internal string Kind { get; }
                internal int Start { get; }
                internal int End { get; set; }
            }
        }
    }
}
