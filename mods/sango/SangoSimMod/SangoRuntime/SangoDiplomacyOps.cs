// M3.d 外交命令内核操作层(与 SangoCityOps/SangoTroopOps 同层):命令层(Web UI payload
// 解析)与重放器(SangoReplayJournal)共用的唯一实现。命令到内核的映射逆向自原版玩家
// 城内外交菜单(sango-src Project/Assets/Sango/Scripts/Game/System/Diplomacy/*.cs):
//   结盟(alliance)        CityDiplomacyAlliance.DoJob → DiplomacyManager.
//                          CreateDiplomacyAction(Alliance, 城势力, 目标势力, 使者, 金) +
//                          DispatchDiplomat(使者赴对方君主城,PersonDiplomacy 任务;
//                          OnDispatch 扣金;resourceValue==0 时 Action 构造器取默认 3000)
//   送礼(sendGift)        CityDiplomacySendGift.DoJob → CreateDiplomacyAction(SendGift,
//                          ..., JobType.GetJobCost(SendGift)=1000) + DispatchDiplomat
//   摒弃同盟(discardAlliance) CityDiplomacyDiscardAlliance.DoJob → 双方 AllianceList 直接
//                          摘除同盟对象(不过 DiplomacyManager,原版未接关系惩罚)
//   宣战/停战/通商/和亲/请求技术/请求兵力/赎回俘虏:原版 DoJob 空桩("暂时留空")或
//   CreateDiplomacyAction 无 case 返回 null → 停用面,本层不接入(分级表见 M3.d 汇报)。
// 下单前置 IsValid 逐条照抄(freePersons>0 && 军团 AP>=costAP && 城 gold>=1000);原版
// 外交 DoJob 不扣 AP(只扣金,城内政命令才 ReduceActionPoint),照抄。成功入 journal
// (kind=diplomacyCommand)。使者到达结算链是内核既有面,不改:Person.OnTurnStart 的
// PersonDiplomacy 分支 → 玩家局 DiplomacyEvent 演出事件(headless 自动确认)→
// DiplomacyManager.ExecuteDiplomacyMission → Action 效果 + GameEvent.OnDiplomacy*。

using System.Collections.Generic;
using Sango.Core;

namespace Sango.Runtime
{
    public static class SangoDiplomacyOps
    {
        /// <summary>
        /// 按命令型分派(alliance/sendGift/discardAlliance)。diplomat 取 personIds 首位
        /// (原版窗口 DoJob 只用 personList[0]);alliance 另可携 resourceValue 金额
        /// (0 = 原版 Action 构造器默认 3000)。
        /// </summary>
        public static SangoTroopOpResult Execute(
            Scenario scenario, City city, string type, IReadOnlyList<int> personIds,
            int targetForceId, int resourceValue = 0)
        {
            ArgumentNullException.ThrowIfNull(scenario);
            ArgumentNullException.ThrowIfNull(city);
            if (string.IsNullOrEmpty(type)) throw new ArgumentNullException(nameof(type));
            ArgumentNullException.ThrowIfNull(personIds);

            if (city.mBelongForce == null || city.mBelongCorps == null)
            {
                return SangoTroopOpResult.Fail("city_not_owned",
                    "the dispatching city is unowned; diplomacy requires a force and corps.");
            }

            // 玩家门与城命令同一面:外交菜单也是 CityBaseSystem 的城菜单项。
            SangoTroopOpResult? gate = SangoPlayerTurnOps.CityCommandGate(scenario, city);
            if (gate != null)
            {
                return gate;
            }

            SangoTroopOpResult result = type switch
            {
                "alliance" => DispatchAction(scenario, city, personIds, targetForceId,
                    DiplomacyActionType.Alliance, resourceValue),
                "sendGift" => DispatchAction(scenario, city, personIds, targetForceId,
                    DiplomacyActionType.SendGift, JobType.GetJobCost((int)CityJobType.SendGift)),
                "discardAlliance" => DiscardAlliance(city, personIds, targetForceId),
                _ => SangoTroopOpResult.Fail("invalid_payload",
                    $"Unknown diplomacy command type '{type}'; expected alliance/sendGift/discardAlliance."),
            };
            if (!result.Succeeded)
            {
                return result;
            }

            SangoCommandJournal.Record(
                SangoReplayJournal.DiplomacyCommandKind,
                new SangoDiplomacyCommandArgs(type, city.Id, FirstOrZero(personIds), targetForceId, resourceValue));
            return result;
        }

        // CityDiplomacyAlliance/SendGift 的共同面:IsValid(freePersons>0 && AP>=costAP &&
        // gold>=1000)→ CreateDiplomacyAction → DispatchDiplomat。costAP 按型取表
        // (外交同盟 23 / 外交送礼 22,JobTypes.json)。
        static SangoTroopOpResult DispatchAction(
            Scenario scenario, City city, IReadOnlyList<int> personIds, int targetForceId,
            DiplomacyActionType actionType, int resourceValue)
        {
            int jobId = (int)(actionType == DiplomacyActionType.Alliance ? CityJobType.Alliance : CityJobType.SendGift);
            if (!CityDiplomacyGate(city, jobId, personIds, out string errorCode))
            {
                return SangoTroopOpResult.Fail(errorCode,
                    $"CityDiplomacy{actionType} gate rejected the order (no free persons / action points / city gold below 1000).");
            }

            Person? diplomat = city.freePersons.FirstOrDefault(candidate => candidate != null && candidate.Id == personIds[0]);
            if (diplomat == null)
            {
                return SangoTroopOpResult.Fail("person_not_free",
                    "diplomacy requires the diplomat's personId to be in the dispatching city's freePersons list.");
            }

            Force? receiver = scenario.forceSet.Get(targetForceId);
            if (receiver == null || !receiver.IsAlive || receiver == city.mBelongForce)
            {
                return SangoTroopOpResult.Fail("target_force_invalid",
                    "diplomacy requires a distinct, living target force.");
            }

            DiplomacyManager manager = GameSystem.GetSystem<DiplomacyManager>()
                ?? throw new InvalidOperationException("DiplomacyManager system is not registered in the sango kernel.");
            DiplomacyActionBase? action = manager.CreateDiplomacyAction(actionType, city.mBelongForce, receiver, diplomat, resourceValue);
            if (action == null)
            {
                return SangoTroopOpResult.Fail("action_type_inactive",
                    $"the original kernel has no DiplomacyAction implementation for {actionType}.");
            }

            if (!manager.DispatchDiplomat(action))
            {
                return SangoTroopOpResult.Fail("dispatch_failed",
                    $"DispatchDiplomat rejected the mission (receiver governor city unreachable).");
            }

            return SangoTroopOpResult.Ok($"diplomat {diplomat.Name} dispatched to {receiver.Name}.");
        }

        // CityDiplomacyDiscardAlliance.DoJob:按目标势力在己方 AllianceList 里找同盟,
        // 双方(全体成员)AllianceList 摘除。原版不改关系值、不扣金、不派使者,照抄。
        static SangoTroopOpResult DiscardAlliance(City city, IReadOnlyList<int> personIds, int targetForceId)
        {
            if (!CityDiplomacyGate(city, (int)CityJobType.DiscardAlliance, personIds, out string errorCode))
            {
                return SangoTroopOpResult.Fail(errorCode,
                    "CityDiplomacyDiscardAlliance gate rejected the order (no free persons / action points / city gold below 1000).");
            }

            Force sender = city.mBelongForce!;
            Force? target = Scenario.Cur!.forceSet.Get(targetForceId);
            if (target == null || !target.IsAlive || target == sender)
            {
                return SangoTroopOpResult.Fail("target_force_invalid",
                    "discarding an alliance requires a distinct, living target force.");
            }

            var matches = new List<Alliance>();
            foreach (Alliance? alliance in sender.AllianceList)
            {
                if (alliance != null && alliance.Contains(target))
                {
                    matches.Add(alliance);
                }
            }

            if (matches.Count == 0)
            {
                return SangoTroopOpResult.Fail("no_active_alliance",
                    $"no active alliance links {sender.Name} and {target.Name}.");
            }

            foreach (Alliance alliance in matches)
            {
                foreach (Force? member in alliance.ForceList)
                {
                    member?.AllianceList.Remove(alliance);
                }
            }

            return SangoTroopOpResult.Ok($"alliance with {target.Name} discarded.");
        }

        // 三个原版外交窗口 IsValid 的共同谓词(freePersons>0 && 军团 AP>=costAP && gold>=1000)。
        static bool CityDiplomacyGate(City city, int jobId, IReadOnlyList<int> personIds, out string errorCode)
        {
            if (personIds.Count == 0)
            {
                errorCode = "person_not_free";
                return false;
            }

            if (city.freePersons.Count == 0 ||
                city.mBelongCorps!.ActionPoint < JobType.GetJobCostAP(jobId) ||
                city.gold < 1000)
            {
                errorCode = "invalid_state";
                return false;
            }

            errorCode = string.Empty;
            return true;
        }

        static int FirstOrZero(IReadOnlyList<int> personIds) => personIds.Count > 0 ? personIds[0] : 0;
    }
}
