// D-1' 城域原生实现:四型内政令(train/search/reward/recruit)的 job 结算 port。
// 语义源(逐行对照,不发明):City.JobTrainTroops/JobSearching/JobRewardPersons/
// JobRecruitPerson(内核 Game/Object/City/City.cs)。门槛不在本文件——SangoCityOps 的
// 门槛段(逆向自原版 CityXxx 系统 IsValid)双路共用;本文件只做"过门槛后的结算体"。
// 写入面:城 PONO 字段(桥登记 #7/#9)+ 武将/军团对象面(桥登记 #3/#4/#11)+
// 演出事件队列(搜索/登庸的次回合结算排程,桥登记 #11)。组件同步由
// SangoCityNativeRuntime.ExecuteCommand 在结算后统一落账。
// 随机纪律:本四型结算体无随机消耗(train 的士气公式/奖励的忠诚+10 均确定性;
// 搜索/登庸的随机在其演出事件次回合 Enter 时由内核消耗——该面本波内核保留)。

using System.Collections.Generic;
using Sango.Core;
using Sango.Render;

namespace Sango.Runtime
{
    /// <summary>四型内政令的原生结算体(返回语义与内核 Job* 同形)。</summary>
    public static class SangoCityJobOps
    {
        /// <summary>训练(City.JobTrainTroops 非测试路径):士气按统率公式提升,扣金/AP,城级 jobCounter+1。</summary>
        public static int TrainTroops(City city, Person[] personList)
        {
            if (!GameUtility.IsValidPersonArray(personList) || city.morale >= city.MaxMorale)
            {
                return 0;
            }

            int jobId = (int)CityJobType.TrainTroops;
            GameUtility.InitJobFeature(personList, city);

            int goldNeed = JobType.GetJobCost(jobId);
            Sango.Core.Tools.OverrideData<int> overrideData = Sango.Core.Tools.OverrideData<int>.Create(goldNeed);
            GameEvent.OnCityCheckJobCost?.Invoke(city, jobId, personList, overrideData);
            goldNeed = overrideData.ValueAndRecycle;

            if (city.gold < goldNeed)
            {
                GameUtility.ClearJobFeature();
                return 0;
            }

            int totalValue = 0;
            int subValue = 0;
            int maxValue = -99;
            Person? maxPerson = null;
            for (int i = 0; i < personList.Length; i++)
            {
                Person? person = personList[i];
                if (person == null)
                {
                    continue;
                }

                if (person.BaseTrainTroopAbility > maxValue)
                {
                    maxPerson = person;
                    maxValue = person.BaseTrainTroopAbility;
                }
            }

            for (int i = 0; i < personList.Length; i++)
            {
                Person? person = personList[i];
                if (person == null)
                {
                    continue;
                }

                if (person != maxPerson)
                {
                    subValue += maxPerson!.BaseTrainTroopAbility;
                }
                else
                {
                    totalValue += maxPerson.BaseTrainTroopAbility;
                }
            }

            totalValue = GameUtility.Method_TrainTroops(totalValue, subValue);
            overrideData = Sango.Core.Tools.OverrideData<int>.Create(totalValue);
            GameEvent.OnCityJobResult?.Invoke(city, jobId, personList, overrideData);
            totalValue = overrideData.ValueAndRecycle;

            int meritGain = JobType.GetJobMeritGain(jobId);
            int techniquePointGain = JobType.GetJobTPGain(jobId);
            for (int i = 0; i < personList.Length; i++)
            {
                Person? person = personList[i];
                if (person == null)
                {
                    continue;
                }

                person.merit += meritGain;
                person.GainExp(meritGain);
                city.freePersons.Remove(person);
                person.ActionOver = true;
            }

            overrideData = Sango.Core.Tools.OverrideData<int>.Create(techniquePointGain);
            GameEvent.OnCityJobGainTechniquePoint?.Invoke(city, jobId, personList, overrideData);
            techniquePointGain = overrideData.ValueAndRecycle;
            city.mBelongForce!.GainTechniquePoint(techniquePointGain);

            city.gold -= goldNeed;
            city.morale += totalValue;
            city.mBelongCorps!.ReduceActionPoint(JobType.GetJobCostAP(jobId));
            if (city.morale > city.MaxMorale)
            {
                city.morale = city.MaxMorale;
            }

            AddCityJobCounter(city, jobId);
            GameUtility.ClearJobFeature();
            return totalValue;
        }

        /// <summary>褒奖(City.JobRewardPersons):每人忠诚+10,扣金/AP,军团级 jobCounter+1。</summary>
        public static bool RewardPersons(City city, Person[] persons)
        {
            if (!GameUtility.IsValidPersonArray(persons))
            {
                return true;
            }

            int jobId = (int)CityJobType.Reward;
            int apCost = JobType.GetJobCostAP(jobId);
            int goldCost = JobType.GetJobCost(jobId);
            int totalApCost = 0;
            int totalGoldCost = 0;
            for (int i = 0; i < persons.Length; ++i)
            {
                Person? person = persons[i];
                if (person == null)
                {
                    continue;
                }

                totalApCost += apCost;
                totalGoldCost += goldCost;
                person.loyalty += 10;
            }

            city.gold -= totalGoldCost;
            city.mBelongCorps!.ReduceActionPoint(totalApCost);
            city.mBelongCorps.AddJobCounter(jobId);
            return true;
        }

        /// <summary>登庸(City.JobRecruitPerson):同城走演出事件,异地走 PersonRecruitPerson 任务。</summary>
        public static bool RecruitPerson(City city, Person executor, Person dest)
        {
            int jobId = (int)CityJobType.RecruitPerson;
            int apCost = JobType.GetJobCostAP(jobId);

            city.freePersons.Remove(executor);
            if (dest.mCurrentCity == executor.mCurrentCity)
            {
                CityRecruitPersonEvent recruitEvent = RenderEvent.Instance.Create<CityRecruitPersonEvent>();
                recruitEvent.Init(executor, dest);
                RenderEvent.Instance.Add(recruitEvent);
                city.mBelongCorps!.ReduceActionPoint(apCost);
                return true;
            }

            executor.SetMission(MissionType.PersonRecruitPerson, dest, 100, dest.mCurrentCity!.Id);
            executor.ActionOver = true;
            city.mBelongCorps!.ReduceActionPoint(apCost);
            return false;
        }

        /// <summary>探索(City.JobSearching):按人入列搜索演出事件(次回合 Enter 结算,内核保留面)。</summary>
        public static bool Searching(City city, Person[] personList)
        {
            if (!GameUtility.IsValidPersonArray(personList))
            {
                return false;
            }

            for (int i = 0; i < personList.Length; i++)
            {
                Person? person = personList[i];
                if (person == null)
                {
                    continue;
                }

                CityPersonSearchingEvent searchEvent = RenderEvent.Instance.Create<CityPersonSearchingEvent>();
                searchEvent.Init(city, person);
                RenderEvent.Instance.Add(searchEvent);
            }

            return true;
        }

        /// <summary>城级 jobCounter 写入(City.AddJobCounter 的字典语义 port,桥登记 #9)。</summary>
        public static void AddCityJobCounter(City city, int jobId)
        {
            city.jobCounter[jobId] = city.jobCounter.TryGetValue(jobId, out int count) ? count + 1 : 1;
        }
    }
}
