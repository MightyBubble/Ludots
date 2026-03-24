using System.Threading.Tasks;
using ItemSystemShowcaseMod;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace RaidLoopShowcaseMod;

public sealed class RaidLoopShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            if (ctx.GetEngine() is GameEngine engine)
            {
                engine.GlobalContext[ItemSystemShowcaseFocusPack.Key] = ItemSystemShowcaseFocusPack.RaidLoop;
            }

            return Task.CompletedTask;
        });
    }

    public void OnUnload()
    {
    }
}
