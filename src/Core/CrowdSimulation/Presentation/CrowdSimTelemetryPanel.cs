using System.Numerics;
using Ludots.Core.CrowdSimulation.Runtime;
using Ludots.Core.Presentation.Hud;

namespace Ludots.Core.CrowdSimulation.Presentation;

/// <summary>
/// 群体遥测区块,画进与 ChunkDebugPanel 相同的 ScreenOverlayBuffer。
/// 默认一行(子步钳位、挤离无处安放、规划次数,以及接触/重烘/迷雾刷新);
/// 展开时补 tick、等路、停摆和玩家 1 的迷雾格计数。文本只在对应数值变化时重建。
/// </summary>
internal sealed class CrowdSimTelemetryPanel
{
    private long _subStepClamped, _evictStuck, _plans, _contacts, _rebakes, _fog;
    private long _ticks, _units, _pathWaits, _stalls;
    private int _visible, _explored, _ghosts;
    private bool _expanded;
    private bool _ready;
    private string _collapsed = string.Empty;
    private string _detail = string.Empty;
    private string _fogCells = string.Empty;

    public void Draw(
        ScreenOverlayBuffer overlay,
        CrowdHudPanel hud,
        CrowdSimulationTelemetry telemetry,
        bool expanded,
        int visibleCells,
        int exploredCells,
        int ghostCells)
    {
        if (!_ready
            || _subStepClamped != telemetry.SubStepClamped
            || _evictStuck != telemetry.EvictStuck
            || _plans != telemetry.Plans
            || _contacts != telemetry.ContactsSum
            || _rebakes != telemetry.Rebakes
            || _fog != telemetry.FogRefreshes
            || _expanded != expanded
            || (expanded && (
                _ticks != telemetry.Ticks
                || _units != telemetry.Units
                || _pathWaits != telemetry.PathWaits
                || _stalls != telemetry.StallTicks
                || _visible != visibleCells
                || _explored != exploredCells
                || _ghosts != ghostCells)))
        {
            _subStepClamped = telemetry.SubStepClamped;
            _evictStuck = telemetry.EvictStuck;
            _plans = telemetry.Plans;
            _contacts = telemetry.ContactsSum;
            _rebakes = telemetry.Rebakes;
            _fog = telemetry.FogRefreshes;
            _expanded = expanded;
            _collapsed = $"subStepClamped {_subStepClamped} evictStuck {_evictStuck} plans {_plans} contacts {_contacts} rebake {_rebakes} fog {_fog}";
            if (expanded)
            {
                _ticks = telemetry.Ticks;
                _units = telemetry.Units;
                _pathWaits = telemetry.PathWaits;
                _stalls = telemetry.StallTicks;
                _visible = visibleCells;
                _explored = exploredCells;
                _ghosts = ghostCells;
                _detail = $"ticks {_ticks} units {_units} pathWait {_pathWaits} stall {_stalls}";
                _fogCells = $"fogCells vis {_visible} explored {_explored} ghost {_ghosts}";
            }

            _ready = true;
        }

        int x = hud.OriginXPx;
        int y = hud.OriginYPx + hud.HeightPx + 8;
        int height = expanded ? 80 : 32;
        overlay.AddRect(x, y, hud.WidthPx, height, new Vector4(0f, 0f, 0f, 0.72f), new Vector4(0.45f, 0.75f, 1f, 1f));
        overlay.AddText(x + 16, y + 6, _collapsed, 16, new Vector4(0.85f, 0.95f, 1f, 1f));
        if (!expanded) return;
        overlay.AddText(x + 16, y + 30, _detail, 16, new Vector4(0.85f, 0.95f, 1f, 1f));
        overlay.AddText(x + 16, y + 54, _fogCells, 16, new Vector4(0.85f, 0.95f, 1f, 1f));
    }
}
