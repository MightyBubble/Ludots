using System;

namespace ThreeKingdomsScenarioMod;

internal static class ThreeKingdomsScenarioIds
{
    public const string MapId = "road_network_showcase_chunked";
    public const string MapTag = "three_kingdoms_siege";
    public const string SelectionCollectionHandleKey = "ThreeKingdomsScenario.SelectionCollection";
    public const string SelectionInsightHandleKey = "ThreeKingdomsScenario.SelectionInsight";
    public const string RoadColumnPlannerAgentTypeId = "RoadColumn";

    public const string FeatureWallTag = "Feature.ThreeKingdoms.Wall";
    public const string FeatureGateTag = "Feature.ThreeKingdoms.Gate";
    public const string FeatureTunnelTag = "Feature.ThreeKingdoms.Tunnel";
    public const string FeatureTrenchTag = "Feature.ThreeKingdoms.Trench";

    public const string InfantryAssaultContextAbilityId = "Ability.ThreeKingdoms.Unit.AssaultContext";
    public const string InfantryEnterAttachmentAbilityId = "Ability.ThreeKingdoms.Unit.EnterAttachment";
    public const string InfantryWallClimbAbilityId = "Ability.ThreeKingdoms.Unit.WallClimb";
    public const string InfantryGateCaptureAbilityId = "Ability.ThreeKingdoms.Unit.GateCapture";
    public const string InfantryFillTrenchAbilityId = "Ability.ThreeKingdoms.Unit.FillTrench";

    public const string LadderAttachContextAbilityId = "Ability.ThreeKingdoms.Ladder.AttachContext";
    public const string LadderAttachAbilityId = "Ability.ThreeKingdoms.Ladder.Attach";
    public const string LadderDeployOneAbilityId = "Ability.ThreeKingdoms.Ladder.DeployOne";
    public const string LadderToggleHoldAbilityId = "Ability.ThreeKingdoms.Ladder.ToggleHold";

    public const string TunnelContextAbilityId = "Ability.ThreeKingdoms.Tunnel.SetExitContext";
    public const string TunnelSetExitAbilityId = "Ability.ThreeKingdoms.Tunnel.SetExit";
    public const string TunnelEvacuateAbilityId = "Ability.ThreeKingdoms.Tunnel.Evacuate";
    public const string TunnelSummonAbilityId = "Ability.ThreeKingdoms.Tunnel.Summon";

    public static bool IsScenarioMap(string? mapId)
    {
        return string.Equals(mapId, MapId, StringComparison.OrdinalIgnoreCase);
    }
}
