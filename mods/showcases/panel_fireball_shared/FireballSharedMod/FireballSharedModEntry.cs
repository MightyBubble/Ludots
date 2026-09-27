using System;
using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace FireballSharedMod;

public sealed class FireballSharedModEntry : IMod
{
    public void OnLoad(IModContext context)
    {
        context.Log("[FireballSharedMod] Loaded - fireball arena uses GAS abilities/effects and presenter rules; local order input installs via CoreInputMod auto assembly");
    }

    public void OnUnload() { }
}
