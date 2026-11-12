using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Presentation;

namespace CrowdSimulationUiMod.UI;

/// <summary>
/// 指令通知照原型 OrderNotices：记住见过的指令，读数里没有了就标“已结束”再留 4 秒；
/// 按指令号从新到旧显示前三条；换了本方玩家就清空。记忆用定长槽位，不分配。
/// </summary>
internal sealed partial class CrowdHudPage
{
    private const int NoticeSlots = CrowdPlayerHud.MaxNotices * 2;

    private readonly NoticeSlot[] _slots = new NoticeSlot[NoticeSlots];
    private readonly NoticeSlot[] _shownNotices = new NoticeSlot[CrowdPlayerHud.MaxNotices];
    private readonly int[] _order = new int[NoticeSlots];

    private void ClearNotices()
    {
        Array.Clear(_slots);
    }

    private void AgeNotices(in CrowdPlayerHud p)
    {
        for (int k = 0; k < p.NoticeCount; k++)
        {
            CrowdOrderNotice live = p.Notice(k);
            int slot = FindSlot(live.Id);
            if (slot < 0) slot = FreeSlot();
            _slots[slot] = new NoticeSlot { Used = true, Gone = false, At = _now, Notice = live };
        }

        for (int s = 0; s < _slots.Length; s++)
        {
            ref NoticeSlot slot = ref _slots[s];
            if (!slot.Used || IsLive(in p, slot.Notice.Id)) continue;
            if (!slot.Gone)
            {
                slot.Gone = true;
                slot.At = _now;
            }
            else if (_now - slot.At > LingerTicks)
            {
                slot = default;
            }
        }

        int shown = 0;
        for (int s = 0; s < _slots.Length; s++)
        {
            if (_slots[s].Used) _order[shown++] = s;
        }

        for (int i = 1; i < shown; i++)
        {
            int key = _order[i];
            int j = i - 1;
            while (j >= 0 && _slots[_order[j]].Notice.Id < _slots[key].Notice.Id)
            {
                _order[j + 1] = _order[j];
                j--;
            }

            _order[j + 1] = key;
        }

        for (int row = 0; row < _shownNotices.Length; row++)
        {
            NoticeSlot next = row < shown ? _slots[_order[row]] : default;
            next.At = 0;
            if (next.Equals(_shownNotices[row])) continue;
            _shownNotices[row] = next;
            WriteNotice(row, in next);
        }
    }

    private void WriteNotice(int row, in NoticeSlot slot)
    {
        Mark(_notices[row].Pick(!slot.Used ? 0 : slot.Gone ? 2 : 1));
        if (!slot.Used) return;
        CrowdOrderNotice n = slot.Notice;
        string mode = n.Mode switch
        {
            CrowdOrderMode.Preserve => "保持相对位置",
            CrowdOrderMode.Converge => "聚拢",
            CrowdOrderMode.Formation => "成阵",
            _ => throw new InvalidOperationException($"未知指令模式 {(int)n.Mode}。"),
        };
        W.Append(CrowdHudFormat.Inv, $"指令 #{n.Id} · {mode}");
        Flush(_noticeTitle[row]);

        int tone;
        if (slot.Gone)
        {
            W.Append("已结束");
            tone = 1;
        }
        else if (n.Planning > 0)
        {
            W.Append("规划路线中…");
            tone = 1;
        }
        else if (n.Units > 0 && n.Arrived + n.Unreachable >= n.Units && n.Moving == 0)
        {
            W.Append(n.Unreachable > 0 ? "部分到达" : "全部到达");
            tone = n.Unreachable > 0 ? 2 : 3;
        }
        else
        {
            W.Append(CrowdHudFormat.Inv, $"行进中 {n.Moving}");
            tone = 0;
        }

        Flush(tone == 0 ? _noticeStatus[row] : tone == 1 ? _noticeMuted[row] : tone == 2 ? _noticeBad[row] : _noticeGood[row]);
        if (tone != 0) Put(_noticeStatus[row], "");
        if (tone != 1) Put(_noticeMuted[row], "");
        if (tone != 2) Put(_noticeBad[row], "");
        if (tone != 3) Put(_noticeGood[row], "");

        W.Append(CrowdHudFormat.Inv, $"{CrowdHudFormat.Int(n.Units)} 单位 · 已到达 {CrowdHudFormat.Int(n.Arrived)}");
        if (n.Unreachable > 0) W.Append(CrowdHudFormat.Inv, $" · 无法到达 {CrowdHudFormat.Int(n.Unreachable)}");
        Flush(_noticeDetail[row]);
    }

    private int FindSlot(int id)
    {
        for (int s = 0; s < _slots.Length; s++)
        {
            if (_slots[s].Used && _slots[s].Notice.Id == id) return s;
        }

        return -1;
    }

    /// <summary>空槽优先；满了就让出指令号最小的那条（排在显示的三条之外）。</summary>
    private int FreeSlot()
    {
        int oldest = 0;
        for (int s = 0; s < _slots.Length; s++)
        {
            if (!_slots[s].Used) return s;
            if (_slots[s].Notice.Id < _slots[oldest].Notice.Id) oldest = s;
        }

        return oldest;
    }

    private static bool IsLive(in CrowdPlayerHud p, int id)
    {
        for (int k = 0; k < p.NoticeCount; k++)
        {
            if (p.Notice(k).Id == id) return true;
        }

        return false;
    }

    private record struct NoticeSlot
    {
        public bool Used;
        public bool Gone;
        public long At;
        public CrowdOrderNotice Notice;
    }
}
