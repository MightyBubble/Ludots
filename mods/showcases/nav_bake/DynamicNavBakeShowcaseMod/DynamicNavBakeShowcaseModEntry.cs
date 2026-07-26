using Arch.System;
using DynamicNavBakeShowcaseMod.Runtime;
using DynamicNavBakeShowcaseMod.Systems;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Navigation;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;

namespace DynamicNavBakeShowcaseMod;

public sealed class DynamicNavBakeShowcaseModEntry : IMod
{
    private DynamicNavBakeShowcaseRuntime? _runtime;
    private DynamicNavBakeShowcaseActions? _actions;
    private IModContext? _context;
    private ISystem<float>? _localOrderSourceSystem;
    private ISystem<float>? _playerControlSystem;
    private ISystem<float>? _presentationSystem;
    private ISystem<float>? _fixedStepSystem;

    public void OnLoad(IModContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        context.Log("[DynamicNavBakeShowcaseMod] Loaded");
        _runtime = new DynamicNavBakeShowcaseRuntime();
        _actions = new DynamicNavBakeShowcaseActions(_runtime);
        context.OnEvent(GameEvents.MapLoaded, HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapResumed, HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapUnloaded, HandleMapUnloadedAsync);
    }

    public void OnUnload()
    {
    }

    private async Task HandleMapFocusedAsync(ScriptContext context)
    {
        if (_runtime == null || _actions == null)
        {
            return;
        }

        await _runtime.HandleMapFocusedAsync(context).ConfigureAwait(false);
        GameEngine? engine = context.GetEngine();
        if (engine == null || !DynamicNavBakeShowcaseIds.IsShowcaseMap(engine.CurrentMapSession?.MapId.Value))
        {
            return;
        }

        engine.GlobalContext[DynamicNavBakeShowcaseIds.RuntimeServiceKey] = _actions;
        EnsureLocalOrderSourceSystem(engine);
        EnsurePlayerControlSystem(engine);
        EnsureFixedStepSystem(engine);
        EnsurePresentationSystems(engine);
    }

    private Task HandleMapUnloadedAsync(ScriptContext context)
    {
        GameEngine? engine = context.GetEngine();
        if (engine != null)
        {
            UnregisterPresentationSystems(engine);

            if (_fixedStepSystem != null)
            {
                ISystem<float> registeredFixed = _fixedStepSystem;
                if (!engine.UnregisterSystem(registeredFixed, SystemGroup.AbilityActivation))
                {
                    throw new InvalidOperationException(
                        "DynamicNavBakeShowcaseMod failed to unregister FixedStep orchestration system on map unload.");
                }

                _fixedStepSystem = null;
            }

            if (_playerControlSystem != null)
            {
                ISystem<float> registeredControl = _playerControlSystem;
                if (!engine.UnregisterSystem(registeredControl, SystemGroup.InputCollection))
                {
                    throw new InvalidOperationException(
                        "DynamicNavBakeShowcaseMod failed to unregister player control system on map unload.");
                }

                _playerControlSystem = null;
            }

            if (_localOrderSourceSystem != null)
            {
                ISystem<float> registeredInput = _localOrderSourceSystem;
                if (!engine.UnregisterSystem(registeredInput, SystemGroup.InputCollection))
                {
                    throw new InvalidOperationException(
                        "DynamicNavBakeShowcaseMod failed to unregister local order source system on map unload.");
                }

                _localOrderSourceSystem = null;
            }
        }

        return _runtime?.HandleMapUnloadedAsync(context) ?? Task.CompletedTask;
    }

    private void EnsureLocalOrderSourceSystem(GameEngine engine)
    {
        if (_localOrderSourceSystem != null)
        {
            return;
        }

        IModContext context = _context
            ?? throw new InvalidOperationException(
                "DynamicNavBakeShowcaseMod requires IModContext before registering local player orders.");
        OrderQueue orders = engine.GetService(CoreServiceKeys.OrderQueue)
            ?? throw new InvalidOperationException(
                "DynamicNavBakeShowcaseMod requires OrderQueue before registering local player orders.");
        DynamicNavBakeShowcaseRuntime runtime = _runtime
            ?? throw new InvalidOperationException(
                "DynamicNavBakeShowcaseMod requires Runtime before registering local player orders.");
        var system = new DynamicNavBakeShowcaseLocalOrderSourceSystem(
            engine.World,
            engine.GlobalContext,
            orders,
            context,
            runtime);
        engine.RegisterSystem(system, SystemGroup.InputCollection);
        _localOrderSourceSystem = system;
    }

    private void EnsurePlayerControlSystem(GameEngine engine)
    {
        if (_playerControlSystem != null)
        {
            return;
        }

        DynamicNavBakeShowcaseRuntime runtime = _runtime
            ?? throw new InvalidOperationException(
                "DynamicNavBakeShowcaseMod requires Runtime before registering player construction control.");
        var system = new DynamicNavBakeShowcasePlayerControlSystem(engine, runtime);
        engine.RegisterSystem(system, SystemGroup.InputCollection);
        _playerControlSystem = system;
    }

    private void EnsureFixedStepSystem(GameEngine engine)
    {
        if (_fixedStepSystem != null)
        {
            return;
        }

        DynamicNavBakeShowcaseRuntime runtime = _runtime
            ?? throw new InvalidOperationException(
                "DynamicNavBakeShowcaseMod requires Runtime before registering the FixedStep system.");

        // Append after MassNavigationMovePlanExecutionSystem (installed into AbilityActivation during MassNavigation bootstrap).
        var system = new DynamicNavBakeShowcaseFixedStepSystem(engine, runtime);
        engine.RegisterSystem(system, SystemGroup.AbilityActivation);
        _fixedStepSystem = system;
    }

    private void EnsurePresentationSystems(GameEngine engine)
    {
        if (_presentationSystem != null)
        {
            return;
        }

        DynamicNavBakeShowcaseRuntime runtime = _runtime
            ?? throw new InvalidOperationException(
                "DynamicNavBakeShowcaseMod requires Runtime before registering the presentation system.");
        DynamicNavBakeShowcaseActions actions = _actions
            ?? throw new InvalidOperationException(
                "DynamicNavBakeShowcaseMod requires Actions before registering the presentation system.");

        PresentationAdapterCapabilities? capabilities = null;
        if (engine.TryGetService(CoreServiceKeys.PresentationAdapterCapabilities, out PresentationAdapterCapabilities declared))
        {
            capabilities = declared;
        }

        NavMeshPresentationCapabilityValidator.Require(capabilities);
        _ = engine.GetService(CoreServiceKeys.NavMeshPresentationState)
            ?? throw new InvalidOperationException(
                "DynamicNavBakeShowcaseMod requires the Core-owned NavMeshPresentationState service.");
        _ = engine.GetService(CoreServiceKeys.NavMeshPresentationBuffer)
            ?? throw new InvalidOperationException(
                "DynamicNavBakeShowcaseMod requires the Core-owned NavMeshPresentationBuffer service.");

        var showcasePresentation = new DynamicNavBakeShowcasePresentationSystem(engine, runtime, actions);

        // Core owns the NavMesh projector; this Mod only contributes showcase-specific UI/path requests.
        engine.InsertPresentationSystemBefore<PerformerRuleSystem>(showcasePresentation);
        _presentationSystem = showcasePresentation;
    }

    private void UnregisterPresentationSystems(GameEngine engine)
    {
        if (_presentationSystem != null)
        {
            ISystem<float> registeredPresentation = _presentationSystem;
            if (!engine.UnregisterPresentationSystem(registeredPresentation))
            {
                throw new InvalidOperationException(
                    "DynamicNavBakeShowcaseMod failed to unregister DynamicNavBakeShowcasePresentationSystem on map unload.");
            }

            _presentationSystem = null;
        }
    }
}
