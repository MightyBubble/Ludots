using Ludots.Core.Presentation.Presenters;

namespace MassNavigationPresentationAdapter;

public static class MassNavigationPresentationAdapterIds
{
    public const string AgentLocomotionSpeedParamKey = "mass_navigation.agent.locomotion.speed";

    public static int AgentLocomotionSpeedParam => PresenterParamKeyRegistry.Register(AgentLocomotionSpeedParamKey);
}
