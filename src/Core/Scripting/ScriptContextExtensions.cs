using Ludots.Core.Diagnostics;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Map;

namespace Ludots.Core.Scripting
{
    public static class ScriptContextExtensions
    {
        public static GameEngine GetEngine(this ScriptContext ctx) => ctx.Get(CoreServiceKeys.Engine);
        public static World GetWorld(this ScriptContext ctx) => ctx.Get(CoreServiceKeys.World);
        public static MapSession GetMapSession(this ScriptContext ctx) => ctx.Get(CoreServiceKeys.MapSession);
        public static ILogBackend GetLogBackend(this ScriptContext ctx) => ctx.Get(CoreServiceKeys.LogBackend);

        public static bool IsMap(this ScriptContext ctx, MapId mapId)
        {
            MapSession? session = ctx.Get(CoreServiceKeys.MapSession);
            return session != null && session.MapId == mapId;
        }

        public static bool IsMap<TMap>(this ScriptContext ctx)
            where TMap : MapDefinition, new()
        {
            return ctx.IsMap(new TMap().Id);
        }
    }
}
