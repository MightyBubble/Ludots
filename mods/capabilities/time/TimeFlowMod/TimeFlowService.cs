using Ludots.Core.Engine;
using Ludots.Core.Engine.Navigation2D;
using Ludots.Core.Engine.Pacemaker;
using Ludots.Core.Engine.Physics2D;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Scripting;
using CoreTimeFlowDomainIds = Ludots.Core.Engine.TimeFlow.TimeFlowDomainIds;
using CoreTimeFlowDomainSnapshot = Ludots.Core.Engine.TimeFlow.TimeFlowDomainSnapshot;
using CoreTimeFlowService = Ludots.Core.Engine.TimeFlow.TimeFlowService;
using CoreTimeFlowToken = Ludots.Core.Engine.TimeFlow.TimeFlowToken;

namespace TimeFlowMod;

public sealed class TimeFlowService
{
    private readonly GameEngine _engine;
    private readonly TimeFlowProfileRegistry _registry;
    private readonly CoreTimeFlowService _coreTimeFlow;
    private readonly GasClockStepPolicy _gasClockStepPolicy;
    private readonly Physics2DTickPolicy _physics2DTickPolicy;
    private readonly Navigation2DTickPolicy _navigation2DTickPolicy;
    private readonly SimulationLoopController _simulationLoopController;
    private readonly BaselineState _baseline;
    private readonly Dictionary<int, TimeFlowRequest> _requests = new();
    private readonly List<CoreTimeFlowToken> _activeTokens = new();

    private int _nextHandle = 1;
    private long _nextSequence = 1;
    private string? _effectiveProfileId;
    private string? _effectiveOwner;

    public TimeFlowService(GameEngine engine, TimeFlowProfileRegistry registry)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _coreTimeFlow = engine.GetService(CoreServiceKeys.TimeFlow)
            ?? throw new InvalidOperationException("TimeFlowMod requires Core TimeFlowService.");
        _gasClockStepPolicy = engine.GetService(CoreServiceKeys.GasClockStepPolicy)
            ?? throw new InvalidOperationException("TimeFlowMod requires GasClockStepPolicy.");
        _physics2DTickPolicy = engine.GetService(CoreServiceKeys.Physics2DTickPolicy)
            ?? throw new InvalidOperationException("TimeFlowMod requires Physics2DTickPolicy.");
        _navigation2DTickPolicy = engine.GetService(CoreServiceKeys.Navigation2DTickPolicy)
            ?? throw new InvalidOperationException("TimeFlowMod requires Navigation2DTickPolicy.");
        _simulationLoopController = engine.GetService(CoreServiceKeys.SimulationLoopController)
            ?? throw new InvalidOperationException("TimeFlowMod requires SimulationLoopController.");

        _baseline = new BaselineState(
            SimulationScalePermille: _coreTimeFlow.GetEffectiveScalePermille(CoreTimeFlowDomainIds.Simulation),
            GasScalePermille: _coreTimeFlow.GetEffectiveScalePermille(CoreTimeFlowDomainIds.Gas),
            PhysicsScalePermille: _coreTimeFlow.GetEffectiveScalePermille(CoreTimeFlowDomainIds.Physics2D),
            NavigationScalePermille: _coreTimeFlow.GetEffectiveScalePermille(CoreTimeFlowDomainIds.Navigation2D),
            TasksScalePermille: _coreTimeFlow.GetEffectiveScalePermille(CoreTimeFlowDomainIds.Tasks),
            LoopMode: _simulationLoopController.Mode,
            GasMode: _gasClockStepPolicy.Mode,
            GasStepEveryFixedTicks: _gasClockStepPolicy.StepEveryFixedTicks,
            PhysicsTargetHz: _physics2DTickPolicy.TargetHz,
            PhysicsMaxStepsPerFixedTick: _physics2DTickPolicy.MaxStepsPerFixedTick,
            NavigationTargetHz: _navigation2DTickPolicy.TargetHz,
            NavigationMaxStepsPerFixedTick: _navigation2DTickPolicy.MaxStepsPerFixedTick);
    }

    public int ActivateProfile(string profileId, string owner, int priority = 0)
    {
        if (!_registry.TryGet(profileId, out TimeFlowProfile profile))
        {
            throw new InvalidOperationException($"Unknown TimeFlow profile '{profileId}'.");
        }

        int handle = _nextHandle++;
        _requests[handle] = new TimeFlowRequest(handle, owner, priority, _nextSequence++, profile);
        ApplyResolvedState();
        return handle;
    }

    public bool Release(int handle)
    {
        bool removed = _requests.Remove(handle);
        if (removed)
        {
            ApplyResolvedState();
        }

        return removed;
    }

    public int ClearOwner(string owner)
    {
        if (string.IsNullOrWhiteSpace(owner))
        {
            return 0;
        }

        int removed = 0;
        foreach (int handle in _requests.Values
                     .Where(request => string.Equals(request.Owner, owner, StringComparison.Ordinal))
                     .Select(request => request.Handle)
                     .ToArray())
        {
            if (_requests.Remove(handle))
            {
                removed++;
            }
        }

        if (removed > 0)
        {
            ApplyResolvedState();
        }

        return removed;
    }

    public void QueueStep(int fixedTicks = 1)
    {
        _simulationLoopController.Step(fixedTicks);
    }

    public TimeFlowSnapshot Snapshot()
    {
        var rawDomains = new List<CoreTimeFlowDomainSnapshot>();
        _coreTimeFlow.FillSnapshots(rawDomains);

        var domains = new List<CoreTimeFlowDomainState>(rawDomains.Count);
        for (int i = 0; i < rawDomains.Count; i++)
        {
            CoreTimeFlowDomainSnapshot domain = rawDomains[i];
            domains.Add(new CoreTimeFlowDomainState
            {
                Name = domain.Name,
                ParentDomainId = domain.ParentDomainId,
                BaseScalePermille = domain.BaseScalePermille,
                EffectiveScalePermille = domain.EffectiveScalePermille,
                Paused = domain.Paused,
                ModifierCount = domain.ModifierCount
            });
        }

        return new TimeFlowSnapshot
        {
            ActiveProfileId = _effectiveProfileId ?? "(baseline)",
            ActiveOwner = _effectiveOwner ?? "(baseline)",
            ActiveRequestCount = _requests.Count,
            GlobalTimeScale = _coreTimeFlow.GetEffectiveScalePermille(CoreTimeFlowDomainIds.Simulation) / 1000f,
            LoopMode = _simulationLoopController.Mode,
            GasMode = _gasClockStepPolicy.Mode,
            GasStepEveryFixedTicks = _gasClockStepPolicy.StepEveryFixedTicks,
            SimulationScalePermille = _coreTimeFlow.GetEffectiveScalePermille(CoreTimeFlowDomainIds.Simulation),
            GasScalePermille = _coreTimeFlow.GetEffectiveScalePermille(CoreTimeFlowDomainIds.Gas),
            PhysicsScalePermille = _coreTimeFlow.GetEffectiveScalePermille(CoreTimeFlowDomainIds.Physics2D),
            NavigationScalePermille = _coreTimeFlow.GetEffectiveScalePermille(CoreTimeFlowDomainIds.Navigation2D),
            TasksScalePermille = _coreTimeFlow.GetEffectiveScalePermille(CoreTimeFlowDomainIds.Tasks),
            PhysicsTargetHz = _physics2DTickPolicy.TargetHz,
            PhysicsMaxStepsPerFixedTick = _physics2DTickPolicy.MaxStepsPerFixedTick,
            NavigationTargetHz = _navigation2DTickPolicy.TargetHz,
            NavigationMaxStepsPerFixedTick = _navigation2DTickPolicy.MaxStepsPerFixedTick,
            Domains = domains
        };
    }

    private void ApplyResolvedState()
    {
        TimeFlowRequest? request = ResolveEffectiveRequest();
        TimeFlowProfile? profile = request?.Profile;

        ClearActiveTokens();

        ApplyDomainScaleOrPause(
            CoreTimeFlowDomainIds.Simulation,
            request?.Owner,
            profile?.Id,
            ResolveSimulationScale(profile));
        ApplyDomainScaleOrPause(
            CoreTimeFlowDomainIds.Gas,
            request?.Owner,
            profile?.Id,
            ResolveGasScale(profile));
        ApplyDomainScaleOrPause(
            CoreTimeFlowDomainIds.Physics2D,
            request?.Owner,
            profile?.Id,
            ResolvePhysicsScale(profile));
        ApplyDomainScaleOrPause(
            CoreTimeFlowDomainIds.Navigation2D,
            request?.Owner,
            profile?.Id,
            ResolveNavigationScale(profile));
        ApplyDomainScaleOrPause(
            CoreTimeFlowDomainIds.Tasks,
            request?.Owner,
            profile?.Id,
            ResolveTasksScale(profile));

        SimulationLoopMode desiredLoopMode = profile?.LoopMode ?? _baseline.LoopMode;
        if (_simulationLoopController.Mode != desiredLoopMode)
        {
            if (desiredLoopMode == SimulationLoopMode.Realtime)
            {
                _simulationLoopController.SetRealtime();
            }
            else
            {
                _simulationLoopController.SetTurnBased();
            }
        }

        _gasClockStepPolicy.SetMode(profile?.GasMode ?? _baseline.GasMode);
        _gasClockStepPolicy.SetStepEveryFixedTicks(profile?.GasStepEveryFixedTicks ?? _baseline.GasStepEveryFixedTicks);
        _physics2DTickPolicy.SetTargetHz(profile?.PhysicsTargetHz ?? _baseline.PhysicsTargetHz);
        _physics2DTickPolicy.SetMaxStepsPerFixedTick(profile?.PhysicsMaxStepsPerFixedTick ?? _baseline.PhysicsMaxStepsPerFixedTick);
        _navigation2DTickPolicy.SetTargetHz(profile?.NavigationTargetHz ?? _baseline.NavigationTargetHz);
        _navigation2DTickPolicy.SetMaxStepsPerFixedTick(profile?.NavigationMaxStepsPerFixedTick ?? _baseline.NavigationMaxStepsPerFixedTick);

        _effectiveProfileId = profile?.Id;
        _effectiveOwner = request?.Owner;
    }

    private int? ResolveSimulationScale(TimeFlowProfile? profile)
    {
        if (profile == null)
        {
            return null;
        }

        if (profile.SimulationScalePermille.HasValue)
        {
            return profile.SimulationScalePermille.Value;
        }

        return profile.GlobalTimeScale.HasValue
            ? ScaleToPermille(profile.GlobalTimeScale.Value)
            : null;
    }

    private static int? ResolveGasScale(TimeFlowProfile? profile) => profile?.GasScalePermille;

    private int? ResolvePhysicsScale(TimeFlowProfile? profile)
    {
        if (profile == null)
        {
            return null;
        }

        if (profile.Physics2DScalePermille.HasValue)
        {
            return profile.Physics2DScalePermille.Value;
        }

        return profile.PhysicsTargetHz.HasValue
            ? ResolveAbsoluteRateScale(profile.PhysicsTargetHz.Value, _baseline.PhysicsTargetHz)
            : null;
    }

    private int? ResolveNavigationScale(TimeFlowProfile? profile)
    {
        if (profile == null)
        {
            return null;
        }

        if (profile.Navigation2DScalePermille.HasValue)
        {
            return profile.Navigation2DScalePermille.Value;
        }

        return profile.NavigationTargetHz.HasValue
            ? ResolveAbsoluteRateScale(profile.NavigationTargetHz.Value, _baseline.NavigationTargetHz)
            : null;
    }

    private static int? ResolveTasksScale(TimeFlowProfile? profile) => profile?.TasksScalePermille;

    private void ApplyDomainScaleOrPause(string domainName, string? owner, string? reason, int? scalePermille)
    {
        if (!scalePermille.HasValue)
        {
            return;
        }

        string appliedOwner = string.IsNullOrWhiteSpace(owner) ? "TimeFlowMod" : owner!;
        string appliedReason = reason ?? string.Empty;
        if (scalePermille.Value <= 0)
        {
            _activeTokens.Add(_coreTimeFlow.AcquirePauseToken(domainName, appliedOwner, appliedReason));
            return;
        }

        _activeTokens.Add(_coreTimeFlow.AcquireScaleToken(domainName, scalePermille.Value, appliedOwner, appliedReason));
    }

    private void ClearActiveTokens()
    {
        for (int i = 0; i < _activeTokens.Count; i++)
        {
            _coreTimeFlow.ReleaseToken(_activeTokens[i]);
        }

        _activeTokens.Clear();
    }

    private static int ScaleToPermille(float scale)
    {
        if (float.IsNaN(scale) || float.IsInfinity(scale) || scale < 0f)
        {
            throw new InvalidOperationException($"Invalid time-flow scale '{scale}'.");
        }

        return (int)MathF.Round(scale * 1000f);
    }

    private static int ResolveAbsoluteRateScale(int absoluteRate, int baselineRate)
    {
        if (absoluteRate < 0)
        {
            throw new InvalidOperationException($"Invalid absolute time-flow rate '{absoluteRate}'.");
        }

        if (baselineRate <= 0)
        {
            return absoluteRate > 0 ? CoreTimeFlowService.DefaultScalePermille : 0;
        }

        return (int)Math.Round((double)absoluteRate * 1000d / baselineRate, MidpointRounding.AwayFromZero);
    }

    private TimeFlowRequest? ResolveEffectiveRequest()
    {
        TimeFlowRequest? best = null;
        foreach (TimeFlowRequest request in _requests.Values)
        {
            if (!best.HasValue ||
                request.Priority > best.Value.Priority ||
                (request.Priority == best.Value.Priority && request.Sequence > best.Value.Sequence))
            {
                best = request;
            }
        }

        return best;
    }

    private readonly record struct BaselineState(
        int SimulationScalePermille,
        int GasScalePermille,
        int PhysicsScalePermille,
        int NavigationScalePermille,
        int TasksScalePermille,
        SimulationLoopMode LoopMode,
        GasStepMode GasMode,
        int GasStepEveryFixedTicks,
        int PhysicsTargetHz,
        int PhysicsMaxStepsPerFixedTick,
        int NavigationTargetHz,
        int NavigationMaxStepsPerFixedTick);

    private readonly record struct TimeFlowRequest(
        int Handle,
        string Owner,
        int Priority,
        long Sequence,
        TimeFlowProfile Profile);
}

public sealed class TimeFlowSnapshot
{
    public string ActiveProfileId { get; init; } = "(baseline)";
    public string ActiveOwner { get; init; } = "(baseline)";
    public int ActiveRequestCount { get; init; }
    public float GlobalTimeScale { get; init; }
    public SimulationLoopMode LoopMode { get; init; }
    public GasStepMode GasMode { get; init; }
    public int GasStepEveryFixedTicks { get; init; }
    public int SimulationScalePermille { get; init; }
    public int GasScalePermille { get; init; }
    public int PhysicsScalePermille { get; init; }
    public int NavigationScalePermille { get; init; }
    public int TasksScalePermille { get; init; }
    public int PhysicsTargetHz { get; init; }
    public int PhysicsMaxStepsPerFixedTick { get; init; }
    public int NavigationTargetHz { get; init; }
    public int NavigationMaxStepsPerFixedTick { get; init; }
    public IReadOnlyList<CoreTimeFlowDomainState> Domains { get; init; } = Array.Empty<CoreTimeFlowDomainState>();
}

public sealed class CoreTimeFlowDomainState
{
    public string Name { get; init; } = string.Empty;
    public int ParentDomainId { get; init; }
    public int BaseScalePermille { get; init; }
    public int EffectiveScalePermille { get; init; }
    public bool Paused { get; init; }
    public int ModifierCount { get; init; }
}
