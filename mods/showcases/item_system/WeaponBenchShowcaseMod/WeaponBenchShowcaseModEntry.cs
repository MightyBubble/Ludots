using System.Threading.Tasks;
using ItemSystemShowcaseMod;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace WeaponBenchShowcaseMod;

public sealed class WeaponBenchShowcaseModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            if (ctx.GetEngine() is GameEngine engine)
            {
                engine.GlobalContext[ItemSystemShowcaseFocusPack.Key] = ItemSystemShowcaseFocusPack.WeaponBench;
            }

            return Task.CompletedTask;
        });
    }

    public void OnUnload()
    {
    }
}
