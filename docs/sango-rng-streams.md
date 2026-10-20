# sango 随机流拓扑(T2.1 / #1843 / #1847)

基线:`feat/sango-port-m0` 主线(阶段1 后)。消费点清点口径:全 SangoSimMod 内
`GameRandom.` 活调用(剔除 GameRandom.cs 自身、Unity shim 别名行、纯注释行),
共 149 处(审计口径 163 含注释与别名,活调用面见下表)。

## 流划分(默认拓扑,待评审)

| 流 | 用途 | 消费点数 |
|---|---|---|
| `sango.combat` | 战斗解算/单挑/技能/战步 | 42 |
| `sango.economy` | 城内政/建筑生产/势力月结 | 31 |
| `sango.economy` | 势力内政/外交事件 | 3 |
| `sango.ai` | 委任与 AI 决策 | 37 |
| `sango.turn` | 回合框架/剧本事件/开局生成 | 24 |
| `sango.turn` | 演出;T5.1 解耦后评估摘除 | 3 |
| `sango.turn` | 泛型权重工具,随首用方细分时再定 | 5 |
| `` | 生命周期/快照,非消费 | 4 |

## 种子来源

- 每流 seed = `RngSeed`(FNV-1a)对 `<世界种子>:<流名>` 派生后由 `GameRandom.Init(seed)`
  显式 `DeclareStream`——世界种子是唯一熵源,流名只做派生盐(平台流 seed 语义见
  gitbook/architecture/deterministic-rng.md;平台无世界种子派生口,由 mod 侧
  显式声明,不改平台)。
- 快照:平台 `RngStreamSnapshot` 进 sango.sim 域 `random` 节(T2.3)。

## 推进步骤(T2.2/T2.3)

1. **单流阶段**:全部消费点先落 `sango.turn` 单流保序(API 面不变,序列必然变化,
   golden 一次重建——合法断点)。
2. **逐域切换**:combat → economy → ai 各一 PR,每域切换单独对拍重建 golden,
   禁止两域同 PR。
3. 泛型工具(WeightList 等)与混合文件(BuildingWorking 兼产线/委任)在域切换时
   按调用点显式归流,归不进四流的留 turn。

## 消费点清单(grep 一一对应)

### sango.combat(42 处)

- `Game/Action/Action/ForceOnly/TroopStealFood.cs` — `int stealNum = System.Math.Min(troop.troops / 2, troop.Attack * GameRa`
- `Game/Action/Action/Troop/TroopAddBuff.cs` — `if (!GameRandom.Chance(probability, 10000))`
- `Game/Action/Action/Troop/TroopAddBuff.cs` — `int index = GameRandom.RandomWeightIndex(weight);`
- `Game/Object/Duel/DuelSystem.cs` — `bool attackerFirst = GameRandom.Chance(50, 100);`
- `Game/Object/Duel/DuelSystem.cs` — `bool isCritical = GameRandom.Chance(criticalRate, 100);`
- `Game/Object/Duel/DuelSystem.cs` — `bool isHit = GameRandom.Chance(attackSuccessRate, 100);`
- `Game/Object/Duel/DuelSystem.cs` — `if (GameRandom.Chance(v.duelCaptureChance, 100))`
- `Game/Object/Duel/DuelSystem.cs` — `if (GameRandom.Chance(v.duelCaptureChance, 100))`
- `Game/Object/Duel/DuelSystem.cs` — `if (GameRandom.Chance(v.duelTroopDestroyChance, 100))`
- `Game/Object/Duel/DuelSystem.cs` — `if (GameRandom.Chance(v.duelTroopDestroyChance, 100))`
- `Game/Object/Skill/Effect/AddBuff.cs` — `if (!GameRandom.Chance(probability, 10000))`
- `Game/Object/Skill/Effect/AddBuff.cs` — `int index = GameRandom.RandomWeightIndex(weight);`
- `Game/Object/Skill/Effect/PutoutFire.cs` — `if (!GameRandom.Chance(probability, 10000))`
- `Game/Object/Skill/Effect/RecureWoundedTroops.cs` — `if (!GameRandom.Chance(probability, 10000))`
- `Game/Object/Skill/Effect/RecureWoundedTroops.cs` — `int index = GameRandom.RandomWeightIndex(weight);`
- `Game/Object/Skill/Effect/RemoveBuffById.cs` — `if (!GameRandom.Chance(probability, 10000))`
- `Game/Object/Skill/Effect/RemoveBuffByKind.cs` — `if (!GameRandom.Chance(probability, 10000))`
- `Game/Object/Skill/Effect/SetFire.cs` — `if (!GameRandom.Chance(probability, 10000))`
- `Game/Object/Skill/Effect/SetFire.cs` — `int index = GameRandom.RandomWeightIndex(weight);`
- `Game/Object/Skill/SkillInstance.cs` — `bool b_suc = GameRandom.Chance(baseSuccessRate);`
- `Game/Object/Skill/SkillInstance.cs` — `if (GameRandom.Chance(basCriticalRate))`
- `Game/Object/Skill/SkillInstance.cs` — `return skill.personSound[GameRandom.Range(0, skill.personSound.Length)`
- `Game/Object/Troop/Troop.cs` — `GameMedia.Instance.PlayPersonSay(Leader, GameRandom.Chance(50) ? 3216 `
- `Game/Object/Troop/Troop.cs` — `if (GameRandom.Chance(50))`
- `Game/Object/Troop/Troop.cs` — `if (GameRandom.Chance(80))`
- `Game/Object/Troop/Troop.cs` — `if (GameRandom.Chance(GameFormula.Instance.PersonEscapeProbablility_In`
- `Game/Object/Troop/Troop.cs` — `if (GameRandom.Chance(p))`
- `Game/Object/Troop/Troop.cs` — `if (GameRandom.Chance(p))`
- `Game/Object/Troop/Troop.cs` — `if (GameRandom.Chance(p))`
- `Game/Object/Troop/TroopOccupyCity.cs` — `if (IsMissionComplete || (!troop.IsPlayerControl && troop.IsWithOutFoo`
- `Game/Object/Troop/TroopProtectCity.cs` — `if (IsMissionComplete || (TargetCity.troops < 2000 && TargetCity == Tr`
- `Game/Object/Troop/TroopProtectCity.cs` — `socer += distance * GameRandom.Random(500, 1000);`
- `SangoRuntime/NativeCombat/SangoCombatSteps.cs` — `GameMedia.Instance.PlayPersonSay(target.Leader, GameRandom.Chance(50) `
- `SangoRuntime/NativeCombat/SangoCombatSteps.cs` — `if (GameRandom.Chance(50))`
- `SangoRuntime/NativeCombat/SangoCombatSteps.cs` — `if (GameRandom.Chance(80))`
- `SangoRuntime/NativeCombat/SangoCombatSteps.cs` — `if (GameRandom.Chance(p))`
- `SangoRuntime/NativeCombat/SangoCombatSteps.cs` — `if (GameRandom.Chance(p))`
- `SangoRuntime/NativeCombat/SangoCombatSteps.cs` — `if (GameRandom.Chance(p))`
- `SangoRuntime/NativeCombat/SangoCombatSteps.cs` — `int randomIndex = GameRandom.Range(0, availableCells.Count);`
- `SangoRuntime/NativeCombat/SangoCombatSteps.cs` — `int randomIndex = GameRandom.Range(0, availableCells.Count);`
- `SangoRuntime/SangoChallengeOps.cs` — `debate.UseSkill(current, GameRandom.Range(0, current.Skills.Count));`
- `SangoRuntime/SangoChallengeOps.cs` — `if (!GameRandom.Chance(ChallengeChancePercent))`

### sango.economy(31 处)

- `Game/Object/City/City.cs` — `agriculture = agriculture * (GameRandom.RandomWeightIndex(scenarioVari`
- `Game/Object/City/City.cs` — `building.rot = GameRandom.Range(0, 10) * 90 * Mathf.Deg2Rad;`
- `Game/Object/City/City.cs` — `building.rot = GameRandom.Range(0, 10) * 90 * Mathf.Deg2Rad;`
- `Game/Object/City/City.cs` — `commerce = agriculture * (GameRandom.RandomWeightIndex(scenarioVariabl`
- `Game/Object/City/City.cs` — `food = food * (GameRandom.RandomWeightIndex(scenarioVariables.cityFall`
- `Game/Object/City/City.cs` — `gold = gold * (GameRandom.RandomWeightIndex(scenarioVariables.cityFall`
- `Game/Object/City/City.cs` — `if (!person.ActionOver && GameRandom.Chance(probality))`
- `Game/Object/City/City.cs` — `if (GameRandom.Chance(GameFormula.Instance.PersonEscapeProbablility_In`
- `Game/Object/City/City.cs` — `if (GameRandom.Chance(probality))`
- `Game/Object/City/City.cs` — `if (building.isComplate && GameRandom.Chance(30))`
- `Game/Object/City/City.cs` — `if (person.mTroop == null && person.mCurrentCity == this && person != `
- `Game/Object/City/City.cs` — `int findGold = GameRandom.Range(person.Politics * 2, person.Politics *`
- `Game/Object/City/City.cs` — `itemStore.Split((100 - (GameRandom.RandomWeightIndex(scenarioVariables`
- `Game/Object/City/City.cs` — `person.loyalty += GameRandom.Range(7, 18);`
- `Game/Object/City/City.cs` — `pop = GameRandom.Random((int)(population * population_increase_factor `
- `Game/Object/City/City.cs` — `return NeighborList[GameRandom.Range(0, NeighborList.Count)];`
- `Game/Object/City/City.cs` — `target = invisiblePersons[GameRandom.Range(0, invisiblePersons.Count)]`
- `Game/Object/City/City.cs` — `troops = troops * (GameRandom.RandomWeightIndex(scenarioVariables.city`
- `Game/System/BuildingWorking/BuildingWorking.cs` — `city.AddMorale(GameRandom.Range(building.AccumulatedProduct, building.`
- `Game/System/BuildingWorking/BuildingWorking.cs` — `city.AddSecurity(GameRandom.Range(building.AccumulatedProduct / 2, bui`
- `Game/System/BuildingWorking/BuildingWorking.cs` — `if (GameRandom.Chance(60))`
- `Game/System/BuildingWorking/BuildingWorking.cs` — `if (GameRandom.Chance(80))`
- `Game/System/BuildingWorking/BuildingWorking.cs` — `int s = -GameRandom.Range(1, 3);`
- `Game/System/BuildingWorking/BuildingWorking.cs` — `int targetIndex = GameRandom.RandomWeightIndex(levelTotal);`
- `Game/System/BuildingWorking/BuildingWorking.cs` — `int targetIndex = GameRandom.RandomWeightIndex(levelTotal);`
- `Game/System/BuildingWorking/BuildingWorking.cs` — `x.AddMorale(GameRandom.Range(building.AccumulatedProduct / 2, building`
- `Game/System/BuildingWorking/BuildingWorking.cs` — `x.AddMorale(GameRandom.Range(building.AccumulatedProduct / 2, building`
- `Game/System/City/CityUpgradeOfficial.cs` — `Official dst = match_officials[GameRandom.Range(match_officials.Count)`
- `Game/System/City/CityUpgradeOfficial.cs` — `words = says[GameRandom.Range(says.Length)]`
- `SangoRuntime/NativeCity/SangoCityEconomy.cs` — `int drawn = GameRandom.Random(totalGainFood, 0.05f);`
- `SangoRuntime/NativeCity/SangoCityEconomy.cs` — `int drawn = GameRandom.Random(totalGainGold, 0.05f);`

### sango.economy(势力内政/外交事件)(3 处)

- `Game/Object/Force/Force.cs` — `if (GameRandom.Chance(overrideData.ValueAndRecycle))`
- `Game/Object/Force/Force.cs` — `if (GameRandom.Chance(totalProbability, 10000))`
- `Game/Object/Force/Force.cs` — `int v = GameRandom.RandomWeightIndex(loyaltyWeight, 9);`

### sango.ai(37 处)

- `Game/Object/City/CityAI.cs` — `buildingType = scenario.GetObject<BuildingType>(GameRandom.Range((int)`
- `Game/Object/City/CityAI.cs` — `if ((city.invisiblePersons.Count > 0 && city.freePersons.Count > 0 && `
- `Game/Object/City/CityAI.cs` — `if ((targetCity.mBelongForce != null && city.AttackTroopsCount < GameR`
- `Game/Object/City/CityAI.cs` — `if (GameRandom.Chance((100 - city.security) * 4))`
- `Game/Object/City/CityAI.cs` — `if (GameRandom.Chance((95 - city.morale) * 3 / 2))`
- `Game/Object/City/CityAI.cs` — `if (GameRandom.Chance(50))`
- `Game/Object/City/CityAI.cs` — `if (GameRandom.Chance(50))`
- `Game/Object/City/CityAI.cs` — `if (GameRandom.Chance(50))`
- `Game/Object/City/CityAI.cs` — `if (GameRandom.Chance(60))`
- `Game/Object/City/CityAI.cs` — `if (GameRandom.Chance(60))`
- `Game/Object/City/CityAI.cs` — `if (GameRandom.Chance(80))`
- `Game/Object/City/CityAI.cs` — `if (GameRandom.Chance(80))`
- `Game/Object/City/CityAI.cs` — `if (GameRandom.Chance(attackChance))`
- `Game/Object/City/CityAI.cs` — `if (GameRandom.Chance(priority, 10000))`
- `Game/Object/City/CityAI.cs` — `if (city.allAttackTroops.FindAll(x => x.LandTroopType.IsMachine()).Cou`
- `Game/Object/City/CityAI.cs` — `int count = GameRandom.Range(0, UnityEngine.Mathf.Max(0, priorityQueue`
- `Game/Object/City/CityAI.cs` — `int randomType = buildTypes[GameRandom.RandomWeightIndex(build_weight,`
- `Game/Object/City/CityAI.cs` — `int targetItemId = validItem[GameRandom.RandomWeightIndex(pr)];`
- `Game/Object/City/CityAI.cs` — `spType = costEnoughTroopTypes[GameRandom.Range(0, costEnoughTroopTypes`
- `Game/Object/Force/ForceAI.cs` — `Official dst = match_officials[GameRandom.Range(match_officials.Count)`
- `Game/Object/Force/ForceAI.cs` — `Person captive = force.BeCaptiveList[GameRandom.Range(force.BeCaptiveL`
- `Game/Object/Force/ForceAI.cs` — `float random = GameRandom.Range(0, totalWeight);`
- `Game/Object/Force/ForceAI.cs` — `if (!GameRandom.Chance(30)) return true;`
- `Game/Object/Force/ForceAI.cs` — `if (GameRandom.Chance(25))`
- `Game/Object/Force/ForceAI.cs` — `if (GameRandom.Chance(chance))`
- `Game/Object/Force/ForceAI.cs` — `if (GameRandom.Chance(chance))`
- `Game/Object/Force/ForceAI.cs` — `if (GameRandom.Chance(chance))`
- `Game/Object/Force/ForceAI.cs` — `if (GameRandom.Chance(chance))`
- `Game/Object/Force/ForceAI.cs` — `if (GameRandom.Chance(chance))`
- `Game/Object/Force/ForceAI.cs` — `if (GameRandom.Chance(chance))`
- `Game/Object/Force/ForceAI.cs` — `if (GameRandom.Chance(chance))`
- `Game/Object/Force/ForceAI.cs` — `if (GameRandom.Chance(chance))`
- `Game/Object/Force/ForceAI.cs` — `if (GameRandom.Chance(chance, 100))`
- `Game/Object/Force/ForceAI.cs` — `if (GameRandom.Chance(probability, 10000))`
- `Game/Object/Force/ForceAI.cs` — `if (GameRandom.Chance(ransomChance))`
- `Game/Object/Force/ForceAI.cs` — `if (GameRandom.Chance(releaseChance))`
- `Game/Object/Force/ForceAI.cs` — `if (neighbor.IsPlayer && GameRandom.Chance(95)) continue;`

### sango.turn(24 处)

- `Game/Condition/Core/ProbabilityCheck.cs` — `return GameRandom.Range(0, 10000) <= probability;`
- `Game/GameFormula.cs` — `n += GameRandom.Range(0, Math.Max(0, variables.recruitRandomMax - argu`
- `Game/Map/Cell.cs` — `interiorModel.transform.rotation = Quaternion.Euler(new Vector3(0, Gam`
- `Game/Object/Core/SangoObjectOffSet.cs` — `int index = GameRandom.Range(0, list.Count);`
- `Game/Object/Core/SangoObjectSet.cs` — `int index = GameRandom.Range(0, list.Count);`
- `Game/Object/Person/Person.cs` — `bool success = GameRandom.Chance(probability);`
- `Game/Object/Person/Person.cs` — `if (GameRandom.Chance(10))`
- `Game/Object/Person/Person.cs` — `if (stayTurnCount > 5 && GameRandom.Chance(5)) // 10%概率`
- `Game/Object/Person/Person.cs` — `int randomIndex = GameRandom.Range(neighborCities.Count);`
- `Game/Object/Province.cs` — `return cities[GameRandom.Range(0, cities.Count)];`
- `Game/Object/TerrainType.cs` — `int num = (halfDeposit > 0 ? GameRandom.Range(halfDeposit) : 0) + ((fo`
- `Game/Object/TerrainType.cs` — `return GameRandom.Range(fertility[0], fertility[1]);`
- `Game/Object/TerrainType.cs` — `return GameRandom.Range(prosperity[0], prosperity[1]);`
- `Game/Scenario/Scenario.cs` — `GameRandom.Init();`
- `Game/Scenario/Scenario.cs` — `GameRandom.Init();`
- `Game/System/BuildingWorking/ClassicsCityWorking.cs` — `int harvest = GameRandom.Random(city.totalGainFood, 0.05f);`
- `Game/System/BuildingWorking/ClassicsCityWorking.cs` — `int inComingGold = GameRandom.Random(city.totalGainGold, 0.05f);`
- `Game/System/Diplomacy/Actions/DiplomacyActionAlliance.cs` — `if (!GameRandom.Chance(SuccessRate))`
- `Game/System/Diplomacy/Actions/DiplomacyActionTruce.cs` — `if (!GameRandom.Chance(SuccessRate))`
- `Game/System/Diplomacy/DiplomacyEventManager.cs` — `if (GameRandom.Chance(e.Probability))`
- `Game/System/Diplomacy/DiplomacyManager.cs` — `if (GameRandom.Chance(chance))`
- `Game/System/Diplomacy/DiplomacyManager.cs` — `if (GameRandom.Chance(chance))`
- `Game/System/Diplomacy/DiplomacyManager.cs` — `return GameRandom.Chance(action.SuccessRate);`
- `Game/System/Person/PersonRecruit.cs` — `GameDialog.Instance.Open(GameDialog.DialogStyle.ClickPersonSay, talk[G`

### sango.turn(演出;T5.1 解耦后评估摘除)(3 处)

- `Game/Render/RenderEvent/Events/DiplomacyEvent.cs` — `bool success = GameRandom.Chance(successRate);`
- `Game/Render/RenderEvent/Events/TroopBuildBuildingEvent.cs` — `GameMedia.Instance.PlayPersonSay(troop.Leader, GameRandom.Chance(50) ?`
- `Game/Render/RenderEvent/Events/TroopSpellSkillEvent.cs` — `GameMedia.Instance.PlayPersonSay(troop.Leader, GameRandom.Chance(50) ?`

### sango.turn(泛型权重工具,随首用方细分时再定)(5 处)

- `Game/Tools/AutoName.cs` — `return all[Sango.Core.GameRandom.Range(0, all.Count)];`
- `Game/Tools/AutoName.cs` — `return list[Sango.Core.GameRandom.Range(0, list.Count)];`
- `Game/Tools/AutoName.cs` — `return mFirstNameList[Sango.Core.GameRandom.Range(0, mFirstNameList.Co`
- `Game/Tools/WeightList.cs` — `int ran = GameRandom.Range(TotaleWeight);`
- `Game/Tools/WeightList.cs` — `int ran = GameRandom.Range(total);`

### (生命周期/快照,非消费)(4 处)

- `SangoRuntime/SangoKernelBoot.cs` — `GameRandom.ImportState(randomState);`
- `SangoRuntime/SangoKernelBoot.cs` — `GameRandom.Init(0);`
- `SangoRuntime/SangoKernelBoot.cs` — `GameRandom.Init(seed);`
- `SangoRuntime/SangoSaveParticipant.cs` — `int[] randomState = GameRandom.ExportState();`
