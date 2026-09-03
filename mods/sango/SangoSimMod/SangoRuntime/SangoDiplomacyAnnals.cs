// M3.d 外交消息行采集器(照 SangoCombatAnnals 范式):订阅内核外交 GameEvent 群,
// 产出 MUD 风格外交行,经 LinePublished 投 SangoWorldFeed 消息流。事件真源与触发点
// (全部在 DiplomacyAction*.Perform/PerformWithoutCheck 解算尾部,使者到达
// PersonDiplomacy 任务结算时触发):
//   OnDiplomacyAlliance(sender, receiver, success)   结盟成败(DiplomacyActionAlliance);
//   OnDiplomacySendGift(sender, receiver, 金, 成败)   送礼(DiplomacyActionSendGift,派遣即成功);
//   OnDiplomacyTruce / OnDiplomacyDeclareWar          原版 Action 类存在但玩家 UI 桩停用,
//                                                    仅全托管 PerformDiplomacyAction 面可达
//                                                    (当前无调用方);订阅保持接线,事件不来即无行。
// 原版这两类结果只走演出对话框(DiplomacyEvent 的 GameDialog 台词),不入 PlayerMessage;
// 移植的消息流呈现是本仓既定投影面(战报同款),不反向改内核。
// 纪律:只读不改内核状态,不消耗随机——订阅与否不影响确定性 digest。

using System;
using System.Collections.Generic;
using Sango.Core;

namespace Sango.Runtime
{
    public sealed class SangoDiplomacyAnnals : IDisposable
    {
        public const int MaxLines = 200;

        private readonly object _sync = new();
        private readonly Queue<string> _lines = new();
        private bool _attached;

        /// <summary>新外交行发布(feed 转投消息流;内核线程即发布线程)。</summary>
        public event Action<string>? LinePublished;

        /// <summary>订阅内核外交事件群(幂等;事件是静态委托,内核二次启动无需重挂)。</summary>
        public void Attach()
        {
            if (_attached)
            {
                return;
            }

            GameEvent.OnDiplomacyAlliance += OnAlliance;
            GameEvent.OnDiplomacySendGift += OnSendGift;
            GameEvent.OnDiplomacyTruce += OnTruce;
            GameEvent.OnDiplomacyDeclareWar += OnDeclareWar;
            _attached = true;
        }

        public void Dispose()
        {
            if (!_attached)
            {
                return;
            }

            GameEvent.OnDiplomacyAlliance -= OnAlliance;
            GameEvent.OnDiplomacySendGift -= OnSendGift;
            GameEvent.OnDiplomacyTruce -= OnTruce;
            GameEvent.OnDiplomacyDeclareWar -= OnDeclareWar;
            _attached = false;
        }

        public string[] SnapshotLines()
        {
            lock (_sync)
            {
                return _lines.ToArray();
            }
        }

        void OnAlliance(Force sender, Force receiver, bool success) => Publish(success
            ? $"[外交] {Name(sender)}与{Name(receiver)}缔结了同盟"
            : $"[外交] {Name(sender)}对{Name(receiver)}的同盟提议被拒绝");

        void OnSendGift(Force sender, Force receiver, int value, bool success) => Publish(success
            ? $"[外交] {Name(sender)}向{Name(receiver)}赠送了{value}金"
            : $"[外交] {Name(sender)}向{Name(receiver)}的赠礼未能送达");

        void OnTruce(Force sender, Force receiver, bool success) => Publish(success
            ? $"[外交] {Name(sender)}与{Name(receiver)}达成了停战"
            : $"[外交] {Name(sender)}对{Name(receiver)}的停战请求被拒绝");

        void OnDeclareWar(Force sender, Force receiver, bool success) => Publish(success
            ? $"[外交] {Name(sender)}向{Name(receiver)}宣战"
            : $"[外交] {Name(sender)}对{Name(receiver)}的宣战未能成行");

        void Publish(string line)
        {
            lock (_sync)
            {
                _lines.Enqueue(line);
                while (_lines.Count > MaxLines)
                {
                    _lines.Dequeue();
                }
            }

            LinePublished?.Invoke(line);
        }

        static string Name(Force? force) => force?.Name ?? "未知势力";
    }
}
