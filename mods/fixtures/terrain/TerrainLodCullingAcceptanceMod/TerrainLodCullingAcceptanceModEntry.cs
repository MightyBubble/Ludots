using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace TerrainLodCullingAcceptanceMod;

public sealed class TerrainLodCullingAcceptanceModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            GameEngine? engine = ctx.GetEngine();
            if (engine == null)
            {
                return Task.CompletedTask;
            }

            engine.RegisterPresentationSystem(new TerrainLodCameraScriptSystem(engine));
            return Task.CompletedTask;
        });
    }

    public void OnUnload()
    {
    }
}
