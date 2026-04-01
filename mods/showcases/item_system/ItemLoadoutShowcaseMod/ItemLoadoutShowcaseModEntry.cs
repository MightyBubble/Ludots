using System.Threading.Tasks;
using ItemSystemShowcaseMod;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace ItemLoadoutShowcaseMod;

public sealed class ItemLoadoutShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            if (ctx.GetEngine() is GameEngine engine)
            {
                engine.GlobalContext[ItemSystemShowcaseFocusPack.Key] = ItemSystemShowcaseFocusPack.Loadout;
            }

            return Task.CompletedTask;
        });
    }

    public void OnUnload()
    {
    }
}
