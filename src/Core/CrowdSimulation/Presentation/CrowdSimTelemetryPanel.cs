using System.Numerics;
using Ludots.Core.CrowdSimulation.Runtime;
using Ludots.Core.Presentation.Hud;

namespace Ludots.Core.CrowdSimulation.Presentation;

/// <summary>
/// 群体遥测区块,画进与 ChunkDebugPanel 相同的 ScreenOverlayBuffer。
/// 文本只在计数变化时重建。
/// </summary>
internal sealed class CrowdSimTelemetryPanel
{
    private long _ticks, _units, _contacts, _rebakes, _fog, _pathWaits, _stalls;
    private bool _ready;
    private string _line0 = string.Empty;
    private string _line1 = string.Empty;

    public void Draw(ScreenOverlayBuffer overlay, CrowdHudPanel hud, CrowdSimulationTelemetry telemetry)
    {
        if (!_ready
            || _ticks != telemetry.Ticks
            || _units != telemetry.Units
            || _contacts != telemetry.ContactsSum
            || _rebakes != telemetry.Rebakes
            || _fog != telemetry.FogRefreshes
            || _pathWaits != telemetry.PathWaits
            || _stalls != telemetry.StallTicks)
        {
            _ticks = telemetry.Ticks;
            _units = telemetry.Units;
            _contacts = telemetry.ContactsSum;
            _rebakes = telemetry.Rebakes;
            _fog = telemetry.FogRefreshes;
            _pathWaits = telemetry.PathWaits;
            _stalls = telemetry.StallTicks;
            _line0 = $"ticks {_ticks} units {_units} contacts {_contacts}";
            _line1 = $"rebake {_rebakes} fog {_fog} pathWait {_pathWaits} stall {_stalls}";
            _ready = true;
        }

        int x = hud.OriginXPx;
        int y = hud.OriginYPx + hud.HeightPx + 8;
        overlay.AddRect(x, y, hud.WidthPx, 56, new Vector4(0f, 0f, 0f, 0.72f), new Vector4(0.45f, 0.75f, 1f, 1f));
        overlay.AddText(x + 16, y + 6, _line0, 16, new Vector4(0.85f, 0.95f, 1f, 1f));
        overlay.AddText(x + 16, y + 30, _line1, 16, new Vector4(0.85f, 0.95f, 1f, 1f));
    }
}
