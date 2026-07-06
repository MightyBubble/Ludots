using Ludots.Core.Mathematics;

namespace LiveMapEditorMod.Runtime;

internal sealed class LiveMapEditorObstacleState
{
    public const string DefaultTemplateId = "live_map_editor_obstacle";

    public string TemplateId { get; set; } = DefaultTemplateId;
    public string Shape { get; set; } = "circle";
    public int RadiusCm { get; set; } = 120;
    public int HalfWidthCm { get; set; } = 120;
    public int HalfHeightCm { get; set; } = 120;
    public int NavRadiusCm { get; set; } = 120;
    public bool SinkPhysicsCollider { get; set; } = true;
    public bool SinkNavigationObstacle { get; set; } = true;
    public WorldCmInt2[] PolygonVertices { get; set; } =
    [
        new(-120, -120),
        new(120, -120),
        new(120, 120),
        new(-120, 120)
    ];
}
