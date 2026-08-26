using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;
using SaveLoadShowcaseMod.Runtime;

namespace SaveLoadShowcaseMod.Systems;

internal sealed class SaveLoadShowcaseInputSystem : Arch.System.ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly SaveLoadShowcaseRuntime _runtime;

    public SaveLoadShowcaseInputSystem(GameEngine engine, SaveLoadShowcaseRuntime runtime)
    {
        _engine = engine;
        _runtime = runtime;
    }

    public void Initialize() { }
    public void BeforeUpdate(in float t) { }
    public void AfterUpdate(in float t) { }
    public void Dispose() { }

    public void Update(in float t)
    {
        if (_engine.GetService(CoreServiceKeys.InputHandler) is not PlayerInputHandler input) return;
        if (input.PressedThisFrame(SaveLoadShowcaseIds.NudgeWorld)) _runtime.NudgeWorld();
        if (input.PressedThisFrame(SaveLoadShowcaseIds.AblateReset)) _runtime.AblateReset();
        if (input.PressedThisFrame(SaveLoadShowcaseIds.AblateRestore)) _runtime.AblateRestore();
        if (input.PressedThisFrame(SaveLoadShowcaseIds.TamperSlot)) _runtime.TamperSelectedSlot();
        if (input.PressedThisFrame(SaveLoadShowcaseIds.ToggleExclude)) _runtime.ToggleExclude();
        if (input.PressedThisFrame(SaveLoadShowcaseIds.ColdStartStory)) _runtime.ColdStartStory();
        if (input.PressedThisFrame(SaveLoadShowcaseIds.RetentionDown)) _runtime.AdjustRetention(-1);
        if (input.PressedThisFrame(SaveLoadShowcaseIds.RetentionUp)) _runtime.AdjustRetention(1);
    }
}
