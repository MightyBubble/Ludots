using System.Reflection;
using Ludots.Core.Engine;
using Ludots.Core.Engine.Navigation2D;
using Ludots.Core.Engine.Pacemaker;
using Ludots.Core.Engine.Physics2D;
using Ludots.Core.Engine.TimeFlow;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Scripting;

namespace TimeFlowShowcaseMod;

internal sealed class TimeFlowProfileBridge
{
    public const string ServiceName = "TimeFlowMod.Service";

    private readonly GameEngine _engine;
    private readonly object _service;
    private readonly MethodInfo _activateProfile;
    private readonly MethodInfo _clearOwner;
    private readonly MethodInfo _snapshot;

    private TimeFlowProfileBridge(GameEngine engine, object service)
    {
        _engine = engine;
        _service = service;
        Type type = service.GetType();
        _activateProfile = type.GetMethod("ActivateProfile", new[] { typeof(string), typeof(string), typeof(int) })
            ?? throw new InvalidOperationException("TimeFlowMod service is missing ActivateProfile.");
        _clearOwner = type.GetMethod("ClearOwner", new[] { typeof(string) })
            ?? throw new InvalidOperationException("TimeFlowMod service is missing ClearOwner.");
        _snapshot = type.GetMethod("Snapshot", Type.EmptyTypes)
            ?? throw new InvalidOperationException("TimeFlowMod service is missing Snapshot.");
    }

    public static bool TryCreate(GameEngine engine, out TimeFlowProfileBridge? bridge)
    {
        if (engine.GlobalContext.TryGetValue(ServiceName, out object? service) && service != null)
        {
            bridge = new TimeFlowProfileBridge(engine, service);
            return true;
        }

        bridge = null;
        return false;
    }

    public int ActivateProfile(string profileId, string owner, int priority)
    {
        return (int)(_activateProfile.Invoke(_service, new object[] { profileId, owner, priority }) ?? 0);
    }

    public int ClearOwner(string owner)
    {
        return (int)(_clearOwner.Invoke(_service, new object[] { owner }) ?? 0);
    }

    public TimeFlowShowcaseTimeFlowSnapshot Snapshot()
    {
        object? raw = _snapshot.Invoke(_service, Array.Empty<object>());
        TimeFlowService? coreTimeFlow = _engine.GetService(CoreServiceKeys.TimeFlow);
        GasClockStepPolicy? gasClock = _engine.GetService(CoreServiceKeys.GasClockStepPolicy);
        Physics2DTickPolicy? physics = _engine.GetService(CoreServiceKeys.Physics2DTickPolicy);
        Navigation2DTickPolicy? navigation = _engine.GetService(CoreServiceKeys.Navigation2DTickPolicy);
        SimulationLoopController? loop = _engine.GetService(CoreServiceKeys.SimulationLoopController);

        return new TimeFlowShowcaseTimeFlowSnapshot
        {
            ActiveProfileId = ReadString(raw, "ActiveProfileId", "(baseline)"),
            ActiveOwner = ReadString(raw, "ActiveOwner", "(baseline)"),
            ActiveRequestCount = ReadInt(raw, "ActiveRequestCount"),
            GlobalTimeScale = coreTimeFlow == null ? 1f : coreTimeFlow.GetEffectiveScalePermille(TimeFlowDomainIds.Simulation) / 1000f,
            LoopMode = loop?.Mode.ToString() ?? ReadString(raw, "LoopMode", "Realtime"),
            GasMode = gasClock?.Mode.ToString() ?? ReadString(raw, "GasMode", "Auto"),
            GasStepEveryFixedTicks = gasClock?.StepEveryFixedTicks ?? ReadInt(raw, "GasStepEveryFixedTicks"),
            SimulationScalePermille = coreTimeFlow?.GetEffectiveScalePermille(TimeFlowDomainIds.Simulation) ?? 1000,
            GasScalePermille = coreTimeFlow?.GetEffectiveScalePermille(TimeFlowDomainIds.Gas) ?? 1000,
            PhysicsScalePermille = coreTimeFlow?.GetEffectiveScalePermille(TimeFlowDomainIds.Physics2D) ?? 1000,
            NavigationScalePermille = coreTimeFlow?.GetEffectiveScalePermille(TimeFlowDomainIds.Navigation2D) ?? 1000,
            TasksScalePermille = coreTimeFlow?.GetEffectiveScalePermille(TimeFlowDomainIds.Tasks) ?? 1000,
            PhysicsTargetHz = physics?.TargetHz ?? ReadInt(raw, "PhysicsTargetHz"),
            PhysicsMaxStepsPerFixedTick = physics?.MaxStepsPerFixedTick ?? ReadInt(raw, "PhysicsMaxStepsPerFixedTick"),
            NavigationTargetHz = navigation?.TargetHz ?? ReadInt(raw, "NavigationTargetHz"),
            NavigationMaxStepsPerFixedTick = navigation?.MaxStepsPerFixedTick ?? ReadInt(raw, "NavigationMaxStepsPerFixedTick")
        };
    }

    private static int ReadInt(object? instance, string propertyName)
    {
        object? value = ReadProperty(instance, propertyName);
        return value switch
        {
            int i => i,
            _ => 0
        };
    }

    private static string ReadString(object? instance, string propertyName, string fallback)
    {
        object? value = ReadProperty(instance, propertyName);
        return value?.ToString() ?? fallback;
    }

    private static object? ReadProperty(object? instance, string propertyName)
    {
        if (instance == null)
        {
            return null;
        }

        return instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)?.GetValue(instance);
    }
}
