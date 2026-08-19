namespace Ludots.Core.Scripting
{
    /// <summary>
    /// ScriptContext payload keys carried by map trigger events
    /// (ThinkWaveElapsed / EntitySpawned / EntityDied / EntityAliveCountChanged /
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
        public const string WaveIndex = "MapTrigger.WaveIndex";            // int
    }
}
