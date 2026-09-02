using System.Collections.Generic;
using Sango.UI;

namespace Sango.Core
{
    /// <summary>
    /// 外交系统
    /// </summary>
    [GameSystem]
    public class DiplomacySystem : GameSystem
    {
        /// <summary>
        /// 目标势力
        /// </summary>
        public Force TargetForce { get; private set; }

        /// <summary>
        /// 所有势力列表
        /// </summary>
        public List<Force> AllForces { get; private set; }

        /// <summary>
        /// 窗口名称
        /// </summary>
        private const string windowName = "UIDiplomacy";

        /// <summary>
        /// 初始化外交系统
        /// </summary>
        /// <param name="targetForce">目标势力</param>
        /// <param name="allForces">所有势力列表</param>
        public void Start(Force targetForce, List<Force> allForces)
        {
            TargetForce = targetForce;
            AllForces = allForces;

            // 显示外交界面
            UIDiplomacy uiDiplomacy = Window.Instance.Open<UIDiplomacy>(windowName);
            uiDiplomacy.OnOpen(this);
        }

        /// <summary>
        /// 执行外交行动（重载，用于赎回俘虏）
        /// </summary>
        /// <param name="actionType">外交行动类型</param>
        /// <param name="receiver">接收方势力</param>
        /// <param name="diplomat">执行外交的武将</param>
        /// <param name="param">行动参数</param>
        /// <param name="captiveId">俘虏ID</param>
        /// <returns>行动是否成功</returns>
        public bool PerformDiplomacyAction(DiplomacyActionType actionType, Force receiver, Person diplomat = null, object param = null, int captiveId = 0)
        {
            // 获取当前势力
            Force sender = Scenario.Cur.CurRunForce;
            if (sender == null)
                return false;

            // 执行外交行动
            return GameSystem.GetSystem<DiplomacyManager>().PerformDiplomacyAction(actionType, sender, receiver, diplomat, param, captiveId);
        }

        /// <summary>
        /// 获取势力间的关系
        /// </summary>
        /// <param name="forceA">势力A</param>
        /// <param name="forceB">势力B</param>
        /// <returns>关系值</returns>
        public int GetRelation(Force forceA, Force forceB)
        {
            return GameSystem.GetSystem<DiplomacyManager>().GetRelation(forceA, forceB);
        }

        /// <summary>
        /// 检查是否可以执行外交行动
        /// </summary>
        /// <param name="actionType">外交行动类型</param>
        /// <param name="sender">发送方势力</param>
        /// <param name="receiver">接收方势力</param>
        /// <returns>是否可以执行</returns>
        public bool CanPerformDiplomacyAction(DiplomacyActionType actionType, Force sender, Force receiver)
        {
            return GameSystem.GetSystem<DiplomacyManager>().CanPerformDiplomacyAction(actionType, sender, receiver);
        }

        /// <summary>
        /// 获取势力的可用武将列表
        /// </summary>
        /// <param name="force">势力</param>
        /// <returns>可用武将列表</returns>
        public List<Person> GetAvailableDiplomats(Force force)
        {
            List<Person> diplomats = new List<Person>();
            force.ForEachPerson(person =>
            {
                if (person.IsFree && person.IsAlive)
                {
                    diplomats.Add(person);
                }
            });
            return diplomats;
        }

        /// <summary>
        /// 退出外交系统
        /// </summary>
        public void Exit()
        {
            Window.Instance.Close(windowName);
            Done();
        }
    }
}