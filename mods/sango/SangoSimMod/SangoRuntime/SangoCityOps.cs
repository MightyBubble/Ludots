// M2.d 内政命令内核操作层:原 M1.c SangoCityCommandHandler 的门槛/执行体内核化
// (与 SangoTroopOps 同层),使命令层(Web UI payload 解析)与重放器(SangoReplayJournal)
// 共用同一实现,不再有第二份。命令到内核的映射逆向自原版 UI 窗口处理器
// (sango-src Project/Assets/Sango/Scripts/UI/City/*.cs → GameSystem City*.DoJob → City.Job*):
//   招揽(recruit) UICityRecruit.OnSure → CityRecruit.DoJob → City.JobRecruitPerson(executor, target)
//   探索(search)  UICitySearching.OnSure → CitySeraching.DoJob → City.JobSearching(persons)
//   训练(train)   UICityTrainTroops.OnSure → CityTrainTroops.DoJob → City.JobTrainTroops(persons)
//   奖励(reward)  UICityReward.OnSure → CityReward.DoJob → City.JobRewardPersons(persons)
// 下单前置条件逐条照抄各 CityXxx 系统的 IsValid 原语义(含城/军团双层 jobCounter 与
// 行动力门槛),不新增平行命令、不放宽门槛;成功路径入 SangoCommandJournal。

using System.Collections.Generic;
using Sango.Core;

namespace Sango.Runtime
{
    public static class SangoCityOps
    {
        /// <summary>
        /// 按命令型分派(train/search/reward/recruit)。执行武将名单 personIds 必须全落在
        /// 城 freePersons(原版 PersonSelectSystem 的选择列表语义);recruit 另需 targetPersonId。
        /// </summary>
        public static SangoTroopOpResult Execute(
            Scenario scenario, City city, string type, IReadOnlyList<int> personIds, int targetPersonId = 0)
        {
            ArgumentNullException.ThrowIfNull(scenario);
            ArgumentNullException.ThrowIfNull(city);
            if (string.IsNullOrEmpty(type)) throw new ArgumentNullException(nameof(type));
            ArgumentNullException.ThrowIfNull(personIds);

            if (city.mBelongForce == null || city.mBelongCorps == null)
            {
                return SangoTroopOpResult.Fail("city_not_owned",
                    "the target city is unowned; internal affairs require a force and corps.");
            }

            // M3.a 玩家门(原版 CityBaseSystem.OnCityContextMenuShow):存在玩家势力时,城
            // 命令只对"玩家势力且当前行动"的城开放;全托管世界(无玩家)保持 M1.c 的
            // 世界命令面(测试/重放同走此面)。
            SangoTroopOpResult? gate = SangoPlayerTurnOps.CityCommandGate(scenario, city);
            if (gate != null)
            {
                return gate;
            }

            SangoTroopOpResult result = type switch
            {
                "train" => Train(city, personIds),
                "search" => Search(city, personIds),
                "reward" => Reward(scenario, city, personIds),
                "recruit" => Recruit(scenario, city, personIds, targetPersonId),
                _ => SangoTroopOpResult.Fail("invalid_payload",
                    $"Unknown city command type '{type}'; expected train/search/reward/recruit."),
            };
            if (!result.Succeeded)
            {
                return result;
            }

            SangoCommandJournal.Record(
                SangoReplayJournal.CityCommandKind,
                new SangoCityCommandArgs(type, city.Id, ToArray(personIds), targetPersonId > 0 ? targetPersonId : null));
            return result;
        }

        // CityTrainTroops.IsValid:freePersons.Count>0 && CheckJobCost && morale<MaxMorale &&
        // 城 jobCounter(TrainTroops)==0 && 军团 ActionPoint>=costAP;DoJob → JobTrainTroops(全选武将)。
        static SangoTroopOpResult Train(City city, IReadOnlyList<int> personIds)
        {
            int jobId = (int)CityJobType.TrainTroops;
            if (city.freePersons.Count == 0 ||
                !city.CheckJobCost(CityJobType.TrainTroops) ||
                city.morale >= city.MaxMorale ||
                city.GetJobCounter(jobId) != 0 ||
                city.mBelongCorps!.ActionPoint < JobType.GetJobCostAP(jobId))
            {
                return SangoTroopOpResult.Fail("invalid_state",
                    "CityTrainTroops.IsValid gate rejected the order (no free persons / gold / morale cap / already trained / action points).");
            }

            Person[]? persons = ResolveFreePersons(personIds, city);
            if (persons == null || persons.Length == 0)
            {
                return SangoTroopOpResult.Fail("person_not_free",
                    "train requires personIds of persons currently in the city freePersons list.");
            }

            city.JobTrainTroops(persons);
            return SangoTroopOpResult.Ok();
        }

        // CitySeraching.IsValid:freePersons.Count>0 && ActionPoint>=costAP;DoJob → JobSearching(入 RenderEvent 队列,
        // 下一回合 Run 时 DoJobSearching 结算:发现人才/资金)。
        static SangoTroopOpResult Search(City city, IReadOnlyList<int> personIds)
        {
            int jobId = (int)CityJobType.Searching;
            if (city.freePersons.Count == 0 ||
                city.mBelongCorps!.ActionPoint < JobType.GetJobCostAP(jobId))
            {
                return SangoTroopOpResult.Fail("invalid_state",
                    "CitySeraching.IsValid gate rejected the order (no free persons / action points).");
            }

            Person[]? persons = ResolveFreePersons(personIds, city);
            if (persons == null || persons.Length == 0)
            {
                return SangoTroopOpResult.Fail("person_not_free",
                    "search requires personIds of persons currently in the city freePersons list.");
            }

            city.JobSearching(persons);
            return SangoTroopOpResult.Ok();
        }

        // CityReward.IsValid:gold>100 && CheckJobCost && 军团 jobCounter(Reward)==0 && ActionPoint>=costAP;
        // OnEnter targetList = 势力武将(非君主、无部队、忠诚<100);DoJob → JobRewardPersons(选中者,忠诚+10)。
        static SangoTroopOpResult Reward(Scenario scenario, City city, IReadOnlyList<int> personIds)
        {
            int jobId = (int)CityJobType.Reward;
            if (city.gold <= 100 ||
                !city.CheckJobCost(CityJobType.Reward) ||
                city.mBelongCorps!.GetJobCounter(jobId) != 0 ||
                city.mBelongCorps.ActionPoint < JobType.GetJobCostAP(jobId))
            {
                return SangoTroopOpResult.Fail("invalid_state",
                    "CityReward.IsValid gate rejected the order (gold / already rewarded this turn / action points).");
            }

            if (personIds.Count == 0)
            {
                return SangoTroopOpResult.Fail("invalid_payload", "reward requires personIds.");
            }

            Force force = city.mBelongForce!;
            var targets = new List<Person>(personIds.Count);
            foreach (int personId in personIds)
            {
                Person? person = scenario.personSet.Get(personId);
                if (person == null || person.mBelongForce != force ||
                    person == force.mGovernor || person.mTroop != null || person.loyalty >= 100)
                {
                    return SangoTroopOpResult.Fail("invalid_state",
                        $"person {personId} is outside the CityReward target list (own force, not governor, no troop, loyalty<100).");
                }

                targets.Add(person);
            }

            city.JobRewardPersons(targets.ToArray());
            return SangoTroopOpResult.Ok();
        }

        // CityRecruit.IsValid:freePersons.Count>0 && ActionPoint>=costAP;DoJob → JobRecruitPerson(executor, target);
        // targetList 语义(CityRecruit.OnEnter):他势力非君主非俘虏武将 + 本势力在野/俘虏。
        static SangoTroopOpResult Recruit(Scenario scenario, City city, IReadOnlyList<int> personIds, int targetPersonId)
        {
            int jobId = (int)CityJobType.RecruitPerson;
            if (city.freePersons.Count == 0 ||
                city.mBelongCorps!.ActionPoint < JobType.GetJobCostAP(jobId))
            {
                return SangoTroopOpResult.Fail("invalid_state",
                    "CityRecruit.IsValid gate rejected the order (no free persons / action points).");
            }

            if (personIds.Count != 1)
            {
                return SangoTroopOpResult.Fail("person_not_free",
                    "recruit requires exactly one executor personId from the city freePersons list.");
            }

            if (targetPersonId <= 0)
            {
                return SangoTroopOpResult.Fail("invalid_payload", "recruit requires targetPersonId.");
            }

            Person? executor = city.freePersons.FirstOrDefault(candidate => candidate != null && candidate.Id == personIds[0]);
            if (executor == null)
            {
                return SangoTroopOpResult.Fail("person_not_free",
                    "recruit executor is not in the city freePersons list.");
            }

            Person? target = scenario.personSet.Get(targetPersonId);
            if (target == null || !IsRecruitTarget(city, target))
            {
                return SangoTroopOpResult.Fail("invalid_state",
                    $"person {targetPersonId} is outside the CityRecruit target list.");
            }

            city.JobRecruitPerson(executor, target);
            return SangoTroopOpResult.Ok();
        }

        static bool IsRecruitTarget(City city, Person target)
        {
            Force? force = city.mBelongForce;
            if (force == null)
            {
                return false;
            }

            if (target.mBelongForce != force)
            {
                return target.state != (int)PersonStateType.Governor &&
                       target.state != (int)PersonStateType.Prisoner;
            }

            return target.state == (int)PersonStateType.Unemployed ||
                   target.state == (int)PersonStateType.Prisoner;
        }

        // 原版各窗口的执行武将选择列表 = TargetCity.freePersons(UICityTrainTroops/UICitySearching/
        // UICityPersonCall 的 PersonSelectSystem.Start 入参);这里要求 personIds 全部落在该列表内。
        static Person[]? ResolveFreePersons(IReadOnlyList<int> personIds, City city)
        {
            if (personIds.Count == 0)
            {
                return null;
            }

            var persons = new List<Person>(personIds.Count);
            foreach (int personId in personIds)
            {
                Person? person = city.freePersons.FirstOrDefault(candidate => candidate != null && candidate.Id == personId);
                if (person == null)
                {
                    return null;
                }

                persons.Add(person);
            }

            return persons.ToArray();
        }

        static int[] ToArray(IReadOnlyList<int> personIds)
        {
            var ids = new int[personIds.Count];
            for (int i = 0; i < personIds.Count; i++)
            {
                ids[i] = personIds[i];
            }

            return ids;
        }
    }
}
