using System.Numerics;

namespace Sango.Core.Player
{
    public interface ICommandEvent
    {
        /// <summary>
        /// 进入当前命令时候触发
        /// </summary>
        void OnEnter();

        /// <summary>
        /// 离开当前命令的时候触发
        /// </summary>
        void OnExit();

        /// <summary>
        /// 当前命令被重新拾起的时候触发(返回)
        /// </summary>
        void OnBack(ICommandEvent whoGone);

        /// <summary>
        /// 当前命令被舍弃的时候触发
        /// </summary>
        void OnDestroy();

        /// <summary>
        /// 结束整个命令链的时候触发
        /// </summary>
        void OnDone();

        /// <summary>
        /// 每帧更新
        /// </summary>
        void Update();

        /// <summary>
        /// 输入处理
        /// </summary>
        /// <param name="eventType"></param>
        /// <param name="cell"></param>
        void HandleEvent(CommandEventType eventType, Cell cell, Vector3 clickPosition, bool isOverUI);
    }
}
