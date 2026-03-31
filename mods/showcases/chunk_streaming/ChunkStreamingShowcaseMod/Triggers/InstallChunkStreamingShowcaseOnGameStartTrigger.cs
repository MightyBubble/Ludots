using System.Threading.Tasks;
using ChunkStreamingShowcaseMod.Runtime;
using ChunkStreamingShowcaseMod.Systems;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace ChunkStreamingShowcaseMod.Triggers
{
    internal sealed class InstallChunkStreamingShowcaseOnGameStartTrigger : Trigger
    {
        private readonly IModContext _context;
        private readonly ChunkStreamingShowcaseRuntime _runtime;

        public InstallChunkStreamingShowcaseOnGameStartTrigger(IModContext context, ChunkStreamingShowcaseRuntime runtime)
        {
            _context = context;
            _runtime = runtime;
            EventKey = GameEvents.GameStart;
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            GameEngine? engine = context.GetEngine();
            if (engine == null)
            {
                return Task.CompletedTask;
            }

            if (engine.GlobalContext.TryGetValue(ChunkStreamingShowcaseIds.InstalledKey, out object? installedObj) &&
                installedObj is bool installed &&
                installed)
            {
                return Task.CompletedTask;
            }

            engine.GlobalContext[ChunkStreamingShowcaseIds.InstalledKey] = true;
            engine.GlobalContext[ChunkStreamingShowcaseIds.RuntimeServiceKey] = _runtime;
            engine.RegisterSystem(new ChunkStreamingShowcaseChunkSystem(engine, _runtime), SystemGroup.InputCollection);
            engine.RegisterPresentationSystem(new ChunkStreamingShowcasePresentationSystem(engine, _runtime));
            _context.Log("[ChunkStreamingShowcaseMod] Chunk streaming runtime and presentation systems registered.");
            return Task.CompletedTask;
        }
    }
}
