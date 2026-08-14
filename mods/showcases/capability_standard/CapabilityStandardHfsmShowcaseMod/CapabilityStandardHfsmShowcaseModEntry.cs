using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CapabilityStandardHfsmShowcaseMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Modding;
using Ludots.Core.Presentation;
using Ludots.Core.Scripting;

namespace CapabilityStandardHfsmShowcaseMod;

public sealed class CapabilityStandardHfsmShowcaseModEntry : IMod
{
    private readonly CapabilityStandardHfsmShowcaseRuntime _runtime = new();
    private CapabilityStandardHfsmGraphDebugBrowserHost? _graphDebugBrowserHost;

    public void OnLoad(IModContext context)
    {
        context.Log("[CapabilityStandardHfsmShowcaseMod] Loaded");

        context.OnEvent(GameEvents.GameStart, async ctx =>
        {
            GameEngine? engine = ctx.GetEngine();
            if (engine == null)
            {
                return;
            }

            engine.SetService(CoreServiceKeys.BenchmarkSceneController, (IBenchmarkSceneController)_runtime);
            engine.GlobalContext[CapabilityStandardHfsmShowcaseRuntime.RuntimeKey] = _runtime;
            engine.RegisterSystem(new CapabilityStandardHfsmShowcaseSimulationSystem(_runtime), SystemGroup.InputCollection);
            engine.RegisterPresentationSystem(new CapabilityStandardHfsmShowcasePresentationSystem(engine, _runtime));

            if (engine.GetService(CoreServiceKeys.InputFrameConsumers) is not List<IInputFrameConsumer> inputConsumers)
            {
                throw new InvalidOperationException("HFSM showcase shortcuts require InputFrameConsumers.");
            }

            if (!inputConsumers.Contains(_runtime))
            {
                inputConsumers.Add(_runtime);
            }

            _graphDebugBrowserHost = new CapabilityStandardHfsmGraphDebugBrowserHost(engine, _runtime, context);
            await _graphDebugBrowserHost.TryInstallAsync(ctx).ConfigureAwait(false);
        });

        context.OnEvent(GameEvents.MapLoaded, _runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapResumed, _runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapUnloaded, _runtime.HandleMapUnloadedAsync);
    }

    public void OnUnload()
    {
        _graphDebugBrowserHost?.Dispose();
        _graphDebugBrowserHost = null;
    }
}
