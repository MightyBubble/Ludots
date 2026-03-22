using ChunkStreamingShowcaseMod.Runtime;
using ChunkStreamingShowcaseMod.Triggers;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace ChunkStreamingShowcaseMod
{
    public sealed class ChunkStreamingShowcaseModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("[ChunkStreamingShowcaseMod] Loaded");

            var runtime = new ChunkStreamingShowcaseRuntime();
            context.OnEvent(GameEvents.GameStart, new InstallChunkStreamingShowcaseOnGameStartTrigger(context, runtime).ExecuteAsync);
            context.OnEvent(GameEvents.MapLoaded, runtime.HandleMapFocusedAsync);
            context.OnEvent(GameEvents.MapResumed, runtime.HandleMapFocusedAsync);
            context.OnEvent(GameEvents.MapUnloaded, runtime.HandleMapUnloadedAsync);
        }

        public void OnUnload()
        {
        }
    }
}
