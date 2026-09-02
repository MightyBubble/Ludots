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
        // InputAction contract (input/command chain): the acting representative entity,
        // the semantic action id, the pointer's window-pixel position at the fired moment
        // (press -> press point, release -> release point; window pixels so per-binding
        // routing stays possible), and the held semantic-modifier bitmask
        // (InputActionFiredModifiers). Input events carry pointer facts only — ground
        // projection is a graph-side derivation through ScreenPointToGround on the same
        // LogicView ray.
        public const string Rep = "MapTrigger.Rep";                              // Entity
        public const string Action = "MapTrigger.Action";                        // string
        public const string PointerScreenX = "MapTrigger.PointerScreenX";        // float (window px)
        public const string PointerScreenY = "MapTrigger.PointerScreenY";        // float (window px)
        public const string Modifiers = "MapTrigger.Modifiers";                  // int (bitmask)
        public const string SourceMapId = "MapTrigger.SourceMapId";              // MapId (cross-map/global dispatch transport metadata)
        // Collection pass-through contract (#1398 S2b gap 9, Case E 06): DispatchCollectionEvent
        // fires a schema-less map event carrying the final entity set plus the set semantics;
        // EventKeyedCollectionWriter receives by event key and writes EntityCollectionStore.
        public const string CollectionEntitySet = "MapTrigger.CollectionEntitySet"; // Entity[] (final hit set)
        public const string CollectionOp = "MapTrigger.CollectionOp";              // int (0=replace,1=add,2=subtract)
        public const string CollectionKey = "MapTrigger.CollectionKey";            // int (EntityCollectionStore key id)
        public const string FieldLayer = "MapTrigger.FieldLayer";                // string (field layer key)

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
