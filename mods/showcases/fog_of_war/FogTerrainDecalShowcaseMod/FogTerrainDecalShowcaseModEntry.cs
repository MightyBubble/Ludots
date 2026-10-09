using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace FogTerrainDecalShowcaseMod;

public sealed class FogTerrainDecalShowcaseModEntry : IMod
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

            engine.RegisterPresentationSystem(new FogTerrainDecalDemoWriterSystem(engine));
            return Task.CompletedTask;
        });
    }

    public void OnUnload()
    {
    }
}
