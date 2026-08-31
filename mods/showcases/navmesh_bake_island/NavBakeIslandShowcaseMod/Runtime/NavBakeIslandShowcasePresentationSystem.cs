using System;
using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.MassNavigation;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace NavBakeIslandShowcaseMod.Runtime;

internal sealed class NavBakeIslandShowcasePresentationSystem : ISystem<float>
{
    private static readonly QueryDescription AnimatorQuery = new QueryDescription()
        .WithAll<PresenterState, PresenterAnimatorSlot>();

    private readonly GameEngine _engine;
    private readonly ScreenOverlayBuffer _overlay;
    private readonly PresenterAnimatorStateBuffer _animatorStates;
    private readonly AnimatorControllerRegistry _controllers;
    private string _status = string.Empty;
    private string _animation = string.Empty;
    private string _firstStateName = string.Empty;
    private string _secondStateName = string.Empty;
    private int _controllerId;
    private int _firstPackedStateIndex = -1;
    private int _secondPackedStateIndex = -1;
    private int _lastAgentCount = -1;
    private int _lastMovingCount = -1;
    private int _lastRouteCount = -1;
    private int _lastAnimatorCount = -1;
    private int _lastIdleCount = -1;
    private int _lastWalkingCount = -1;

    public NavBakeIslandShowcasePresentationSystem(
        GameEngine engine,
        ScreenOverlayBuffer overlay,
        PresenterAnimatorStateBuffer animatorStates,
        AnimatorControllerRegistry controllers)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _overlay = overlay ?? throw new ArgumentNullException(nameof(overlay));
        _animatorStates = animatorStates ?? throw new ArgumentNullException(nameof(animatorStates));
        _controllers = controllers ?? throw new ArgumentNullException(nameof(controllers));
    }

    public void Initialize()
    {
    }

    public void BeforeUpdate(in float dt)
    {
    }

    public void Update(in float dt)
    {
        if (!string.Equals(
                _engine.CurrentMapSession?.MapConfig?.Id,
                "nav_bake_island",
                StringComparison.Ordinal))
        {
            return;
        }

        MassNavigationRuntimeBinding binding = _engine.GetService(MassNavigationKeys.RuntimeBinding)
            ?? throw new InvalidOperationException("NavBake Island showcase requires MassNavigation runtime binding.");
        if (!binding.IsReady)
        {
            return;
        }

        MassNavigationSimulationRuntime simulation = binding.RequireCurrent();
        MassNavigationRouteExecutionSink routeSink = _engine.GetService(MassNavigationKeys.RouteExecutionSink)
            ?? throw new InvalidOperationException("NavBake Island showcase requires MassNavigation route execution.");
        int agentCount = simulation.NavigationAgentCount;
        int movingCount = Math.Max(0, agentCount - simulation.NavigationSettledAgentCount);
        CountAnimatorStates(out int animatorCount, out int firstStateCount, out int secondStateCount);

        if (agentCount != _lastAgentCount ||
            movingCount != _lastMovingCount ||
            routeSink.ActiveRouteCount != _lastRouteCount)
        {
            _status = $"NAV BAKE ISLAND | agents {agentCount} | moving {movingCount} | routes {routeSink.ActiveRouteCount}";
            _lastAgentCount = agentCount;
            _lastMovingCount = movingCount;
            _lastRouteCount = routeSink.ActiveRouteCount;
        }

        if (animatorCount != _lastAnimatorCount ||
            firstStateCount != _lastIdleCount ||
            secondStateCount != _lastWalkingCount)
        {
            _animation = animatorCount == 0
                ? $"ANIMATOR | waiting for presenter assignment | agents {agentCount}"
                : $"ANIMATOR | active {animatorCount}/{agentCount} | {_firstStateName} {firstStateCount} | {_secondStateName} {secondStateCount}";
            _lastAnimatorCount = animatorCount;
            _lastIdleCount = firstStateCount;
            _lastWalkingCount = secondStateCount;
        }

        RequireOverlayItem(_overlay.AddRect(
            16,
            16,
            620,
            142,
            new System.Numerics.Vector4(0.03f, 0.05f, 0.07f, 0.86f),
            new System.Numerics.Vector4(0.28f, 0.68f, 0.86f, 0.95f),
            stableId: 14020,
            dirtySerial: 1));
        RequireOverlayItem(_overlay.AddText(30, 28, "Mass Navigation + KayKit Animator", 22, new System.Numerics.Vector4(0.94f, 0.97f, 1f, 1f), 14021, 1));
        RequireOverlayItem(_overlay.AddText(30, 58, _status, 15, new System.Numerics.Vector4(0.78f, 0.91f, 0.98f, 1f), 14022, _lastAgentCount ^ (_lastMovingCount << 8) ^ (_lastRouteCount << 16)));
        RequireOverlayItem(_overlay.AddText(30, 82, _animation, 15, new System.Numerics.Vector4(0.98f, 0.84f, 0.52f, 1f), 14023, _lastAnimatorCount ^ (_lastIdleCount << 8) ^ (_lastWalkingCount << 16)));
        RequireOverlayItem(_overlay.AddText(30, 108, "LEFT DRAG select | RIGHT CLICK move | WHEEL zoom | M minimap", 13, new System.Numerics.Vector4(0.84f, 0.87f, 0.92f, 1f), 14024, 1));
        RequireOverlayItem(_overlay.AddText(30, 130, "Blue Azure Shore Guard | Red Crimson Ridge Scouts | light + heavy profiles", 12, new System.Numerics.Vector4(0.70f, 0.76f, 0.82f, 1f), 14025, 1));
    }

    public void AfterUpdate(in float dt)
    {
    }

    public void Dispose()
    {
    }

    private void CountAnimatorStates(out int activeCount, out int firstStateCount, out int secondStateCount)
    {
        activeCount = 0;
        firstStateCount = 0;
        secondStateCount = 0;
        foreach (ref var chunk in _engine.World.Query(in AnimatorQuery))
        {
            Span<PresenterState> states = chunk.GetSpan<PresenterState>();
            Span<PresenterAnimatorSlot> slots = chunk.GetSpan<PresenterAnimatorSlot>();
            foreach (int index in chunk)
            {
                Entity owner = states[index].OwnerEntity;
                if (owner == Entity.Null ||
                    !_engine.World.IsAlive(owner) ||
                    !_engine.World.Has<MassNavigationAgent>(owner))
                {
                    continue;
                }

                int slot = slots[index].Value;
                if (slot < 0)
                {
                    continue;
                }

                AnimatorPackedState packed = _animatorStates.GetPackedStateBySlot(slot);
                if ((packed.GetFlags() & AnimatorPackedStateFlags.Active) == 0)
                {
                    continue;
                }

                activeCount++;
                if (!_controllers.TryGet(packed.GetControllerId(), out AnimatorControllerDefinition controller))
                {
                    throw new InvalidOperationException($"Animator controller id {packed.GetControllerId()} is not registered.");
                }

                int packedStateIndex = packed.GetPrimaryStateIndex();
                EnsureStateContract(controller);
                if (packedStateIndex == _firstPackedStateIndex)
                {
                    firstStateCount++;
                }
                else if (packedStateIndex == _secondPackedStateIndex)
                {
                    secondStateCount++;
                }
            }
        }
    }

    private void EnsureStateContract(AnimatorControllerDefinition controller)
    {
        if (_controllerId == controller.ControllerId)
        {
            return;
        }

        if (_controllerId != 0)
        {
            throw new InvalidOperationException(
                $"NavBake Island agents use multiple animator controllers: {_controllerId} and {controller.ControllerId}.");
        }

        if (controller.States.Length != 2 ||
            string.IsNullOrWhiteSpace(controller.States[0].Name) ||
            string.IsNullOrWhiteSpace(controller.States[1].Name))
        {
            throw new InvalidOperationException(
                "NavBake Island locomotion controller must expose exactly two named states.");
        }

        _controllerId = controller.ControllerId;
        _firstStateName = controller.States[0].Name;
        _secondStateName = controller.States[1].Name;
        _firstPackedStateIndex = controller.States[0].PackedStateIndex;
        _secondPackedStateIndex = controller.States[1].PackedStateIndex;
    }

    private static void RequireOverlayItem(bool added)
    {
        if (!added)
        {
            throw new InvalidOperationException("NavBake Island HUD exceeded ScreenOverlayBuffer capacity.");
        }
    }
}
