using System.Threading.Tasks;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace CaseESelectionMod;

/// <summary>
/// Case E selection showcase entry. The base scene remains data-driven; the 10k scene
/// uses the existing runtime spawn queue only to create its larger deterministic field.
/// </summary>
public sealed class CaseESelectionModEntry : IMod
{
    private readonly CaseESelection10kRuntime _runtime = new();

    public void OnLoad(IModContext context)
    {
        context.OnEvent(GameEvents.MapLoaded, _runtime.OnMapLoadedAsync);
        context.OnEvent(GameEvents.MapUnloaded, _runtime.OnMapUnloadedAsync);
        context.OnEvent(GameEvents.GameStart, ctx =>
        {
            GameEngine? engine = ctx.GetEngine();
            if (engine == null)
            {
                return Task.CompletedTask;
            }

            engine.RegisterPresentationSystem(new CaseESelection10kPresentationSystem(engine, _runtime));
            return Task.CompletedTask;
        });
    }

    public void OnUnload() { }
}

internal sealed class CaseESelection10kPresentationSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly CaseESelection10kRuntime _runtime;

    public CaseESelection10kPresentationSystem(GameEngine engine, CaseESelection10kRuntime runtime)
    {
        _engine = engine;
        _runtime = runtime;
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }
    public void Update(in float dt) => _runtime.UpdatePresentation(_engine);
}
