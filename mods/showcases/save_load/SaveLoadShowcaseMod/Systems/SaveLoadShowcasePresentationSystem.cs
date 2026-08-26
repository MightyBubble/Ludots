using Ludots.Core.Engine;
using SaveLoadShowcaseMod.Runtime;

namespace SaveLoadShowcaseMod.Systems;

internal sealed class SaveLoadShowcasePresentationSystem : Arch.System.ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly SaveLoadShowcaseRuntime _runtime;

    public SaveLoadShowcasePresentationSystem(GameEngine engine, SaveLoadShowcaseRuntime _runtime)
    {
        _engine = engine;
        this._runtime = _runtime;
    }

    public void Initialize() { }
    public void BeforeUpdate(in float t) { }
    public void AfterUpdate(in float t) { }
    public void Dispose() { }

    public void Update(in float t) => _runtime.AdvanceFixedStep(_engine);
}
