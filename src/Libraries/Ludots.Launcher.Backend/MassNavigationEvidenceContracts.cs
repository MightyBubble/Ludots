using System.Numerics;
using System.Text.Json.Serialization;

namespace Ludots.Launcher.Backend;

public readonly record struct MassNavigationEvidencePoint(
    [property: JsonPropertyName("x_cm")] float Xcm,
    [property: JsonPropertyName("y_cm")] float Ycm)
{
    public static MassNavigationEvidencePoint From(Vector2 value) => new(value.X, value.Y);

    public Vector2 ToVector2() => new(Xcm, Ycm);
}

public readonly record struct MassNavigationAnchorEvidenceSample(
    [property: JsonPropertyName("agent_index")] int AgentIndex,
    [property: JsonPropertyName("team_id")] int TeamId,
    [property: JsonPropertyName("owner_entity_id")] int OwnerEntityId,
    [property: JsonPropertyName("presenter_stable_id")] int PresenterStableId,
    [property: JsonPropertyName("solver_world_cm")] MassNavigationEvidencePoint SolverWorldCm,
    [property: JsonPropertyName("ecs_world_cm")] MassNavigationEvidencePoint EcsWorldCm,
    [property: JsonPropertyName("visual_world_cm")] MassNavigationEvidencePoint VisualWorldCm,
    [property: JsonPropertyName("presenter_world_cm")] MassNavigationEvidencePoint PresenterWorldCm,
    [property: JsonPropertyName("owner_visible")] bool OwnerVisible);
