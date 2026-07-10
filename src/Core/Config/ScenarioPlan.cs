using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Ludots.Core.Mathematics;

namespace Ludots.Core.Config
{
    /// <summary>
    /// Opening-plan catalog entry: references a map and declares placements / ownership / knobs.
    /// Does not own map identity or ruleset definitions (see map-scenario-plan contract).
    /// </summary>
    public sealed class ScenarioPlan : IIdentifiable
    {
        public string Id { get; set; } = string.Empty;

        public string MapId { get; set; } = string.Empty;

        public long? Seed { get; set; }

        /// <summary>Authoring layout knobs for this opening; free-form scalars/objects under an explicit top-level key.</summary>
        public Dictionary<string, JsonNode>? Layout { get; set; }

        public List<ScenarioPlanPlacement> Placements { get; set; } = new List<ScenarioPlanPlacement>();

        public List<ScenarioPlanTeamOwnership> Teams { get; set; } = new List<ScenarioPlanTeamOwnership>();

        public List<ScenarioPlanPlayerOwnership> Players { get; set; } = new List<ScenarioPlanPlayerOwnership>();

        public ParticipantRelationshipConfig? InitialRelationships { get; set; }
    }

    public sealed class ScenarioPlanPlacement
    {
        public string Id { get; set; } = string.Empty;

        public string TemplateId { get; set; } = string.Empty;

        public IntVector2? Position { get; set; }

        public float? FacingAngleRad { get; set; }

        public int? TeamId { get; set; }

        public int? PlayerOwnerId { get; set; }

        public List<ScenarioPlanComponentPatch> ComponentPatches { get; set; } = new List<ScenarioPlanComponentPatch>();

        public List<ParamOverrideData> PerformerParamOverrides { get; set; } = new List<ParamOverrideData>();
    }

    /// <summary>
    /// Per-instance component patch shape aligned with <c>RuntimeEntitySpawnComponentPatch</c>
    /// without coupling ScenarioPlan authoring to spawn runtime types.
    /// </summary>
    public sealed class ScenarioPlanComponentPatch
    {
        public string ComponentName { get; set; } = string.Empty;

        public JsonNode Data { get; set; } = null!;
    }

    public sealed class ScenarioPlanTeamOwnership
    {
        public int TeamId { get; set; }

        public string RepresentativePlacementId { get; set; } = string.Empty;
    }

    public sealed class ScenarioPlanPlayerOwnership
    {
        public int PlayerId { get; set; }

        public int TeamId { get; set; }

        public string RepresentativePlacementId { get; set; } = string.Empty;
    }
}
