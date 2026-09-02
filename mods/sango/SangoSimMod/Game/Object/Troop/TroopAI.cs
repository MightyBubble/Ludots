//namespace Sango.Core
//{
//    public class TroopAI
//    {
//        public static bool DoAI(Troop troop, Scenario scenario)
//        {
//            if (troop.ActionOver) return true;
//            if (troop.HasControlBuff()) return true;
//            if (!troop.IsAlive) return true;

//            switch (troop.missionType)
//            {
//                case (int)MissionType.TroopOccupyCity:
//                    return AIAttackCity(troop, scenario);
//                case (int)MissionType.TroopProtectCity:
//                    return AIProtectCity(troop, scenario);
//                case (int)MissionType.TroopTransformGoodsToCity:
//                    return AITransportGoods(troop, scenario);
//                case (int)MissionType.TroopBuildBuilding:
//                    return AIBuildBuilding(troop, scenario);
//                case (int)MissionType.TroopReturnCity:
//                    return AIReturnToCity(troop, scenario);
//                default:
//                    return AIIdle(troop, scenario);
//            }
//        }

//        private static bool AIAttackCity(Troop troop, Scenario scenario)
//        {
//            City targetCity = scenario.citySet.Get(troop.missionTarget);
//            if (targetCity == null || !targetCity.IsAlive)
//            {
//                troop.ActionOver = true;
//                return true;
//            }

//            if (troop.cell == targetCity.CenterCell || troop.cell.IsAdjacentTo(targetCity.CenterCell))
//            {
//                return AIAttackTarget(troop, targetCity, scenario);
//            }
//            else
//            {
//                return AIMoveToTarget(troop, targetCity.CenterCell, scenario);
//            }
//        }

//        private static bool AIProtectCity(Troop troop, Scenario scenario)
//        {
//            City targetCity = scenario.citySet.Get(troop.missionTarget);
//            if (targetCity == null || !targetCity.IsAlive)
//            {
//                troop.ActionOver = true;
//                return true;
//            }

//            Troop enemyTroop = FindClosestEnemyTroop(troop, scenario);
//            if (enemyTroop != null)
//            {
//                if (troop.cell.IsAdjacentTo(enemyTroop.cell))
//                {
//                    return AIAttackTarget(troop, enemyTroop, scenario);
//                }
//                else
//                {
//                    return AIMoveToTarget(troop, enemyTroop.cell, scenario);
//                }
//            }
//            else
//            {
//                return AIMoveToTarget(troop, targetCity.CenterCell, scenario);
//            }
//        }

//        private static bool AITransportGoods(Troop troop, Scenario scenario)
//        {
//            City targetCity = scenario.citySet.Get(troop.missionTarget);
//            if (targetCity == null || !targetCity.IsAlive)
//            {
//                troop.ActionOver = true;
//                return true;
//            }

//            if (troop.cell == targetCity.CenterCell || troop.cell.IsAdjacentTo(targetCity.CenterCell))
//            {
//                // 到达目标城市，交付物资
//                if (troop.gold > 0) targetCity.gold += troop.gold;
//                if (troop.food > 0) targetCity.food += troop.food;
//                if (troop.itemStore != null && troop.itemStore.TotalNumber > 0)
//                {
//                    targetCity.itemStore.Add(troop.itemStore);
//                }
//                troop.ActionOver = true;
//                return true;
//            }
//            else
//            {
//                return AIMoveToTarget(troop, targetCity.CenterCell, scenario);
//            }
//        }

//        private static bool AIBuildBuilding(Troop troop, Scenario scenario)
//        {
//            if (troop.missionTargetCell == null)
//            {
//                troop.ActionOver = true;
//                return true;
//            }

//            if (troop.cell == troop.missionTargetCell)
//            {
//                BuildingType buildingType = scenario.GetObject<BuildingType>(troop.missionTarget);
//                if (buildingType != null)
//                {
//                    return troop.BuildBuilding(troop.missionTargetCell, buildingType);
//                }
//                else
//                {
//                    troop.ActionOver = true;
//                    return true;
//                }
//            }
//            else
//            {
//                return AIMoveToTarget(troop, troop.missionTargetCell, scenario);
//            }
//        }

//        private static bool AIReturnToCity(Troop troop, Scenario scenario)
//        {
//            City targetCity = troop.BelongCity;
//            if (targetCity == null || !targetCity.IsAlive)
//            {
//                troop.ActionOver = true;
//                return true;
//            }

//            if (troop.cell == targetCity.CenterCell || troop.cell.IsAdjacentTo(targetCity.CenterCell))
//            {
//                // 回到城市，解散部队
//                if (troop.troops > 0) targetCity.troops += troop.troops;
//                if (troop.food > 0) targetCity.food += troop.food;
//                if (troop.gold > 0) targetCity.gold += troop.gold;
//                if (troop.itemStore != null && troop.itemStore.TotalNumber > 0)
//                {
//                    targetCity.itemStore.Add(troop.itemStore);
//                }
//                // 释放俘虏
//                for (int i = troop.captiveList.Count - 1; i >= 0; i--)
//                {
//                    Person captive = troop.captiveList[i];
//                    targetCity.wildPersons.Add(captive);
//                }
//                troop.ActionOver = true;
//                return true;
//            }
//            else
//            {
//                return AIMoveToTarget(troop, targetCity.CenterCell, scenario);
//            }
//        }

//        private static bool AIIdle(Troop troop, Scenario scenario)
//        {
//            // 检查粮食是否足够
//            if (troop.food < troop.PrepeareFoodCost() * 3)
//            {
//                // 粮食不足，返回城市
//                troop.missionType = (int)MissionType.TroopReturn;
//                return false;
//            }

//            // 寻找附近的敌人
//            Troop enemyTroop = FindClosestEnemyTroop(troop, scenario);
//            if (enemyTroop != null && troop.cell.GetDistance(enemyTroop.cell) <= 3)
//            {
//                // 发现敌人，攻击
//                troop.missionType = (int)MissionType.TroopOccupyCity;
//                troop.missionTarget = enemyTroop.BelongCity?.Id ?? 0;
//                return false;
//            }

//            // 没有任务，返回城市
//            troop.missionType = (int)MissionType.TroopReturn;
//            return false;
//        }

//        private static bool AIMoveToTarget(Troop troop, Cell targetCell, Scenario scenario)
//        {
//            if (troop.MoveTo(targetCell))
//            {
//                // 移动完成
//                return true;
//            }
//            return false;
//        }

//        private static bool AIAttackTarget(Troop troop, SangoObject target, Scenario scenario)
//        {
//            // 选择合适的技能和目标
//            (SkillInstance bestSkill, Cell bestTargetCell) = SelectBestSkillAndTarget(troop, scenario);
//            if (bestSkill != null && bestTargetCell != null)
//            {
//                // 使用技能
//                return troop.SpellSkill(bestSkill, bestTargetCell);
//            }

//            // 没有合适的技能，使用普通攻击
//            return true;
//        }

//        private static (SkillInstance, Cell) SelectBestSkillAndTarget(Troop troop, Scenario scenario)
//        {
//            SkillInstance bestSkill = null;
//            Cell bestTargetCell = null;
//            int bestScore = 0;

//            foreach (SkillInstance skill in troop.skills)
//            {
//                if (skill.CanBeSpell(troop))
//                {
//                    // 寻找最佳目标
//                    (Cell targetCell, int score) = FindBestTargetForSkill(troop, skill, scenario);
//                    if (score > bestScore)
//                    {
//                        bestScore = score;
//                        bestSkill = skill;
//                        bestTargetCell = targetCell;
//                    }
//                }
//            }

//            return (bestSkill, bestTargetCell);
//        }

//        private static (Cell, int) FindBestTargetForSkill(Troop troop, SkillInstance skill, Scenario scenario)
//        {
//            Cell bestTargetCell = null;
//            int bestScore = 0;

//            // 收集所有可能的目标格子
//            List<Cell> possibleTargetCells = new List<Cell>();
//            foreach (Cell cell in scenario.Map.cells)
//            {
//                if (cell != null && skill.CanSpeellToHere(troop, cell))
//                {
//                    possibleTargetCells.Add(cell);
//                }
//            }

//            // 评估每个目标格子
//            foreach (Cell targetCell in possibleTargetCells)
//            {
//                int score = CalculateSkillTargetScore(troop, targetCell, skill, scenario);
//                if (score > bestScore)
//                {
//                    bestScore = score;
//                    bestTargetCell = targetCell;
//                }
//            }

//            return (bestTargetCell, bestScore);
//        }

//        private static int CalculateSkillTargetScore(Troop troop, Cell targetCell, SkillInstance skill, Scenario scenario)
//        {
//            int score = 0;

//            // 计算技能影响范围内的目标
//            List<Cell> atkCellList = new List<Cell>();
//            skill.GetAttackCells(troop, targetCell, atkCellList);

//            // 评估每个受影响的目标
//            foreach (Cell atkCell in atkCellList)
//            {
//                if (atkCell.troop != null)
//                {
//                    if (troop.IsEnemy(atkCell.troop))
//                    {
//                        // 敌方目标
//                        score += Troop.CalculateSkillDamage(troop, atkCell.troop, skill);
//                        // 优先攻击兵力少的目标
//                        score += (1000 - atkCell.troop.troops) / 10;
//                    }
//                    else if (skill.onlySpellToTeam)
//                    {
//                        // 友方目标（治疗或增益技能）
//                        score += 500; // 基础治疗分数
//                        // 优先治疗兵力少的友军
//                        score += (1000 - atkCell.troop.troops) / 5;
//                    }
//                }
//                else if (atkCell.building != null && skill.canDamageBuilding)
//                {
//                    // 建筑目标
//                    if (troop.IsEnemy(atkCell.building))
//                    {
//                        score += Troop.CalculateSkillDamage(troop, atkCell.building, skill);
//                    }
//                }
//            }

//            // 技能消耗修正
//            score -= skill.costEnergy * 10;

//            // 距离修正
//            int distance = troop.cell.GetDistance(targetCell);
//            if (skill.IsRange())
//            {
//                // 远程技能在合适距离有加成
//                if (distance >= 1 && distance <= GetSkillRange(skill))
//                {
//                    score += 100;
//                }
//            }
//            else
//            {
//                // 近战技能必须在相邻格子
//                if (distance == 1)
//                {
//                    score += 200;
//                }
//                else
//                {
//                    return 0; // 无法使用
//                }
//            }

//            // 冷却时间修正（如果有）
//            if (skill.CDCount > 0)
//            {
//                score -= skill.CDCount * 50;
//            }

//            return score;
//        }

//        private static int GetSkillRange(SkillInstance skill)
//        {
//            // 获取技能的最大范围
//            if (skill.spellRanges != null && skill.spellRanges.Length > 0)
//            {
//                int maxRange = 0;
//                foreach (int range in skill.spellRanges)
//                {
//                    if (range > maxRange)
//                    {
//                        maxRange = range;
//                    }
//                }
//                return maxRange;
//            }
//            return 1;
//        }

//        private static Troop FindClosestEnemyTroop(Troop troop, Scenario scenario)
//        {
//            Troop closestEnemy = null;
//            int minDistance = int.MaxValue;

//            foreach (Troop otherTroop in scenario.troopsSet)
//            {
//                if (otherTroop != null && otherTroop.IsAlive && troop.IsEnemy(otherTroop))
//                {
//                    int distance = troop.cell.GetDistance(otherTroop.cell);
//                    if (distance < minDistance)
//                    {
//                        minDistance = distance;
//                        closestEnemy = otherTroop;
//                    }
//                }
//            }

//            return closestEnemy;
//        }
//    }
//}
