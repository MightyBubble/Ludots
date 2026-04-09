using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Map;

namespace Ludots.Adapter.UE5
{
    public readonly record struct ExternalSessionLaunchRequest(
        GameEngine Engine,
        MapId MapId,
        MapConfig MapConfig,
        MapSession Session,
        ExplicitHostMapBinding Binding,
        bool IsPush);

    public readonly record struct ExternalSessionReturnRequest(
        GameEngine Engine,
        MapSession ResumedSession,
        ExplicitHostMapBinding ResumedBinding,
        MapSession ClosedSession,
        ExplicitHostMapBinding ClosedBinding);

    public interface IExternalSessionTransitionHandler
    {
        IPendingMapLoad BeginLaunch(in ExternalSessionLaunchRequest request);

        IPendingMapLoad BeginReturn(in ExternalSessionReturnRequest request);
    }
}
