using System;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;

namespace Ludots.Core.Presentation.MassNavigation;

public static class MassNavigationPresentationAdapterInstaller
{
    private const string LocalObserverDisclosureInstalledKey =
        "MassNavigationPresentationAdapter.LocalObserverDisclosureInstalled";

    public static void EnsureLocalObserverDisclosure(GameEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        if (engine.GlobalContext.ContainsKey(LocalObserverDisclosureInstalledKey))
        {
            return;
        }

        engine.RegisterSystem(
            MassNavigationObserverDisclosure.CreateLocalAgentDisclosure(engine),
            SystemGroup.RuntimeEntityBinding);
        engine.GlobalContext[LocalObserverDisclosureInstalledKey] = true;
    }
}
