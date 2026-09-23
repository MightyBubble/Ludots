using System;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Scripting;

namespace MassNavigationPresentationAdapter;

public static class MassNavigationPresentationAdapterInstaller
{
    private const string LocalObserverDisclosureInstalledKey =
        "MassNavigationPresentationAdapter.LocalObserverDisclosureInstalled";
    private const string LocomotionAnimatorParamSystemInstalledKey =
        "MassNavigationPresentationAdapter.LocomotionAnimatorParamSystemInstalled";

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

    public static void EnsureLocomotionAnimatorParams(GameEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        if (engine.GlobalContext.ContainsKey(LocomotionAnimatorParamSystemInstalledKey))
        {
            return;
        }

        engine.InsertPresentationSystemBefore<AnimatorRuntimeSystem>(
            new MassNavigationLocomotionAnimatorParamSystem(engine));
        engine.GlobalContext[LocomotionAnimatorParamSystemInstalledKey] = true;
    }
}
