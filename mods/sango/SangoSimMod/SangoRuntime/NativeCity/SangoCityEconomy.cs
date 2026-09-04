// D-1' 城域原生实现:经济结算 port(复合收获链/月度金钱/季度粮食/俸给/军粮)+ 事件面交换。
// 语义源(逐行对照,不发明):ClassicsCityWorking.OnCityCalculateHarvest/OnCityMonthStart/
// OnCitySeasonStart/OnCityAIPrepare、BuildingWorking.OnCityCalculateHarvest(经典模式下
// 两订阅者按订阅序先后执行,后者覆盖 totalGain*——原生实现按同序复合,不改变净效果)、
// City.OnMonthStart 的俸给段(GoldCost)、City.FoodCost(军粮)。
// 随机纪律:原生收入/收获在内核同一位调用 GameRandom.Random(v, 0.05f)——共享流、
// 同位取数,这是对拍逐位相等的根基(桥登记 #14)。
// write-through:原生结算把结果同时写内核 City PONO 字段(桥登记 #7)——内核下游
// (AI 门槛、战斗、存档捕获)读的是内核值,不 write-through 即分叉。

using System;
using Sango.Core;
using UnityEngine;

namespace Sango.Runtime
{
    /// <summary>
    /// 城经济公式 port:与内核逐位同算(同一 Mathf 桥、同一整型/浮点混算次序、同一事件触发位)。
    /// 纯公式层不持状态;结算编排(属性写入/write-through/账本计值)在 SangoCityNativeRuntime。
    /// </summary>
    public static class SangoCityFormulas
    {
        const int ClassicGainFactor = 5; // BuildingWorking.classiceGainFactor

        /// <summary>
        /// 复合收获链(ClassicsCityWorking.OnCityCalculateHarport → BuildingWorking.
        /// OnCityCalculateHarvest 按订阅序执行,后者覆盖)。产出 (totalGainFood,
        /// totalGainGold, populationIncreaseFactor);同时 write-through 内核暂态字段
        /// (内核 AI/存档捕获读这些字段,桥登记 #7)。
        /// </summary>
        public static (int TotalGainFood, int TotalGainGold, float PopulationFactor) CompositeCalculateHarvest(City city)
        {
            if (city.mBelongCorps == null)
            {
                return (city.totalGainFood, city.totalGainGold, city.population_increase_factor);
            }

            ScenarioVariables variables = Scenario.Cur.Variables;

            // --- ClassicsCityWorking.OnCityCalculateHarvest ---
            city.population_increase_factor = variables.populationIncreaseBaseFactor;
            int totalGainFood = city.BaseGainFood + city.agriculture * variables.agriculture_add_food;
            int totalGainGold = city.BaseGainGold + city.commerce * variables.commerce_add_gold;
            if (variables.populationEnable)
            {
                totalGainGold += (int)(city.population * variables.populationGoldIncomeFactor);
                totalGainFood += (int)(city.population * variables.populationFoodCostFactor * 0.5f);
            }

            // SangoObjectList.ForEach 为倒序遍历(M3.a 在案);原生实现同向倒序,事件触发序逐位一致。
            for (int i = city.allBuildings.Count - 1; i >= 0; i--)
            {
                Sango.Core.Building building = city.allBuildings[i];
                if (building != null && building.isComplate)
                {
                    Sango.Core.Tools.OverrideData<int> overrideData = Sango.Core.Tools.OverrideData<int>.Create(building.BuildingType.foodGain);
                    GameEvent.OnBuildingCalculateFoodGain?.Invoke(building, overrideData);
                    totalGainFood += overrideData.ValueAndRecycle;

                    overrideData = Sango.Core.Tools.OverrideData<int>.Create(building.BuildingType.goldGain);
                    GameEvent.OnBuildingCalculateGoldGain?.Invoke(building, overrideData);
                    totalGainGold += overrideData.ValueAndRecycle;

                    overrideData = Sango.Core.Tools.OverrideData<int>.Create(building.BuildingType.populationGain);
                    GameEvent.OnBuildingCalculatePopulationGain?.Invoke(building, overrideData);
                    city.population_increase_factor += overrideData.ValueAndRecycle;
                }
            }

            (float foodLeft, float goldLeft) = InfluenceFactors(city, variables);
            totalGainFood = Mathf.CeilToInt(totalGainFood * foodLeft * (FoodFactor(city, variables) + city.extraGainFoodFactor));
            totalGainGold = Mathf.CeilToInt(totalGainGold * goldLeft * (GoldFactor(city, variables) + city.extraGainGoldFactor));

            Sango.Core.Tools.OverrideData<int> harvestData = Sango.Core.Tools.OverrideData<int>.Create(totalGainFood);
            GameEvent.OnCityCalculateFoodHarvest?.Invoke(city, harvestData);
            GameEvent.OnCityCalculateFoodHarvestAfter?.Invoke(city, harvestData);
            totalGainFood = harvestData.Value;

            harvestData.Value = totalGainGold;
            GameEvent.OnCityCalculateGoldHarvest?.Invoke(city, harvestData);
            GameEvent.OnCityCalculateGoldHarvestAfter?.Invoke(city, harvestData);
            totalGainGold = harvestData.Value;

            float populationFactor = city.population_increase_factor * city.extraPopulationFactor;

            // --- BuildingWorking.OnCityCalculateHarvest(经典模式:无内政工作建筑,
            // 工作段为空转,覆盖段照算)---
            int leaderFactor = LeaderInfuse(city, (int)AttributeType.Politics);
            int workingFoodValue = 0;
            int workingGoldValue = 0;
            for (int i = city.allBuildings.Count - 1; i >= 0; i--)
            {
                Sango.Core.Building building = city.allBuildings[i];
                if (building == null || !building.isComplate || !building.IsIntorBuilding())
                {
                    continue;
                }

                Sango.Core.BuildingType buildingType = building.BuildingType;
                if (buildingType.foodGain > 0 || buildingType.goldGain > 0)
                {
                    var workers = new System.Collections.Generic.List<Sango.Core.Person>(buildingType.workerLimit);
                    if (building.Workers != null)
                    {
                        for (int slot = 0; slot < buildingType.workerLimit; slot++)
                        {
                            Sango.Core.Person? person = slot < building.Workers.Count ? building.Workers.Get(slot) : null;
                            if (person != null && SangoPersonReadFace.IsFree(person) && !SangoPersonReadFace.ActionOver(person))
                            {
                                workers.Add(person);
                            }
                        }
                    }

                    int personFactor = PersonInfuse(workers.ToArray(), buildingType.effectAttrType);
                    int totalFactor = leaderFactor * personFactor;
                    GameUtility.InitJobFeature(building.Workers, city, building);

                    if (buildingType.foodGain > 0)
                    {
                        // 内核为整型算术(foodGain / 5 * totalFactor / 10000),原生同型不换浮点。
                        int value = (int)((buildingType.foodGain / ClassicGainFactor * totalFactor / 10000) * foodLeft * (FoodFactor(city, variables) + city.extraGainFoodFactor));
                        Sango.Core.Tools.OverrideData<int> overrideData = Sango.Core.Tools.OverrideData<int>.Create(value);
                        GameEvent.OnBuildingCalculateFoodGain?.Invoke(building, overrideData);
                        workingFoodValue += overrideData.ValueAndRecycle;
                    }

                    if (buildingType.goldGain > 0)
                    {
                        int value = (int)((buildingType.goldGain / ClassicGainFactor * totalFactor / 10000) * goldLeft * (GoldFactor(city, variables) + city.extraGainGoldFactor));
                        Sango.Core.Tools.OverrideData<int> overrideData = Sango.Core.Tools.OverrideData<int>.Create(value);
                        GameEvent.OnBuildingCalculateGoldGain?.Invoke(building, overrideData);
                        workingGoldValue += overrideData.ValueAndRecycle;
                    }

                    GameUtility.ClearJobFeature();
                }
            }

            totalGainFood = workingFoodValue * 9 + city.BaseGainFood * leaderFactor / 100;
            harvestData.Value = totalGainFood;
            GameEvent.OnCityCalculateFoodHarvest?.Invoke(city, harvestData);
            GameEvent.OnCityCalculateFoodHarvestAfter?.Invoke(city, harvestData);
            totalGainFood = harvestData.Value;

            totalGainGold = workingGoldValue * 3 + city.BaseGainGold * leaderFactor / 100;
            harvestData.Value = totalGainGold;
            GameEvent.OnCityCalculateGoldHarvest?.Invoke(city, harvestData);
            GameEvent.OnCityCalculateGoldHarvestAfter?.Invoke(city, harvestData);
            totalGainGold = harvestData.Value;

            // write-through:内核暂态字段(桥登记 #7)。
            city.totalGainFood = totalGainFood;
            city.totalGainGold = totalGainGold;
            city.population_increase_factor = populationFactor;
            return (totalGainFood, totalGainGold, populationFactor);
        }

        /// <summary>
        /// 月度金钱收入(ClassicsCityWorking.OnCityMonthStart):同位取随机 + 收入覆盖事件。
        /// 返回 (应用值, 取随机原值)——城附属 Action 的覆盖事件会改写应用值,取随机原值
        /// 独立返回供账本带断言。
        /// </summary>
        public static (int Applied, int Drawn) MonthlyIncomeGold(City city, int totalGainGold)
        {
            int drawn = GameRandom.Random(totalGainGold, 0.05f);
            Sango.Core.Tools.OverrideData<int> overrideData = Sango.Core.Tools.OverrideData<int>.Create(drawn);
            GameEvent.OnCityGainGoldHarvest?.Invoke(city, overrideData);
            return (overrideData.ValueAndRecycle, drawn);
        }

        /// <summary>季度粮食收入(ClassicsCityWorking.OnCitySeasonStart):同位取随机 + 收获覆盖事件(同月金口径)。</summary>
        public static (int Applied, int Drawn) SeasonalHarvestFood(City city, int totalGainFood)
        {
            int drawn = GameRandom.Random(totalGainFood, 0.05f);
            Sango.Core.Tools.OverrideData<int> overrideData = Sango.Core.Tools.OverrideData<int>.Create(drawn);
            GameEvent.OnCityGainFoodHarvest?.Invoke(city, overrideData);
            return (overrideData.ValueAndRecycle, drawn);
        }

        /// <summary>
        /// 俸给(City.GoldCost):官员俸 + 俘虏羁押费,纯确定性(无随机)。
        /// D-2' 消桥 #1/#2:原生武将运行时挂载时读组件源(驻城武将 OfficialCost +
        /// 城俘羁押,读缝同步);未挂载(城原生单挂的对拍面)保持内核 PONO 源。
        /// </summary>
        public static int SalaryGoldCost(City city)
        {
            if (SangoPersonNativeRuntime.Active is { IsDisposed: false, IsCurrentKernel: true } persons)
            {
                return persons.SalaryGoldCostFromComponents(city);
            }

            int goldCost = 0;
            city.allPersons.ForEach(person =>
            {
                if (person != null && person.Official != null)
                {
                    goldCost += person.Official.cost;
                }
            });

            for (int i = 0; i < city.captiveList.Count; i++)
            {
                if (city.captiveList[i] != null)
                {
                    goldCost += 100;
                }
            }

            return goldCost;
        }

        /// <summary>军粮(City.FoodCost):军队 + 人口口粮;覆盖事件照内核同位触发(桥登记 #10)。</summary>
        public static int MilitaryFoodCost(City city)
        {
            ScenarioVariables variables = Scenario.Cur.Variables;
            int foodCost = (int)Math.Ceiling(variables.baseFoodCostInCity * (city.troops + city.woundedTroops));
            if (variables.populationEnable)
            {
                foodCost += (int)Math.Ceiling(city.population * variables.populationFoodCostFactor);
            }

            Sango.Core.Tools.OverrideData<int> overrideData = Sango.Core.Tools.OverrideData<int>.Create(foodCost);
            GameEvent.OnCityCalculateFoodCost?.Invoke(city, Scenario.Cur, overrideData);
            return overrideData.ValueAndRecycle;
        }

        /// <summary>太守影响因子(BuildingWorking.GetCityLeaderInfuse;五维读面 D-2' 消桥 #2)。</summary>
        public static int LeaderInfuse(City city, int effectAttrType)
        {
            Sango.Core.Person? leader = city.Leader;
            int leaderAttrValue = leader == null ? 0 : SangoPersonReadFace.GetAttribute(leader, effectAttrType);
            return 100 + (int)((Mathf.Pow(Mathf.Max(40, leaderAttrValue), 1.5f) / 10 - 25) / 3f);
        }

        /// <summary>工作武将影响因子(BuildingWorking.GetPersonInfuse;五维读面 D-2' 消桥 #2)。</summary>
        public static int PersonInfuse(Sango.Core.Person[]? workers, int effectAttrType)
        {
            int personFactor = 100;
            if (workers == null)
            {
                return personFactor;
            }

            for (int i = 0; i < workers.Length; i++)
            {
                Sango.Core.Person? person = workers[i];
                if (person != null)
                {
                    personFactor += (int)(Mathf.Pow(Mathf.Max(40, SangoPersonReadFace.GetAttribute(person, effectAttrType)), 0.5f) * 100 / (8 * (i + 1)));
                }
            }

            return personFactor;
        }

        static (float FoodLeft, float GoldLeft) InfluenceFactors(City city, ScenarioVariables variables)
        {
            float securityInfluence = ((city.security / variables.securityInfluenceMax) - 1) * variables.securityInfluence;
            float popularSupportInfluence = variables.populationEnable
                ? ((city.popularSupport / variables.popularSupportInfluenceMax) - 1) * variables.popularSupportInfluence
                : 0f;
            float leftInfluence = 1.0f + securityInfluence + popularSupportInfluence;
            return (leftInfluence, leftInfluence);
        }

        static float FoodFactor(City city, ScenarioVariables variables) =>
            city.IsPlayer ? variables.playerFoodFactor : variables.foodFactor;

        static float GoldFactor(City city, ScenarioVariables variables) =>
            city.IsPlayer ? variables.playerGoldFactor : variables.goldFactor;
    }
}
