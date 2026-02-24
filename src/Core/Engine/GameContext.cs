using Ludots.Core.Engine;
using Ludots.Core.Map;
using Arch.Core;

namespace Ludots.Core.Engine
{
    public class GameContext
    {
        public GameEngine Engine { get; }
        public World World => Engine.World;
        public IMapManager MapManager => Engine.MapManager;
        
        // Helper to access services stored in GlobalContext (like UIRoot)
        public T GetService<T>(string name) 
        {
             if (Engine.GlobalContext.TryGetValue(name, out var obj) && obj is T t) return t;
             return default;
        }

        public GameContext(GameEngine engine)
        {
            Engine = engine;
        }
    }
}
