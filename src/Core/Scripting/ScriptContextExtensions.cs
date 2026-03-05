using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Ludots.Core.Diagnostics;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay;
using Ludots.Core.Map;
using Ludots.Core.Modding;
using Ludots.Core.UI;
using Ludots.Core.Config;
using Arch.Core;

namespace Ludots.Core.Scripting
{
    public static class ScriptContextExtensions
    {
        public static GameEngine GetEngine(this ScriptContext ctx) => ctx.Get(CoreServiceKeys.Engine);
        public static World GetWorld(this ScriptContext ctx) => ctx.Get(CoreServiceKeys.World);
        public static WorldMap GetWorldMap(this ScriptContext ctx) => ctx.Get(CoreServiceKeys.WorldMap);
        public static GameSession GetSession(this ScriptContext ctx) => ctx.Get(CoreServiceKeys.GameSession);
        public static IUiSystem GetUI(this ScriptContext ctx) => ctx.Get(CoreServiceKeys.UISystem);

        public static bool IsMap(this ScriptContext ctx, MapId mapId)
        {
            var typed = ctx.Get(CoreServiceKeys.MapId);
            return typed == mapId;
        }

        public static bool IsMap(this ScriptContext ctx, string mapId) => IsMap(ctx, new MapId(mapId));

        public static bool IsMap<TMap>(this ScriptContext ctx) where TMap : MapDefinition
        {
            var mapId = ctx.Get(CoreServiceKeys.MapId);
            var engine = ctx.GetEngine();
            if (engine != null)
            {
                var def = engine.MapManager.GetDefinition<TMap>();
                if (def != null) return mapId == def.Id;
            }

            return mapId == new MapId(typeof(TMap).Name);
        }

        public static bool MapHasTag(this ScriptContext ctx, MapTag tag)
        {
            var tags = ctx.Get(CoreServiceKeys.MapTags);
            if (tags == null) return false;
            return tags.Contains(tag.Name);
        }

        public static Dictionary<string, object> GetGlobals(this ScriptContext ctx)
        {
            var session = ctx.GetSession();
            return session?.Globals;
        }

        public static bool IsModLoaded(this ScriptContext ctx, string modId)
        {
             var engine = ctx.GetEngine();
             return engine?.ModLoader.LoadedModIds.Contains(modId) ?? false;
        }

        public static void Log(this ScriptContext ctx, string message)
        {
            Diagnostics.Log.Info(in LogChannels.Engine, message);
        }
    }
}
