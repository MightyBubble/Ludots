using System;
using System.Threading.Tasks;
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
        // Core Systems
        public static GameEngine GetEngine(this ScriptContext ctx) => ctx.Get<GameEngine>(ContextKeys.Engine);
        public static World GetWorld(this ScriptContext ctx) => ctx.Get<World>(ContextKeys.World);
        public static WorldMap GetWorldMap(this ScriptContext ctx) => ctx.Get<WorldMap>(ContextKeys.WorldMap);
        public static GameSession GetSession(this ScriptContext ctx) => ctx.Get<GameSession>(ContextKeys.GameSession);
        
        // UI
        public static IUiSystem GetUI(this ScriptContext ctx) => ctx.Get<IUiSystem>(ContextKeys.UISystem);
        // UIRoot is not available in Core. Mods referencing UI library should use ctx.Get<UIRoot>("UIRoot") directly.

        // Map Helpers
        public static bool IsMap(this ScriptContext ctx, MapId mapId)
        {
            var typed = ctx.Get<MapId>(ContextKeys.MapId);
            return typed == mapId;
        }

        public static bool IsMap(this ScriptContext ctx, string mapId) => IsMap(ctx, new MapId(mapId));

        public static bool IsMap<TMap>(this ScriptContext ctx) where TMap : MapDefinition
        {
            var mapId = ctx.Get<MapId>(ContextKeys.MapId);
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
            var tags = ctx.Get<List<string>>(ContextKeys.MapTags);
            if (tags == null) return false;
            
            // MapTag.Name corresponds to the string in JSON/Config
            return tags.Contains(tag.Name);
        }

        // Global Variables
        public static Dictionary<string, object> GetGlobals(this ScriptContext ctx)
        {
            var session = ctx.GetSession();
            return session?.Globals;
        }
        
        // Mod Helpers
        public static bool IsModLoaded(this ScriptContext ctx, string modId)
        {
             var engine = ctx.GetEngine();
             return engine?.ModLoader.LoadedModIds.Contains(modId) ?? false;
        }
        
        // Logging
        public static void Log(this ScriptContext ctx, string message)
        {
            Console.WriteLine($"[Script] {message}");
        }
    }
}
