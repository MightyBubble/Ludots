using Ludots.Core.Navigation.Terrain;

namespace LiveMapEditorMod.Runtime;

internal sealed class LiveMapEditorBrushState
{
    public int RadiusCells { get; set; } = 2;
    public byte HeightLevel { get; set; } = 0;
    public byte AreaId { get; set; } = 0;
    public float Cost { get; set; } = 1f;
    public bool Blocked { get; set; }
    public bool Water { get; set; }
    public bool Ramp { get; set; }

    public LogicTerrainSurfaceFlags ResolveFlags()
    {
        LogicTerrainSurfaceFlags flags = LogicTerrainSurfaceFlags.None;
        if (Blocked) flags |= LogicTerrainSurfaceFlags.Blocked;
        if (Water) flags |= LogicTerrainSurfaceFlags.Water;
        if (Ramp) flags |= LogicTerrainSurfaceFlags.Ramp;
        return flags;
    }
}
