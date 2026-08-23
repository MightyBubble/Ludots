namespace Ludots.Core.Scripting
{
    /// <summary>
    /// ScriptContext payload keys carried by map trigger events
    /// (MapHeartbeat / EntitySpawned / EntityDied / EntityAliveCountChanged /
    /// RegionEntered / RegionExited).
    /// </summary>
    public static class MapTriggerEventPayloadKeys
    {
        public const string SourceEntity = "MapTrigger.SourceEntity";      // Entity
        public const string SourceTeamId = "MapTrigger.SourceTeamId";      // int
        public const string RegionId = "MapTrigger.RegionId";              // string
        public const string Count = "MapTrigger.Count";                    // int
        public const string Delta = "MapTrigger.Delta";                    // int
        public const string VarName = "MapTrigger.VarName";                // string
        public const string VarValueFloat = "MapTrigger.VarValueFloat";    // float
        public const string VarValueInt = "MapTrigger.VarValueInt";        // int
        public const string Phase = "MapTrigger.Phase";                    // int
        public const string HeartbeatIndex = "MapTrigger.HeartbeatIndex";            // int
        public const string TargetEntity = "MapTrigger.TargetEntity";      // Entity
        public const string TagId = "MapTrigger.TagId";                    // int
        public const string Magnitude = "MapTrigger.Magnitude";            // float
        public const string AbilityId = "MapTrigger.AbilityId";            // int
        public const string EffectId = "MapTrigger.EffectId";              // int
        public const string Moment = "MapTrigger.Moment";                  // string
        public const string ModId = "ModId";                               // string
        public const string InputAction = "MapTrigger.InputAction";              // string
        public const string GroundXCm = "MapTrigger.GroundXCm";                  // float
        public const string GroundYCm = "MapTrigger.GroundYCm";                  // float
    }
}
