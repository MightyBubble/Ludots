namespace ControlPlaneProjectionShowcaseMod
{
    /// <summary>
    /// Data-side string constants for the RFC-0065 control plane projection showcase (SHOW-2, M3/M4 slices).
    /// Every tag/type/collection key the mod touches is declared here; nothing is hardcoded in Core.
    /// </summary>
    public static class ControlPlaneProjectionShowcaseIds
    {
        public const string MapId = "control_plane_projection";

        public const string InstalledKey = "ControlPlaneProjectionShowcaseMod.Installed";
        public const string StateKey = "ControlPlaneProjectionShowcaseMod.State";
        public const string AutoDemoAppliedKey = "ControlPlaneProjectionShowcaseMod.AutoDemoApplied";
        public const string RefereeUatFrameKey = "ControlPlaneProjectionShowcaseMod.RefereeUat.Frame";
        public const string RefereeUatEnvKey = "LUDOTS_CONTROL_PLANE_PROJECTION_REFEREE_UAT";

        // Presentation projection collections (viewer-relative, owner = P1Rep). Projection only:
        // these keys are never written back into domain collections (RFC-0065 DEC-5).
        public const string OwnedProjectionCollectionKey = "collection.ui.control_plane.owned";
        public const string ProxiedProjectionCollectionKey = "collection.ui.control_plane.proxied";
        public const string RefereePhase0ProjectionCollectionKey = "collection.ui.control_plane.referee.phase0";
        public const string RefereePhase1ProjectionCollectionKey = "collection.ui.control_plane.referee.phase1";

        // Relationship type ids resolved through RelationshipTypeRegistry at bootstrap; no fallback.
        public const string OwnsRelationshipType = "Owns";
        public const string ControlsRelationshipType = "Controls";
        public const string MemberOfRelationshipType = "MemberOf";
        public const string AllyRelationshipType = "Ally";

        public const string OfflineTag = "participant.offline";

        public const string ToggleProxyAction = "ControlPlaneProjection.ToggleProxy";
        public const string InputContext = "ControlPlaneProjection.Controls";

        public const string WebUiTopic = "ludots.showcase.control_plane.state";
        public const string ToggleProxyCommand = "toggleProxy";
        public const string WebUiSessionId = "control-plane-projection-showcase";

        // Map instance ids (assets/Maps/control_plane_projection.json).
        public const string P1RepInstanceId = "p1-rep";
        public const string P2RepInstanceId = "p2-rep";
        public const string TeamRepInstanceId = "team-rep";

        public static readonly string[] P1UnitInstanceIds =
        {
            "p1-unit-1",
            "p1-unit-2",
            "p1-unit-3",
            "p1-unit-4",
            "p1-unit-5",
        };

        public static readonly string[] P2UnitInstanceIds =
        {
            "p2-unit-1",
            "p2-unit-2",
            "p2-unit-3",
        };
    }
}
