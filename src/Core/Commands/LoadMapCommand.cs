using Ludots.Core.Engine;
using Ludots.Core.Scripting;
using System.Threading.Tasks;

namespace Ludots.Core.Commands
{
    public class LoadMapCommand : GameCommand
    {
        public string MapId { get; set; }

        public override Task ExecuteAsync(ScriptContext context)
        {
            if (string.IsNullOrEmpty(MapId)) return Task.CompletedTask;
            
            var engine = context.GetEngine();
            if (engine != null)
            {
                engine.LoadMap(MapId);
            }
            return Task.CompletedTask;
        }
    }
}
