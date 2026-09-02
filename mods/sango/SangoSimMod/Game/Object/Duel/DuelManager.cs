using System;
using System.Collections.Generic;

namespace Sango.Core
{
    /// <summary>
    /// 单挑管理器类，负责管理单挑过程
    /// </summary>
    public class DuelManager : Singleton<DuelManager>
    {
        /// <summary>
        /// 当前正在进行的单挑
        /// </summary>
        private DuelSystem currentDuel;

        /// <summary>
        /// 开始单挑
        /// </summary>
        /// <param name="attackerTroop">攻击者部队</param>
        /// <param name="defenderTroop">防御者部队</param>
        /// <returns>是否成功开始单挑</returns>
        public bool StartDuel(Troop attackerTroop, Troop defenderTroop)
        {
            // 检查是否已经有正在进行的单挑
            if (currentDuel != null && currentDuel.IsDueling)
            {
                return false;
            }

            // 创建新的单挑系统
            currentDuel = new DuelSystem(attackerTroop, defenderTroop);

            // 开始单挑
            return currentDuel.StartDuel();
        }

        /// <summary>
        /// 处理单挑过程
        /// </summary>
        /// <returns>单挑结果</returns>
        public DuelResult ProcessDuel()
        {
            if (currentDuel == null || !currentDuel.IsDueling)
            {
                return DuelResult.None;
            }

            DuelResult result = DuelResult.Continue;
            while (result == DuelResult.Continue)
            {
                result = currentDuel.ProcessDuelTurn();
            }

            return result;
        }

        /// <summary>
        /// 获取当前正在进行的单挑
        /// </summary>
        /// <returns>当前单挑系统实例</returns>
        public DuelSystem GetCurrentDuel()
        {
            return currentDuel;
        }

        /// <summary>
        /// 检查是否有正在进行的单挑
        /// </summary>
        /// <returns>是否有正在进行的单挑</returns>
        public bool IsDueling()
        {
            return currentDuel != null && currentDuel.IsDueling;
        }

        /// <summary>
        /// 清除当前单挑
        /// </summary>
        public void ClearCurrentDuel()
        {
            currentDuel = null;
        }
    }
}
