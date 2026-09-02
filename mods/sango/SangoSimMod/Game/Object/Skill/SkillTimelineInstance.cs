using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sango.Core
{
    /// <summary>
    /// 技能时间轴实例
    /// 负责处理具体的时间控制逻辑
    /// </summary>
    public class SkillTimelineInstance
    {
        /// <summary>
        /// 时间轴配置
        /// </summary>
        private SkillTimeline _timeline;

        /// <summary>
        /// 技能实例
        /// </summary>
        private SkillInstance _skillInstance;

        /// <summary>
        /// 已处理的事件索引
        /// </summary>
        private int _lastProcessedEventIndex = -1;

        /// <summary>
        /// 时间轴总长度
        /// </summary>
        public float Duration => _timeline?.duration ?? 2.5f;

        /// <summary>
        /// 初始化时间轴实例
        /// </summary>
        /// <param name="timeline">时间轴配置</param>
        /// <param name="skillInstance">技能实例</param>
        public void Init(SkillTimeline timeline, SkillInstance skillInstance)
        {
            _timeline = timeline;
            _skillInstance = skillInstance;
            _lastProcessedEventIndex = -1;
        }

        /// <summary>
        /// 处理时间轴事件
        /// </summary>
        /// <param name="troop">释放技能的部队</param>
        /// <param name="spellCell">施法目标单元格</param>
        /// <param name="time">当前时间</param>
        /// <returns>是否处理完成</returns>
        public bool ProcessEvents(Troop troop, Cell spellCell, float time, System.Action action)
        {
            if (_timeline == null || _timeline.events == null)
                return false;

            // 处理事件
            ProcessTimelineEvents(troop, spellCell, time, action);

            // 检查是否完成
            return time > _timeline.duration;
        }

        /// <summary>
        /// 处理时间轴事件
        /// </summary>
        /// <param name="troop">释放技能的部队</param>
        /// <param name="spellCell">施法目标单元格</param>
        /// <param name="time">当前时间</param>
        private void ProcessTimelineEvents(Troop troop, Cell spellCell, float time, System.Action action)
        {
            if (_timeline == null || _timeline.events == null)
                return;

            for (int i = _lastProcessedEventIndex + 1; i < _timeline.events.Count; i++)
            {
                SkillTimelineEvent timelineEvent = _timeline.events[i];
                if (time >= timelineEvent.time)
                {
                    ProcessTimelineEvent(troop, spellCell, timelineEvent, action);
                    _lastProcessedEventIndex = i;
                }
                else
                {
                    break;
                }
            }
        }

        /// <summary>
        /// 处理单个时间轴事件
        /// </summary>
        /// <param name="troop">释放技能的部队</param>
        /// <param name="spellCell">施法目标单元格</param>
        /// <param name="timelineEvent">时间轴事件</param>
        private void ProcessTimelineEvent(Troop troop, Cell spellCell, SkillTimelineEvent timelineEvent, System.Action action)
        {
            timelineEvent.Execute(_skillInstance, troop, spellCell);

            // 如果是执行伤害事件，执行回调
            if (timelineEvent.GetEventType() == SkillTimelineEventType.ExecuteDamage)
            {
                action?.Invoke();
            }
        }

        /// <summary>
        /// 重置时间轴实例
        /// </summary>
        public void Reset()
        {
            _lastProcessedEventIndex = -1;
        }
    }
}