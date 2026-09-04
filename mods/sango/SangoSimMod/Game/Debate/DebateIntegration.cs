/*
 * 文件名：DebateIntegration.cs
 * 描述：舌战系统集成类，用于在游戏流程中触发和管理舌战功能
 * 创建日期：2026-03-27
 * 最后修改：2026-03-27
 */

using Sango.Core.Debate;

namespace Sango.Core
{
    /// <summary>
    /// 舌战系统集成类
    /// </summary>
    public static class DebateIntegration
    {
        /// <summary>
        /// 触发舌战
        /// </summary>
        /// <param name="participant1">参与者1</param>
        /// <param name="participant2">参与者2</param>
        public static void TriggerDebate(DebateParticipant participant1, DebateParticipant participant2)
        {
            // 开始舌战
            DebateManager.Instance.StartDebate(participant1, participant2);

            // 打开舌战窗口
            Sango.Render.WindowEvent windowEvent = Sango.Render.RenderEvent.Instance.Create<Sango.Render.WindowEvent>();
            windowEvent.Init("window_debate", new object[] { });
            Sango.Render.RenderEvent.Instance.Add(windowEvent);
        }

        /// <summary>
        /// 触发舌战（基于武将）
        /// </summary>
        /// <param name="person1">武将1</param>
        /// <param name="person2">武将2</param>
        public static void TriggerDebate(Person person1, Person person2)
        {
            // 创建舌战参与者
            DebateParticipant participant1 = new DebateParticipant(
                person1.Id,
                person1.Name,
                ParticipantType.Player,
                person1.Intelligence,
                person1.Glamour
            );

            DebateParticipant participant2 = new DebateParticipant(
                person2.Id,
                person2.Name,
                ParticipantType.AI,
                person2.Intelligence,
                person2.Glamour
            );

            // 触发舌战
            TriggerDebate(participant1, participant2);
        }

        /// <summary>
        /// 触发舌战（基于城市）
        /// </summary>
        /// <param name="city1">城市1</param>
        /// <param name="city2">城市2</param>
        public static void TriggerDebate(City city1, City city2)
        {
            if (city1.Leader != null && city2.Leader != null)
            {
                TriggerDebate(city1.Leader, city2.Leader);
            }
        }
    }
}
