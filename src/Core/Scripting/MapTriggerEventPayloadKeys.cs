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
        public const string OldValueFloat = "MapTrigger.OldValueFloat";    // float
        public const string OldValueInt = "MapTrigger.OldValueInt";        // int
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
        public const string SourceMapId = "MapTrigger.SourceMapId";              // MapId (cross-map/global dispatch transport metadata)
        public const string CalendarId = "Calendar.CalendarId";                  // string
        public const string CalendarDayIndex = "Calendar.DayIndex";              // int
        public const string CalendarYear = "Calendar.Year";                      // int
        public const string CalendarEraId = "Calendar.EraId";                    // string
        public const string CalendarCycleId = "Calendar.CycleId";                // string
        public const string CalendarPhaseId = "Calendar.PhaseId";                // string
        public const string CalendarPhaseIndex = "Calendar.PhaseIndex";          // int

        /// <summary>
        /// Whether a string is one of the constants above (reflection-built once); the
        /// compile-side gate for LoadEntryPayload* payloadKey fields.
        /// </summary>
        public static bool IsKnownKey(string key) => KnownKeys.Contains(key);

        private static readonly System.Collections.Generic.HashSet<string> KnownKeys = BuildKnownKeys();

        private static System.Collections.Generic.HashSet<string> BuildKnownKeys()
        {
            var keys = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
            foreach (System.Reflection.FieldInfo field in typeof(MapTriggerEventPayloadKeys)
                         .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
            {
                if (field.GetRawConstantValue() is string constant)
                {
                    keys.Add(constant);
                }
            }

            return keys;
        }
    }
}
