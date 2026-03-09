using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using CameraAcceptanceMod.Systems;

namespace CameraAcceptanceMod
{
    public sealed class CameraAcceptanceModEntry : IMod
    {
        public void OnLoad(IModContext context)
        {
            context.Log("[CameraAcceptanceMod] Loaded");
            context.OnEvent(GameEvents.GameStart, ctx =>
            {
                var engine = ctx.GetEngine();
                if (engine != null)
                {
                    engine.RegisterPresentationSystem(new CameraAcceptanceSystem(engine));
                }

                return System.Threading.Tasks.Task.CompletedTask;
            });
        }

        public void OnUnload()
        {
        }
    }
}
