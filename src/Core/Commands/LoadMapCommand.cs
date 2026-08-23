using System.Collections.Generic;
using System.Threading.Tasks;
using Ludots.Core.Client;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Map;
using Ludots.Core.Scripting;

namespace Ludots.Core.Commands
{
    public class LoadMapCommand : GameCommand
    {
        public string MapId { get; set; }

        /// <summary>
        /// Explicit seat table for this load. Empty/null means no local seats (spectate / AI-only client).
        /// </summary>
        public List<StartupLocalSeatConfig>? LocalSeats { get; set; }

        public override Task ExecuteAsync(ScriptContext context)
        {
            if (string.IsNullOrEmpty(MapId)) return Task.CompletedTask;

            var engine = context.GetEngine();
            if (engine != null)
            {
                MapLaunchContext? launchContext = CreateLaunchContext();
                engine.LoadMap(MapLoadRequest.FromMapId(MapId, launchContext));
            }

            return Task.CompletedTask;
        }

        private MapLaunchContext? CreateLaunchContext()
        {
            if (LocalSeats == null || LocalSeats.Count == 0)
            {
                return null;
            }

            var bindings = new LocalSeatLaunchBinding[LocalSeats.Count];
            for (int i = 0; i < LocalSeats.Count; i++)
            {
                StartupLocalSeatConfig seat = LocalSeats[i]
                    ?? throw new System.InvalidOperationException($"LoadMapCommand.LocalSeats[{i}] is null.");
                bindings[i] = seat.ToLaunchBinding();
            }

            return MapLaunchContext.Create(bindings);
        }
    }
}
