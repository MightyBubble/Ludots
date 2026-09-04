/*
 * 文件名：DebateManager.cs
 * 描述：舌战管理器，负责管理整个舌战过程
 * 创建日期：2026-03-27
 * 最后修改：2026-03-27
 */

using System;
using System.Collections.Generic;

namespace Sango.Core.Debate
{
    /// <summary>
    /// 舌战管理器
    /// </summary>
    public class DebateManager : Sango.Singleton<DebateManager>
    {

        /// <summary>
        /// 当前舌战实例
        /// </summary>
        private DebateInstance _currentDebate;

        /// <summary>
        /// 初始化舌战管理器
        /// </summary>
        public void Init()
        {
            // 初始化舌战相关数据
            DebateSkillFactory.Init();
        }

        /// <summary>
        /// 开始新的舌战
        /// </summary>
        /// <param name="participant1">参与者1</param>
        /// <param name="participant2">参与者2</param>
        public void StartDebate(DebateParticipant participant1, DebateParticipant participant2)
        {
            // 创建新的舌战实例
            _currentDebate = new DebateInstance(participant1, participant2);

            // 开始舌战
            _currentDebate.Start();
        }

        /// <summary>
        /// 结束当前舌战
        /// </summary>
        public void EndDebate()
        {
            if (_currentDebate != null)
            {
                _currentDebate.End();
                _currentDebate = null;
            }
        }

        /// <summary>
        /// 获取当前舌战实例
        /// </summary>
        /// <returns>当前舌战实例</returns>
        public DebateInstance GetCurrentDebate()
        {
            return _currentDebate;
        }

        /// <summary>
        /// 更新舌战
        /// </summary>
        /// <param name="deltaTime">时间增量</param>
        public void Update(float deltaTime)
        {
            if (_currentDebate != null && _currentDebate.IsRunning)
            {
                _currentDebate.Update(deltaTime);
            }
        }
    }
}
