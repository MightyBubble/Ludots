using Ludots.Core.Components;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Units;

namespace Ludots.Core.CrowdSimulation.Diagnostics;

/// <summary>Web <c>unitChecksum</c> 近似：位置与运动学按 f32 位型混入（fuzz 对拍诊断，非规范校验码）。</summary>
public static class CrowdWebFuzzChecksum
{
    private const uint Offset = 0x811c9dc5;
    private const uint Prime = 0x01000193;
    private const float CmPerMeter = CrowdReferenceMeters.CentimetersPerMeter;

    public static string UnitHex(CrowdSimSession session)
    {
        uint h = Mix(Mix(Offset, (uint)session.TickCount), (uint)session.Units.Count);
        int n = session.Units.Count;
        var movement = session.Movement;
        if (movement != null && movement.Positions.Length >= n)
        {
            for (int i = 0; i < n; i++)
            {
                h = HashFloat(h, (float)movement.Positions[i].X.ToDouble() / CmPerMeter);
                h = HashFloat(h, (float)movement.Positions[i].Y.ToDouble() / CmPerMeter);
            }

            for (int i = 0; i < n; i++)
            {
                var kin = movement.Kins[i];
                h = HashFloat(h, (float)kin.Velocity.X.ToDouble() / CmPerMeter);
                h = HashFloat(h, (float)kin.Velocity.Y.ToDouble() / CmPerMeter);
                h = HashFloat(h, (float)kin.SlotOffsetCm.X.ToDouble() / CmPerMeter);
                h = HashFloat(h, (float)kin.SlotOffsetCm.Y.ToDouble() / CmPerMeter);
                h = HashFloat(h, (float)kin.Blend.ToDouble());
                h = HashFloat(h, (float)kin.StallSeconds.ToDouble());
            }

            for (int i = 0; i < n; i++)
            {
                h = Mix(h, movement.States[i].State);
                h = Mix(h, (uint)movement.States[i].GroupId);
            }
        }
        else
        {
            for (int i = 0; i < n; i++)
            {
                var entity = session.Units.EntityAt(i);
                var pos = session.World.Get<WorldPositionCm>(entity).Value;
                h = HashFloat(h, (float)pos.X.ToDouble() / CmPerMeter);
                h = HashFloat(h, (float)pos.Y.ToDouble() / CmPerMeter);
                var state = session.World.Get<CrowdSimulationUnitState>(entity);
                h = Mix(h, state.State);
                h = Mix(h, (uint)state.GroupId);
            }
        }

        for (int i = 0; i < n; i++) h = Mix(h, session.Units.HandleAt(i));
        return h.ToString("x8").PadLeft(8, '0');
    }

    private static uint HashFloat(uint h, float v)
    {
        int bits = BitConverter.SingleToInt32Bits(v);
        return Mix(h, (uint)bits);
    }

    private static uint Mix(uint h, uint v) => (uint)((h ^ v) * Prime);
}
